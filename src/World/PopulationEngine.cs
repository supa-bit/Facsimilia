using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

public enum CapitalKind { Country = 0, Province = 1 }

public readonly record struct StartCapital(int Node, CapitalKind Kind, int Realm);

/// <summary>
/// Population &amp; settlement engine, stages 1-2 (see MECHANICS.md,
/// "Population &amp; settlement engine"). Runs on a coarse node grid - one
/// node per HYDE 5-arcminute cell, 780x456 over the map's extent, each
/// covering ~10.5x12 ownership-grid cells - because HYDE is the finest
/// population truth there is to compute against.
///
/// Each yearly tick:
///   0. Growth drivers (PopulationEngine.Growth.cs): each geographic region's
///      new total P_r from natural increase, then migration between
///      neighbouring regions.
///   1. Blend simulated heat with historical heat:
///        H_sim   = normalize(drivers + Beta * (Pop / max Pop)^(1/K))
///        H_final = (1 - Alpha) * H_sim + Alpha * H_hist
///      where drivers saturate with the node's attraction (DriverMods).
///   2. Turn heat into a target population, each region's total preserved:
///        Target_i = P_r * H_final_i^K / sum_(j in r) H_final_j^K
///      then move each node a limited step toward it, on a log scale:
///        Pop_i &lt;- Pop_i * (Target_i / Pop_i)^Mu
///      and rescale the region so it holds exactly P_r again.
///   3. Historical ceiling: every node not owned by the player's realm is
///      clamped to what HYDE says that place held in the current year.
///
/// H_hist, the ceiling, and the 300 BC starting population all come from
/// the same HYDE keyframes (tools/build_population_mask.py), linearly
/// interpolated between snapshots. H_hist = (pop / max pop)^(1/K), so
/// Target reproduces HYDE exactly when nothing has diverged: the
/// historical start is a fixed point, and nothing drifts until play
/// changes the drivers.
///
/// Constants are calibrated in tools/calibrate_population.py --hyde, on the
/// real 300 BC grid, against how long real founded cities took to reach the
/// world top 50 / top 10.
///
/// Per-node state is stored as float (as HYDE and the saves are) and all
/// arithmetic is done in double, which reproduces the original GDScript
/// engine's results exactly.
/// </summary>
public partial class PopulationEngine : RefCounted
{
    public const double Alpha = 0.175;   // historical pull on H_final
    public const double Beta = 5.0;      // how strongly existing population attracts more
    public const double Mu = 0.065;      // share of the log gap to target closed per year (refitted on the regional engine)
    public const double K = 2.0;         // urbanization exponent (ancient/agrarian eras)
    // Rate a lost player city's excess fades back to history: ~60 years to
    // halve it on a log scale. Kept apart from Mu, which the growth fit set.
    public const double LegacyMu = 0.0115;

    // One-time resettlement when a city first becomes a capital, as a share of
    // the largest node's population, drawn from the realm's other nodes.
    public static readonly double[] CapitalTransfer = { 0.08, 0.04 };
    // Added to the capital node's drivers for as long as it stays capital.
    public static readonly double[] CapitalDriverBonus = { 0.05, 0.0 };

    public const int ResettleYears = 25;   // an emptied node resettles on its own after a generation
    public const double TargetFloor = 0.5; // log-scale math needs a positive target; below 1 person rounds to 0
    public const int NotEmpty = int.MinValue;
    public const int RedistributePasses = 8;

    public const string MetaPath = "res://data/population/hyde_meta.json";
    const string DataDir = "res://data/population/";

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int[] LandNodes { get; private set; } = Array.Empty<int>();  // sea is never touched
    public int Year { get; private set; }

    // Historical data, sorted by year.
    readonly List<(int Year, float[] Pop)> _keyframes = new();
    public IReadOnlyList<(int Year, float[] Pop)> Keyframes => _keyframes;
    public float[] HistPop { get; private set; } = Array.Empty<float>();  // HYDE population at the current year = the ceiling
    public float[] HHist { get; private set; } = Array.Empty<float>();    // normalized historical heat

