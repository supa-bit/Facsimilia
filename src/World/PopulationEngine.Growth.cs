using System;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>
/// Natural-increase factors, each scored -1..+1 per region against that
/// region's historical baseline (0 = as history had it). See MECHANICS.md,
/// "Channel 1: natural increase, per region".
/// </summary>
public enum GrowthFactor
{
    Food = 0,       // harvest and storage against consumption
    Disease = 1,    // density, sanitation, marshes, epidemic-spreading trade
    Urban = 2,      // the "urban graveyard": share of people in large towns
    Security = 3,   // war, raids and occupation; long peace
    Land = 4,       // land per person and living standards
    Burden = 5,     // taxes, tribute, forced labour, estates
    Health = 6,     // medicine, public health, famine relief
    Climate = 7,    // harvest luck; not player-controlled
}

/// <summary>
/// Growth drivers: what makes people and what moves them (MECHANICS.md,
/// "Growth drivers"). Two channels, each bounded:
///
///   1. Natural increase decides how many people each region has:
///        P_r(t+1) = P_r(t) * H_r(t+1)/H_r(t) * (1 + deviation + recovery)
///      H_r is HYDE's regional total, so a region nobody touches follows its
///      own history exactly. The factor deviation is clamped to -2% .. +0.5%
///      a year; only the player's share of a region gets the positive part.
///      Positive growth slows to zero at the region's carrying capacity.
///      A region knocked below history recovers toward it (plentiful land
///      after a catastrophe).
///   2. Attraction decides where people live. Between neighbouring regions,
///      people drift toward stronger pull, at most 0.2% of the source's
///      people a year, until crowding balances the pull. Within a region,
///      the calibrated heat/target/log-step engine places them, and the
///      region's total is then held exactly: moving never creates or
///      destroys anyone.
/// </summary>
public partial class PopulationEngine
{
    public const int FactorCount = 8;

    // Channel 1 bounds (ancient and medieval eras), from HYDE's regional
    // growth before 1700: -0.7% .. +0.6% a year.
    public const double MaxFactorGrowth = 0.005;   // +0.5% a year above history, at most
    public const double MaxFactorDecline = 0.02;   // -2% a year below history, at most

    // Starting weights (open: set by the balance tests). Each is the factor's
    // share of the positive bound (Up) and of the negative bound (Down).
    public static readonly double[] FactorUp = { 0.30, 0.00, 0.00, 0.20, 0.25, 0.10, 0.15, 0.00 };
    public static readonly double[] FactorDown = { 0.25, 0.20, 0.10, 0.25, 0.05, 0.10, 0.00, 0.05 };

    // Carrying capacity: provisional, a multiple of the region's historical
    // population until the Resources density field gives each region its
    // arable land and yield.
    public const double CapacityMultiple = 3.0;

    // A region below history regrows toward it: this share of the log gap
    // a year (a catastrophe's half-loss is back to 90% in ~120 years),
    // never faster than RecoveryMax a year.
    public const double RecoveryRate = 0.015;
    public const double RecoveryMax = 0.01;

    // Channel 2 between regions.
    public const double MigrationCap = 0.002;   // at most 0.2% of the source region's people a year
    public const double MigrationPull = 0.35;   // pull gap worth one unit of log crowding: ~1.4x at full pull
    // No place is more attractive than the best site on the map (drivers 1.0),
    // the case the founded-city calibration measured.
    public const double CapEra = 1.0;
    // An attraction score this high is effectively full: drivers = CapEra.
    public const float FullAttraction = 20f;

    /// <summary>Gameplay factor scores, [region * FactorCount + factor], each -1..+1. Region 0 is unused.</summary>
    public float[] RegionFactors { get; private set; } = Array.Empty<float>();

    /// <summary>Off only in tests: regions then change only by migration.</summary>
    public bool NaturalIncreaseEnabled { get; set; } = true;

    // Per-region working state, reused every tick. Index = region id.
    double[] _histRegion = Array.Empty<double>();
    double[] _histRegionPrev = Array.Empty<double>();
    double[] _regionPop = Array.Empty<double>();
    double[] _regionPlayerPop = Array.Empty<double>();
    double[] _regionAllowance = Array.Empty<double>();
    double[] _regionPull = Array.Empty<double>();
    double[] _regionGoal = Array.Empty<double>();
    double[] _regionInflow = Array.Empty<double>();
    double[] _regionWeight = Array.Empty<double>();
    double[] _regionExcess = Array.Empty<double>();
    double[] _flowScratch = Array.Empty<double>();
    double[] _goalSnap = Array.Empty<double>();