    // Simulation state.
    public float[] Pop { get; private set; } = Array.Empty<float>();
    public float[] Legacy { get; private set; } = Array.Empty<float>();      // player-built excess that fades after the player loses a node
    public int[] EmptySince { get; private set; } = Array.Empty<int>();      // year a node emptied, or NotEmpty
    public float[] DriverMods { get; private set; } = Array.Empty<float>();  // attraction per node (pull minus push), written by gameplay systems
    public float[] StartBonus { get; private set; } = Array.Empty<float>();  // capital bonus already baked into the 300 BC history
    public int[] NodeOwner { get; private set; } = Array.Empty<int>();       // realm id per node (0 = unclaimed)
    public int PlayerRealmId { get; private set; }
    readonly Dictionary<int, (CapitalKind Kind, int Realm)> _capitals = new();
    readonly HashSet<string> _designated = new();  // "realm:node:kind"; resettlement happens once
    public double SeedFallback { get; private set; }  // median rural node population, for player foundings on empty land

    // Per-tick scratch buffers, reused so a tick allocates nothing.
    float[] _heat = Array.Empty<float>();
    float[] _target = Array.Empty<float>();
    float[] _bonus = Array.Empty<float>();

    public static bool HydeAvailable() => Godot.FileAccess.FileExists(MetaPath);