    void AllocateRegionState(int count)
    {
        int n = count + 1;
        RegionFactors = new float[n * FactorCount];
        _histRegion = new double[n]; _histRegionPrev = new double[n];
        _regionPop = new double[n]; _regionPlayerPop = new double[n]; _regionAllowance = new double[n];
        _regionPull = new double[n]; _regionGoal = new double[n]; _regionInflow = new double[n];
        _regionWeight = new double[n]; _regionExcess = new double[n];
        _flowScratch = new double[n]; _goalSnap = new double[n];
    }

    public float GetFactor(int region, GrowthFactor f) => RegionFactors[region * FactorCount + (int)f];

    public void SetFactor(int region, GrowthFactor f, double score) =>
        RegionFactors[region * FactorCount + (int)f] = (float)Math.Clamp(score, -1.0, 1.0);

    /// <summary>Saturation: 0 at 0, rising with diminishing returns toward 1.</summary>
    public static double Sat(double x) => 1.0 - Math.Exp(-x);

    /// <summary>
    /// Drivers from a node's baseline heat and its attraction score A (pull
    /// minus push): pull closes a saturating share of the gap up to CapEra,
    /// push removes a saturating share of the baseline.
    /// </summary>
    public static double DriversOf(double baseline, double attraction) =>
        attraction >= 0.0
            ? baseline + (CapEra - baseline) * Sat(attraction)
            : baseline * (1.0 - Sat(-attraction));

    /// <summary>
    /// The factor deviation for a region, a yearly rate within
    /// -MaxFactorDecline .. +MaxFactorGrowth, before the player-share and
    /// capacity rules.
    /// </summary>
    public double FactorDeviation(int region)
    {
        double dev = 0.0;
        int o = region * FactorCount;
        for (int f = 0; f < FactorCount; f++)
        {
            double s = RegionFactors[o + f];
            dev += s >= 0.0 ? s * FactorUp[f] * MaxFactorGrowth : s * FactorDown[f] * MaxFactorDecline;
        }
        return Math.Clamp(dev, -MaxFactorDecline, MaxFactorGrowth);
    }

    /// <summary>
    /// An event (epidemic, famine, massacre) removes a share of a region's
    /// people outright, outside the rate bounds. share is capped at 0.35 per
    /// call, a Black Death-scale year.
    /// </summary>
    public void ApplyShock(int region, double share)
    {
        double keep = 1.0 - Math.Clamp(share, 0.0, 0.35);
        foreach (int i in _regionNodes[region])
        {
            double p = Pop[i] * keep;
            if (p < 1.0 && Pop[i] > 0f)
            {
                p = 0.0;
                EmptySince[i] = Year;
            }
            Pop[i] = (float)p;
        }
    }

    /// <summary>Sums per region: people, the player's people, and the most the region may hold for bots.</summary>
    void SumRegions()
    {
        Array.Clear(_regionPop); Array.Clear(_regionPlayerPop); Array.Clear(_regionAllowance);
        Array.Clear(_regionPull);
        int pid = PlayerOrNone;
        float[] pop = Pop, hp = HistPop, lg = Legacy, mods = DriverMods;
        int[] owners = NodeOwner;
        byte[] reg = NodeRegion;
        foreach (int i in LandNodes)
        {
            int r = reg[i];
            double p = pop[i];
            _regionPop[r] += p;
            if (owners[i] == pid)
                _regionPlayerPop[r] += p;
            double allowance = Math.Max(hp[i], lg[i]);
            if (allowance >= 1.0)
                _regionAllowance[r] += allowance;
            double a = mods[i];
            if (a != 0.0 && p > 0.0)
                _regionPull[r] += p * (a > 0.0 ? Sat(a) : -Sat(-a));
        }
        for (int r = 1; r <= RegionCount; r++)
            _regionPull[r] = _regionPop[r] > 0.0 ? _regionPull[r] / _regionPop[r] : 0.0;
    }

    /// <summary>Channel 1: each region's total for the new year, into _regionGoal.</summary>
    void NaturalIncrease()
    {
        for (int r = 1; r <= RegionCount; r++)
        {
            double p = _regionPop[r];
            if (!NaturalIncreaseEnabled || p <= 0.0)
            {
                _regionGoal[r] = p;
                continue;
            }
            double hist = _histRegion[r], prev = _histRegionPrev[r];
            double ratio = prev > 0.0 && hist > 0.0 ? hist / prev : 1.0;
            double cap = CapacityMultiple * hist;
            double dev = FactorDeviation(r);
            double growth;
            if (dev > 0.0)
            {
                // Only the player's share of a region grows above history,
                // and never past the carrying capacity.
                double playerShare = _regionPlayerPop[r] / p;
                growth = dev * playerShare * (cap > 0.0 ? Math.Max(0.0, 1.0 - p / cap) : 0.0);
            }
            else
            {
                growth = dev;
            }
            if (p > hist && ratio > 1.0 && cap > hist)
                ratio = 1.0 + (ratio - 1.0) * Math.Clamp((cap - p) / (cap - hist), 0.0, 1.0);
            if (hist > 0.0 && p < hist)
                growth += Math.Min(RecoveryMax, RecoveryRate * Math.Log(hist / p));
            double next = p * ratio * (1.0 + growth);
            if (cap > 0.0)
                next = Math.Min(next, Math.Max(p, cap));  // above capacity (history fell): no growth
            _regionGoal[r] = next;
        }
    }