    /// <summary>Loads the baked HYDE keyframes. Returns false if they're missing.</summary>
    public bool LoadHyde()
    {
        if (!HydeAvailable())
            return false;
        HydeMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<HydeMeta>(Godot.FileAccess.GetFileAsString(MetaPath));
        }
        catch (JsonException)
        {
            meta = null;
        }
        if (meta?.keyframes == null)
        {
            GD.PushError($"PopulationEngine: {MetaPath} is not valid JSON");
            return false;
        }
        int n = meta.width * meta.height;
        var years = new List<int>();
        var arrays = new List<float[]>();
        foreach (var kf in meta.keyframes)
        {
            byte[] gz = Godot.FileAccess.GetFileAsBytes(DataDir + kf.file);
            using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
            var values = new float[n];
            Span<byte> bytes = MemoryMarshal.AsBytes(values.AsSpan());
            int read = 0;
            while (read < bytes.Length)
            {
                int got = input.Read(bytes[read..]);
                if (got == 0)
                    break;
                read += got;
            }
            // Little-endian float32, as written; every Godot target is little-endian.
            if (read != bytes.Length || input.ReadByte() != -1)
            {
                GD.PushError($"PopulationEngine: keyframe {kf.file} has the wrong size");
                return false;
            }
            years.Add(kf.year);
            arrays.Add(values);
        }
        SetKeyframes(meta.width, meta.height, years, arrays);
        LoadRegions();
        return true;
    }

    sealed record HydeMeta(int width, int height, List<HydeKeyframe> keyframes);
    sealed record HydeKeyframe(int year, string file);

    /// <summary>
    /// Keyframe arrays: people per node, row-major, negative = sea. The first
    /// keyframe's sea mask applies to all of them.
    /// </summary>
    public void SetKeyframes(int width, int height, IReadOnlyList<int> years, IReadOnlyList<float[]> arrays)
    {
        Width = width;
        Height = height;
        _keyframes.Clear();
        for (int i = 0; i < years.Count; i++)
            _keyframes.Add((years[i], arrays[i]));
        _keyframes.Sort((a, b) => a.Year.CompareTo(b.Year));

        float[] first = _keyframes[0].Pop;
        var land = new List<int>();
        var rural = new List<float>();
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] >= 0f)
            {
                land.Add(i);
                if (first[i] >= 1f)
                    rural.Add(first[i]);
            }
        }
        LandNodes = land.ToArray();
        rural.Sort();
        SeedFallback = rural.Count > 0 ? rural[rural.Count / 2] : 1.0;

        int n = width * height;
        Pop = new float[n]; Legacy = new float[n]; DriverMods = new float[n]; StartBonus = new float[n];
        HistPop = new float[n]; HHist = new float[n];
        EmptySince = new int[n];
        NodeOwner = new int[n];
        _heat = new float[n]; _target = new float[n]; _bonus = new float[n];
        DefaultRegions();
    }

    /// <summary>
    /// Begins the simulation at startYear with every node at its historical
    /// population. startCapitals are capitals that already exist then: their
    /// bonus counts as part of the recorded history (backed out of the
    /// baseline drivers) and triggers no resettlement.
    /// </summary>
    public void Start(int startYear, IEnumerable<StartCapital>? startCapitals = null)
    {
        Year = startYear;
        UpdateHistory();
        Array.Clear(Pop);
        Array.Clear(Legacy);
        Array.Clear(DriverMods);
        Array.Clear(StartBonus);
        Array.Fill(EmptySince, NotEmpty);
        _capitals.Clear();
        _designated.Clear();
        Array.Clear(RegionFactors);
        foreach (int i in LandNodes)
        {
            Pop[i] = HistPop[i] >= 1f ? HistPop[i] : 0f;
            if (Pop[i] == 0f)
                EmptySince[i] = Year - ResettleYears;  // historically empty, not freshly razed
        }
        if (startCapitals == null)
            return;
        foreach (var c in startCapitals)
        {
            _capitals[c.Node] = (c.Kind, c.Realm);
            _designated.Add(DesignationKey(c.Realm, c.Node, c.Kind));
            StartBonus[c.Node] = (float)Math.Min(CapitalDriverBonus[(int)c.Kind], HHist[c.Node]);
        }
    }

    /// <summary>
    /// Ownership per node: realm id at each node, and which realm is the
    /// player's. Nodes owned by anyone else - bots, or 0 for unclaimed - are
    /// held to the historical ceiling.
    /// </summary>
    public void SetOwnership(int[] owners, int playerRealmId)
    {
        NodeOwner = owners;
        PlayerRealmId = playerRealmId;
    }

    public bool IsPlayerNode(int i) => PlayerRealmId != 0 && NodeOwner[i] == PlayerRealmId;

    int PlayerOrNone => PlayerRealmId != 0 ? PlayerRealmId : -1;

    /// <summary>
    /// Makes node a capital of realm. The first time a given city becomes that
    /// realm's capital of that kind, settlers move in from the realm's other
    /// nodes (moved, not created). Bots can't be resettled past the
    /// historical ceiling.
    /// </summary>
    public void DesignateCapital(int node, CapitalKind kind, int realm)
    {
        _capitals[node] = (kind, realm);
        if (!_designated.Add(DesignationKey(realm, node, kind)))
            return;

        double amount = CapitalTransfer[(int)kind] * MaxPop();
        if (!IsPlayerNode(node))
            amount = Math.Min(amount, Math.Max(0.0, HistPop[node] - Pop[node]));
        double donorsTotal = 0.0;
        foreach (int i in LandNodes)
            if (i != node && NodeOwner[i] == realm)
                donorsTotal += Pop[i];
        // Never strip a realm of more than half of everyone else it has.
        amount = Math.Min(amount, donorsTotal * 0.5);
        if (amount <= 0.0)
            return;
        double share = amount / donorsTotal;
        foreach (int i in LandNodes)
            if (i != node && NodeOwner[i] == realm)
                Pop[i] = (float)(Pop[i] - Pop[i] * share);
        Pop[node] = (float)(Pop[node] + amount);
        EmptySince[node] = NotEmpty;
    }

    public void ClearCapital(int node) => _capitals.Remove(node);

    static string DesignationKey(int realm, int node, CapitalKind kind) => $"{realm}:{node}:{(int)kind}";

    /// <summary>Advances the simulation to newYear (normally Year + 1).</summary>
    public void Tick(int newYear)
    {
        Array.Copy(_histRegion, _histRegionPrev, _histRegion.Length);
        Year = newYear;
        UpdateHistory();
        int[] nodes = LandNodes;
        float[] hh = HHist, hp = HistPop, pop = Pop, heat = _heat, target = _target;
        int[] owners = NodeOwner;
        byte[] reg = NodeRegion;
        int pid = PlayerOrNone;

        // Stage 0: each region's total for the new year.
        SumRegions();
        NaturalIncrease();
        MigrateBetweenRegions();

        // Stage 1: blend simulated and historical heat.
        double maxPop = MaxPop();
        double invMax = maxPop > 0.0 ? 1.0 / maxPop : 0.0;
        float[] sb = StartBonus, mods = DriverMods, bonus = CapitalBonusArray();
        double heatMax = 0.0;
        foreach (int i in nodes)
        {
            double drivers = DriversOf(Math.Max(0.0, (double)hh[i] - sb[i]), mods[i]) + bonus[i];
            double v = drivers + Beta * HeatOfShare(pop[i] * invMax);
            heat[i] = (float)v;
            heatMax = Math.Max(heatMax, v);
        }

        // Stage 2: heat -> target population, each region's total preserved.
        double invHeat = heatMax > 0.0 ? 1.0 / heatMax : 0.0;
        Array.Clear(_regionWeight);
        foreach (int i in nodes)
        {
            double hf = (1.0 - Alpha) * heat[i] * invHeat + Alpha * hh[i];
            double w = K == 2.0 ? hf * hf : Math.Pow(hf, K);
            heat[i] = (float)w;  // reuse: now holds the target weight
            _regionWeight[reg[i]] += w;
        }
        // An empty node takes a share only if it's due to resettle and its
        // share would be at least one person; otherwise the share would be
        // people nobody can hold, and the region would lose them.
        int[] es = EmptySince;
        int y = Year;
        for (int r = 1; r <= RegionCount; r++)
            _regionWeight[r] = _regionWeight[r] > 0.0 ? _regionGoal[r] / _regionWeight[r] : 0.0;
        foreach (int i in nodes)
        {
            if (pop[i] > 0f)
                continue;
            if (y - es[i] < ResettleYears || heat[i] * _regionWeight[reg[i]] < 1.0)
                heat[i] = 0f;
        }
        Array.Clear(_regionWeight);
        foreach (int i in nodes)
            _regionWeight[reg[i]] += heat[i];
        for (int r = 1; r <= RegionCount; r++)
            _regionWeight[r] = _regionWeight[r] > 0.0 ? _regionGoal[r] / _regionWeight[r] : 0.0;
        foreach (int i in nodes)
            target[i] = (float)(heat[i] * _regionWeight[reg[i]]);

        // Historical ceiling on targets, excess redistributed among other
        // non-player nodes of the region with headroom.
        UpdateLegacy();
        float[] lg = Legacy;
        ApplyCeilingToTargets(target);

        // Log-scale step toward target; empty nodes resettle after a generation.
        foreach (int i in nodes)
        {
            double p = pop[i];
            double t = target[i];
            if (p > 0.0)
            {
                p *= Math.Pow(Math.Max(t, TargetFloor) / p, Mu);
                if (p < 1.0)
                {
                    p = 0.0;
                    es[i] = y;
                }
            }
            else if (t >= 1.0 && y - es[i] >= ResettleYears)
            {
                p = SeedPopulation(i);
                if (p >= 1.0)
                    es[i] = NotEmpty;
                else
                    p = 0.0;
            }
            // The ceiling applies to the population itself, not just the
            // target - otherwise bot-held places would lag history's declines.
            if (owners[i] != pid)
                p = Math.Min(p, Math.Max(hp[i], lg[i]));
            pop[i] = (float)p;
        }

        // The step moves each node a share of its gap, which doesn't keep a
        // sum: rescale each region back to the total its targets add up to.
        Renormalize(target);
        foreach (int i in nodes)
            if (owners[i] == pid)
                lg[i] = pop[i];  // starts fading from here if the player loses it
    }

    /// <summary>Population share of the largest node -> heat, the inverse of Target's H^K.</summary>
    static double HeatOfShare(double share) => K == 2.0 ? Math.Sqrt(share) : Math.Pow(share, 1.0 / K);

    float[] CapitalBonusArray()
    {
        Array.Clear(_bonus);
        foreach (var (node, c) in _capitals)
            _bonus[node] = (float)CapitalDriverBonus[(int)c.Kind];
        return _bonus;
    }

    /// <summary>
    /// What a founding or natural resettlement on empty node i starts with:
    /// HYDE's population there this year; for the player only, the median
    /// rural node if history had nobody there. Others are clamped to the ceiling.
    /// </summary>
    public double SeedPopulation(int i)
    {
        double s = HistPop[i];
        if (s < 1.0 && IsPlayerNode(i))
            s = SeedFallback;
        if (!IsPlayerNode(i))
            s = Math.Min(s, Allowance(i));
        return s;
    }

    /// <summary>
    /// Most people a non-player node may hold: the historical ceiling, or the
    /// fading remainder of what the player built there, whichever is larger.
    /// </summary>
    double Allowance(int i) => Math.Max(HistPop[i], Legacy[i]);

    /// <summary>
    /// Player-built excess on nodes the player no longer holds relaxes toward
    /// the ceiling on a log-scale LegacyMu curve (~60 years to halve).
    /// </summary>
    void UpdateLegacy()
    {
        float[] lg = Legacy, hp = HistPop;
        int[] owners = NodeOwner;
        int pid = PlayerOrNone;
        foreach (int i in LandNodes)
        {
            double l = lg[i];
            if (l <= 0.0 || owners[i] == pid)
                continue;
            double ceiling = Math.Max(hp[i], TargetFloor);
            lg[i] = l <= ceiling ? 0f : (float)(l * Math.Pow(ceiling / l, LegacyMu));
        }
    }

    /// <summary>
    /// Caps non-player targets at their allowance. The excess goes first to
    /// other non-player nodes of the same region with headroom, in
    /// proportion to their targets; what still doesn't fit goes to the
    /// player's nodes in the region, but only as far as the region is above
    /// history - growth only the player can cause. The rest is never born.
    /// </summary>
    void ApplyCeilingToTargets(float[] target)
    {
        int[] owners = NodeOwner;
        float[] hp = HistPop, lg = Legacy;
        int pid = PlayerOrNone;
        for (int r = 1; r <= RegionCount; r++)
        {
            int[] nodes = _regionNodes[r];
            double excess = 0.0;
            foreach (int i in nodes)
            {
                if (owners[i] == pid)
                    continue;
                double cap = Math.Max(hp[i], lg[i]);
                if (target[i] > cap)
                {
                    excess += target[i] - cap;
                    target[i] = (float)cap;
                }
            }
            for (int pass = 0; pass < RedistributePasses && excess >= 1.0; pass++)
            {
                double roomWeight = 0.0;
                foreach (int i in nodes)
                    if (owners[i] != pid && target[i] < Math.Max(hp[i], lg[i]))
                        roomWeight += target[i];
                if (roomWeight <= 0.0)
                    break;
                double leftover = 0.0;
                double k = excess / roomWeight;
                foreach (int i in nodes)
                {
                    if (owners[i] == pid)
                        continue;
                    double cap = Math.Max(hp[i], lg[i]);
                    if (target[i] < cap)
                    {
                        double t = target[i] * (1.0 + k);
                        if (t > cap)
                        {
                            leftover += t - cap;
                            t = cap;
                        }
                        target[i] = (float)t;
                    }
                }
                excess = leftover;
            }
            double surplus = Math.Min(excess, Math.Max(0.0, _regionGoal[r] - _histRegion[r]));
            if (surplus >= 1.0 && _regionPlayerPop[r] > 0.0)
            {
                double playerWeight = 0.0;
                foreach (int i in nodes)
                    if (owners[i] == pid)
                        playerWeight += target[i];
                if (playerWeight > 0.0)
                {
                    double k = surplus / playerWeight;
                    foreach (int i in nodes)
                        if (owners[i] == pid)
                            target[i] = (float)(target[i] * (1.0 + k));
                }
            }
        }
    }

    /// <summary>
    /// Rescales each region's nodes so the region holds what its targets add
    /// up to. Growth goes to nodes with room (non-player nodes stop at their
    /// allowance); shrinking scales everyone. Empty nodes stay empty.
    /// </summary>
    void Renormalize(float[] target)
    {
        int[] owners = NodeOwner;
        float[] pop = Pop, hp = HistPop, lg = Legacy;
        int pid = PlayerOrNone;
        for (int r = 1; r <= RegionCount; r++)
        {
            int[] nodes = _regionNodes[r];
            double goal = 0.0;
            foreach (int i in nodes)
                goal += target[i];
            for (int pass = 0; pass < RedistributePasses; pass++)
            {
                double sum = 0.0, free = 0.0;
                foreach (int i in nodes)
                {
                    double p = pop[i];
                    sum += p;
                    if (p > 0.0 && (owners[i] == pid || p < Math.Max(hp[i], lg[i])))
                        free += p;
                }
                double diff = goal - sum;
                if (Math.Abs(diff) < 0.01 || sum <= 0.0)
                    break;
                if (diff < 0.0)
                {
                    double f = goal / sum;
                    foreach (int i in nodes)
                        pop[i] = (float)(pop[i] * f);
                    break;
                }
                if (free <= 0.0)
                    break;  // every node is at its ceiling: the rest is never born
                double g = 1.0 + diff / free;
                foreach (int i in nodes)
                {
                    double p = pop[i];
                    if (p <= 0.0)
                        continue;
                    if (owners[i] == pid)
                        pop[i] = (float)(p * g);
                    else
                    {
                        double cap = Math.Max(hp[i], lg[i]);
                        if (p < cap)
                            pop[i] = (float)Math.Min(p * g, cap);
                    }
                }
            }
        }
    }

    /// <summary>Interpolates HYDE to the current year: the ceiling and H_hist.</summary>
    void UpdateHistory()
    {
        var a = _keyframes[0];
        var b = _keyframes[0];
        foreach (var kf in _keyframes)
        {
            if (kf.Year <= Year)
                a = kf;
            if (kf.Year >= Year)
            {
                b = kf;
                break;
            }
        }
        if (b.Year < Year)
            b = a;  // past the last keyframe: hold it
        double f = b.Year == a.Year ? 0.0 : (double)(Year - a.Year) / (b.Year - a.Year);
        float[] pa = a.Pop, pb = b.Pop, hp = HistPop, hh = HHist;
        double maxHist = 0.0;
        foreach (int i in LandNodes)
        {
            double from = Math.Max(pa[i], 0.0f), to = Math.Max(pb[i], 0.0f);
            double v = from + (to - from) * f;
            hp[i] = (float)v;
            maxHist = Math.Max(maxHist, v);
        }
        double invMax = maxHist > 0.0 ? 1.0 / maxHist : 0.0;
        foreach (int i in LandNodes)
            hh[i] = (float)HeatOfShare(hp[i] * invMax);
        // Regional history counts whole people only: a node HYDE gives less
        // than one person starts empty (see Start), and counting its
        // fraction would make sparse regions look under-populated forever.
        Array.Clear(_histRegion);
        byte[] reg = NodeRegion;
        foreach (int i in LandNodes)
            if (hp[i] >= 1f)
                _histRegion[reg[i]] += hp[i];
    }

    double MaxPop()
    {
        float[] p = Pop;
        double m = 0.0;
        foreach (int i in LandNodes)
            m = Math.Max(m, p[i]);
        return m;
    }

    // --- Queries --------------------------------------------------------------

    public double TotalPopulation()
    {
        double s = 0.0;
        foreach (int i in LandNodes)
            s += Pop[i];
        return s;
    }

    public double RealmPopulation(int realm)
    {
        double s = 0.0;
        foreach (int i in LandNodes)
            if (NodeOwner[i] == realm)
                s += Pop[i];
        return s;
    }

    /// <summary>World rank of node i by population (1 = largest).</summary>
    public int RankOf(int i)
    {
        float[] p = Pop;
        float mine = p[i];
        int r = 1;
        foreach (int j in LandNodes)
            if (p[j] > mine)
                r++;
        return r;
    }

    // --- Grid mapping ---------------------------------------------------------
    // Nodes and ownership cells share the map's linear lon/lat projection, so
    // the mapping is a plain rescale.

    public int NodeAtCell(int x, int y, int gridWidth, int gridHeight)
    {
        int nx = Math.Clamp(x * Width / gridWidth, 0, Width - 1);
        int ny = Math.Clamp(y * Height / gridHeight, 0, Height - 1);
        return ny * Width + nx;
    }

    public int NodeAtLonLat(double lon, double lat, double lonMin, double lonMax, double latMin, double latMax)
    {
        int nx = Math.Clamp((int)((lon - lonMin) / (lonMax - lonMin) * Width), 0, Width - 1);
        int ny = Math.Clamp((int)((latMax - lat) / (latMax - latMin) * Height), 0, Height - 1);
        return ny * Width + nx;
    }

    /// <summary>
    /// Samples each node's owner from the ownership grid at the node's centre
    /// cell. Sea cells (seaOwner) count as unclaimed.
    /// </summary>
    public int[] OwnershipFromGrid(int[] cells, int gridWidth, int gridHeight, int seaOwner)
    {
        var owners = new int[Width * Height];
        foreach (int i in LandNodes)
        {
            int nx = i % Width, ny = i / Width;
            int cx = (int)((nx + 0.5) * gridWidth / Width);
            int cy = (int)((ny + 0.5) * gridHeight / Height);
            int o = cells[cy * gridWidth + cx];
            owners[i] = o == seaOwner ? 0 : o;
        }
        return owners;
    }

    // --- Save / load ----------------------------------------------------------
    // Same keys and packed-array types as the original GDScript engine, so
    // saves stay compatible.

    public GDictionary ToDict()
    {
        var caps = new GArray();
        foreach (var (node, c) in _capitals)
            caps.Add(new GDictionary { ["node"] = node, ["kind"] = (int)c.Kind, ["realm"] = c.Realm });
        var designated = new GArray();
        foreach (string key in _designated)
            designated.Add(key);
        var d = new GDictionary
        {
            ["year"] = Year,
            ["pop"] = Pop,
            ["legacy"] = Legacy,
            ["empty_since"] = EmptySince,
            ["driver_mods"] = DriverMods,
            ["start_bonus"] = StartBonus,
            ["capitals"] = caps,
            ["designated"] = designated,
        };
        SaveGrowth(d);
        return d;
    }

    /// <summary>Requires SetKeyframes()/LoadHyde() first, with the same grid size.</summary>
    public bool LoadFromDict(GDictionary data)
    {
        int n = Width * Height;
        foreach (string key in new[] { "pop", "legacy", "driver_mods", "start_bonus" })
        {
            if (!data.TryGetValue(key, out Variant v) || v.VariantType != Variant.Type.PackedFloat32Array
                || v.AsFloat32Array().Length != n)
            {
                GD.PushError($"PopulationEngine: saved '{key}' doesn't match this node grid");
                return false;
            }
        }
        Year = data["year"].AsInt32();
        Pop = data["pop"].AsFloat32Array();
        Legacy = data["legacy"].AsFloat32Array();
        DriverMods = data["driver_mods"].AsFloat32Array();
        StartBonus = data["start_bonus"].AsFloat32Array();
        EmptySince = data["empty_since"].AsInt32Array();
        _capitals.Clear();
        foreach (Variant c in data["capitals"].AsGodotArray())
        {
            var d = c.AsGodotDictionary();
            _capitals[d["node"].AsInt32()] = ((CapitalKind)d["kind"].AsInt32(), d["realm"].AsInt32());
        }
        _designated.Clear();
        foreach (Variant k in data["designated"].AsGodotArray())
            _designated.Add(k.AsString());
        LoadGrowth(data);
        UpdateHistory();
        return true;
    }
}