    /// <summary>
    /// Channel 2 between regions: a net drift toward neighbours with stronger
    /// pull, capped at MigrationCap of the source a year, until the crowding
    /// (people relative to history) balances the pull gap. Conserves people:
    /// a region without the player can only take in what its historical
    /// allowance has room for.
    /// </summary>
    void MigrateBetweenRegions()
    {
        int count = RegionCount;
        if (count < 2)
            return;
        Array.Clear(_regionInflow);
        double[] outflow = _flowScratch;
        Array.Clear(outflow);
        Array.Copy(_regionGoal, _goalSnap, _regionGoal.Length);  // every gap is read from the same state
        // Pass 1: what each region would send, and where.
        for (int r = 1; r <= count; r++)
        {
            double goal = _regionGoal[r], hist = _histRegion[r];
            if (goal <= 0.0 || hist <= 0.0)
                continue;
            double crowdR = Math.Log(goal / hist);
            double dMax = 0.0, dSum = 0.0;
            foreach (int s in _neighbours[r])
            {
                double d = PullGap(r, s, crowdR);
                if (d > 0.0)
                {
                    dSum += d;
                    dMax = Math.Max(dMax, d);
                }
            }
            if (dSum <= 0.0)
                continue;
            double total = MigrationCap * goal * dMax;
            outflow[r] = total;
            foreach (int s in _neighbours[r])
            {
                double d = PullGap(r, s, crowdR);
                if (d > 0.0)
                    _regionInflow[s] += total * d / dSum;
            }
        }
        // Pass 2: regions without the player can't be filled past their
        // allowance; scale what flows into them.
        double[] accept = _regionWeight;   // reused as the accepted share per destination
        for (int s = 1; s <= count; s++)
        {
            accept[s] = 1.0;
            if (_regionInflow[s] > 0.0 && _regionPlayerPop[s] <= 0.0)
            {
                double room = Math.Max(0.0, _regionAllowance[s] - _regionGoal[s]);
                if (_regionInflow[s] > room)
                    accept[s] = room / _regionInflow[s];
            }
        }
        // Pass 3: apply, sender and receiver moving the same people.
        Array.Clear(_regionInflow);
        for (int r = 1; r <= count; r++)
        {
            if (outflow[r] <= 0.0)
                continue;
            double goal = _goalSnap[r];
            double crowdR = Math.Log(goal / _histRegion[r]);
            double dSum = 0.0;
            foreach (int s in _neighbours[r])
                dSum += Math.Max(0.0, PullGap(r, s, crowdR));
            double sent = 0.0;
            foreach (int s in _neighbours[r])
            {
                double d = PullGap(r, s, crowdR);
                if (d <= 0.0)
                    continue;
                double flow = outflow[r] * d / dSum * accept[s];
                _regionInflow[s] += flow;
                sent += flow;
            }
            _regionGoal[r] -= sent;
        }
        for (int s = 1; s <= count; s++)
            _regionGoal[s] += _regionInflow[s];
    }

    /// <summary>How strongly people in r are drawn to s (0..1), or ≤ 0 if not at all.</summary>
    double PullGap(int r, int s, double crowdR)
    {
        double goal = _goalSnap[s], hist = _histRegion[s];
        if (goal <= 0.0 || hist <= 0.0)
            return 0.0;
        double gap = MigrationPull * (_regionPull[s] - _regionPull[r]) - (Math.Log(goal / hist) - crowdR);
        return Math.Min(1.0, gap / MigrationPull);
    }

    // --- Save / load of the growth inputs -------------------------------------

    void SaveGrowth(GDictionary d) => d["region_factors"] = RegionFactors;

    void LoadGrowth(GDictionary data)
    {
        Array.Clear(RegionFactors);
        if (data.TryGetValue("region_factors", out Variant v) && v.VariantType == Variant.Type.PackedFloat32Array)
        {
            float[] saved = v.AsFloat32Array();
            if (saved.Length == RegionFactors.Length)
                RegionFactors = saved;
        }
    }
}
