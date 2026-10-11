using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>A road you have drawn but not yet ordered: its way, length and price.</summary>
public sealed class RoadPlan
{
    public RoadKind Kind { get; init; } = null!;
    public List<int> Nodes { get; init; } = new();
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public double Km { get; init; }
    public int Bridges { get; init; }
    public double Silver { get; init; }
    public double Workers { get; init; }
    public int Years { get; init; }
    /// <summary>Why it can't be built, or null.</summary>
    public string? Problem { get; init; }
}

/// <summary>
/// Roads on the map (the Ledger topic "Buildings and works", your answers):
/// you click a town and then another; the road finds the easiest ground
/// through your own land. Kinds by material, faster and carrying more as
/// they improve. A road costs silver by length, ground and bridges, workers
/// from manpower while it is built, and years (fewer as the realm learns
/// transport and construction), then upkeep. On it everyone marches faster,
/// invaders too, but you can break your own; it brings trade to the
/// provinces it crosses, ties far provinces to the capital, and spreads
/// ideas. Unpaid roads decay a kind at a time. Other realms build history's
/// roads on their dates.
/// </summary>
public partial class MapView
{
    /// <summary>Years in a row of unpaid upkeep before a road falls a kind.</summary>
    public const int RoadNeglectYears = 3;
    /// <summary>Repairing a broken road costs this share of building it again, and a year.</summary>
    public const double RoadRepairShare = 0.25;
    /// <summary>Roads bring at most this much research (ideas travel along them)...</summary>
    public const double RoadResearchMax = 0.1;
    /// <summary>...a realm reaching it with this many km of built road.</summary>
    public const double RoadResearchKm = 5000;
    /// <summary>A click snaps to a town within this many km.</summary>
    public const double RoadSnapKm = 40;

    sbyte[]? _roadRank;
    bool[]? _canal;
    /// <summary>Each node's manned frontier wall: its realm, or 0; and the km it costs a stranger to cross.</summary>
    int[]? _wallRealm;
    float[]? _wallCross;

    /// <summary>The march lost crossing onto a manned wall of a realm not one's own or an ally's (0 otherwise).</summary>
    double WallCrossing(int i, int j, int realm)
    {
        if (realm <= 0)
            return 0;
        RoadRanks();
        if (_wallRealm == null || j < 0 || j >= _wallRealm.Length || i < 0 || i >= _wallRealm.Length)
            return 0;
        int owner = _wallRealm[j];
        if (owner <= 0 || owner == realm || _wallRealm[i] == owner || Game.Treaties.Allied(realm, owner))
            return 0;
        return _wallCross![j];
    }

    /// <summary>Garrison Might from manned walls running through a province.</summary>
    double WallGarrison(int realm, int provinceId)
    {
        double g = 0;
        var cat = RoadCatalog.Instance;
        foreach (var r in Game.Roads)
            if (r.Realm == realm && r.Built && r.Manned && !r.Broken && cat[r.Kind] is { Wall: true } kind
                && r.Nodes.Any(n => ProvinceOfNode(n) == provinceId))
                g = Math.Max(g, kind.ProvinceGarrison);
        return g;
    }

    /// <summary>Whether boats can pass between two land nodes by a canal.</summary>
    bool CanalStep(int i, int j)
    {
        RoadRanks();
        return _canal != null && i >= 0 && j >= 0 && i < _canal.Length && j < _canal.Length && _canal[i] && _canal[j];
    }
    bool _roadsDirty = true;

    /// <summary>Each node's best road kind (rank), or -1: built, unbroken roads only.</summary>
    sbyte[] RoadRanks()
    {
        if (_roadRank != null && !_roadsDirty)
            return _roadRank;
        int n = Population == null ? 0 : Population.Width * Population.Height;
        if (_roadRank == null || _roadRank.Length != n)
            _roadRank = new sbyte[n];
        Array.Fill(_roadRank, (sbyte)-1);
        if (_canal == null || _canal.Length != n)
            _canal = new bool[n];
        Array.Fill(_canal, false);
        if (_wallRealm == null || _wallRealm.Length != n)
        {
            _wallRealm = new int[n];
            _wallCross = new float[n];
        }
        Array.Fill(_wallRealm, 0);
        var cat = RoadCatalog.Instance;
        foreach (var r in Game.Roads)
        {
            if (!r.Built || r.Broken || cat[r.Kind] is not { } kind)
                continue;
            if (kind.Water)
            {
                foreach (int node in r.Nodes)
                    if (node >= 0 && node < n)
                        _canal[node] = true;
                continue;
            }
            if (kind.Wall)
            {
                if (r.Manned)
                    foreach (int node in r.Nodes)
                        if (node >= 0 && node < n)
                        {
                            _wallRealm[node] = r.Realm;
                            _wallCross![node] = (float)kind.CrossKm;
                        }
                continue;
            }
            foreach (int node in r.Nodes)
                if (node >= 0 && node < n && _roadRank[node] < kind.Rank)
                    _roadRank[node] = (sbyte)kind.Rank;
        }
        _roadsDirty = false;
        return _roadRank;
    }

    /// <summary>The road's share of a step's marching cost between two nodes on a road (1 off the road).</summary>
    double RoadPace(int i, int j)
    {
        var ranks = RoadRanks();
        if (i < 0 || j < 0 || i >= ranks.Length || j >= ranks.Length)
            return 1;
        int rank = Math.Min(ranks[i], ranks[j]);
        return rank < 0 ? 1 : RoadCatalog.Instance.Kinds[rank].Pace;
    }

    /// <summary>How much faster a realm builds roads: more the more of transport and construction it knows.</summary>
    public double RoadBuildSpeed(int realmId)
    {
        var tech = TechCatalog.Instance;
        var branch = tech.All.Where(t => t.Branch is "transport" or "construction").ToList();
        if (branch.Count == 0)
            return 1;
        var known = Game.Realm(realmId).Techs;
        return 1 + 3.0 * branch.Count(t => known.Contains(t.Id)) / branch.Count;
    }

    /// <summary>The cheapest way for a road between two nodes over land; only through the realm's own land unless anyLand.</summary>
    List<int>? RoadPath(int realmId, int from, int to, bool anyLand, double maxRugged = 1000)
    {
        if (Population == null || from < 0 || to < 0)
            return null;
        EnsureMoveCost();
        var pop = Population;
        int w = pop.Width, h = pop.Height;
        bool rugged = maxRugged < 1000 && Land != null && Land.Has("ruggedness");
        bool Open(int j) => pop.NodeRegion[j] != 0 && (anyLand || pop.NodeOwner[j] == realmId) && (!rugged || Land!.Value("ruggedness", j) <= maxRugged);
        if (!Open(from) || !Open(to))
            return null;
        var cost = new Dictionary<int, double> { [from] = 0 };
        var prev = new Dictionary<int, int>();
        int tx = to % w, ty = to / w;
        double minX = _stepKmX.Min();
        double Guess(int i) => Math.Max(Math.Abs(i % w - tx) * minX, Math.Abs(i / w - ty) * _stepKmY);
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(from, Guess(from));
        while (queue.TryDequeue(out int i, out _))
        {
            if (i == to)
                break;
            double c = cost[i];
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    if (!Open(j))
                        continue;
                    double next = c + StepCost(i, j, false) / RoadPace(i, j);   // the ground's own cost, not today's roads
                    if (!cost.TryGetValue(j, out var old) || next < old - 1e-9)
                    {
                        cost[j] = next;
                        prev[j] = i;
                        queue.Enqueue(j, next + Guess(j));
                    }
                }
            if (cost.Count > 400000)
                return null;
        }
        if (!prev.ContainsKey(to))
            return null;
        var path = new List<int> { to };
        for (int k = to; k != from; k = prev[k])
            path.Add(prev[k]);
        path.Reverse();
        return path;
    }

    /// <summary>Length (km of ground, not counting the terrain) and river crossings of a chain of nodes.</summary>
    (double Km, int Bridges) RoadMeasure(List<int> nodes)
    {
        var pop = Population!;
        int w = pop.Width;
        double km = 0;
        int bridges = 0;
        bool wasRiver = false;
        for (int k = 0; k < nodes.Count; k++)
        {
            int i = nodes[k];
            if (k > 0)
            {
                int j = nodes[k - 1];
                double ex = i % w != j % w ? _stepKmX[i / w] : 0, ey = i / w != j / w ? _stepKmY : 0;
                km += Math.Sqrt(ex * ex + ey * ey);
            }
            bool river = Land != null && Land.Has("river") && Land.Value("river", i) >= 0.3;
            if (river && !wasRiver)
                bridges++;
            wasRiver = river;
        }
        return (km, bridges);
    }

    /// <summary>The ground's roughness along a way, as a cost factor (1 on open ground).</summary>
    double RoadGround(List<int> nodes)
    {
        if (nodes.Count == 0 || _moveCost == null)
            return 1;
        return nodes.Average(i => Math.Max(1, (double)_moveCost[i]));
    }

    /// <summary>Whether a land node lies by the sea or on a river.</summary>
    bool TouchesWater(int node)
    {
        if (Population == null || node < 0)
            return false;
        var pop = Population;
        int w = pop.Width;
        if (Land != null && Land.Has("river") && Land.Value("river", node) >= 0.3)
            return true;
        int x = node % w, y = node / w;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < pop.Height && pop.NodeRegion[ny * w + nx] == 0)
                    return true;
            }
        return false;
    }

    /// <summary>The water's edge nearest a point on the map, in the realm's land, within a few nodes: for canals.</summary>
    public (string Name, int Node)? WaterNear(Vector2 world, int realmId)
    {
        if (Population == null)
            return null;
        var pop = Population;
        int at = pop.NodeAtCell((int)(world.X / CellPixels), (int)(world.Y / CellPixels), GridWidth, GridHeight);
        if (at < 0)
            return null;
        int w = pop.Width;
        for (int r = 0; r <= 4; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = at % w + dx, y = at / w + dy;
                    if (x < 0 || y < 0 || x >= w || y >= pop.Height)
                        continue;
                    int j = y * w + x;
                    if (pop.NodeRegion[j] != 0 && pop.NodeOwner[j] == realmId && TouchesWater(j))
                    {
                        var town = TownNear(NodeCell(j) * CellPixels, 0);
                        return (town?.Name ?? (Provinces?.Provinces.TryGetValue(ProvinceOfNode(j), out var p) == true ? p.Name : "the shore"), j);
                    }
                }
        return null;
    }

    /// <summary>The town nearest a point on the map, within reach, in the given realm's land (any realm if 0).</summary>
    public (string Name, int Node)? TownNear(Vector2 world, int realmId)
    {
        if (Population == null)
            return null;
        (string, int)? best = null;
        double bestKm = RoadSnapKm;
        double kmPerPixel = (LatMax - LatMin) * 111.2 / (GridHeight * CellPixels);
        foreach (var (name, at, _) in TownsNow())
        {
            double km = at.DistanceTo(world) * kmPerPixel;
            if (km > bestKm)
                continue;
            int node = Population.NodeAtCell((int)(at.X / CellPixels), (int)(at.Y / CellPixels), GridWidth, GridHeight);
            if (node < 0 || Population.NodeRegion[node] == 0 || (realmId > 0 && Population.NodeOwner[node] != realmId))
                continue;
            best = (name, node);
            bestKm = km;
        }
        return best;
    }

    /// <summary>Works out a road between two nodes for a realm: its way and its price.</summary>
    public RoadPlan PlanRoad(int realmId, RoadKind kind, int from, int to, string fromName, string toName)
    {
        var rs = Game.Realm(realmId);
        if (kind.Requires != null && !rs.Techs.Contains(kind.Requires))
            return new RoadPlan { Kind = kind, From = fromName, To = toName, Problem = $"Needs the technology: {TechCatalog.Instance[kind.Requires]?.Name ?? kind.Requires}." };
        if (kind.Water && (!TouchesWater(from) || !TouchesWater(to)))
            return new RoadPlan { Kind = kind, From = fromName, To = toName, Problem = "A canal joins two waters: start and end it by the sea or a river." };
        var nodes = RoadPath(realmId, from, to, false, kind.Water ? kind.MaxRugged : 1000);
        if (nodes == null || nodes.Count < 2)
            return new RoadPlan { Kind = kind, From = fromName, To = toName, Problem = kind.Water
                ? "No level way through your own land between them (locks would let a canal climb hills)."
                : "No way through your own land between them." };
        return PriceRoad(realmId, kind, nodes, fromName, toName);
    }

    /// <summary>The price of a road, canal or wall along a chain of nodes.</summary>
    RoadPlan PriceRoad(int realmId, RoadKind kind, List<int> nodes, string fromName, string toName)
    {
        var rs = Game.Realm(realmId);
        var (km, bridges) = RoadMeasure(nodes);
        var cat = RoadCatalog.Instance;
        if (kind.Water || kind.Wall)
            bridges = 0;
        double silver = kind.Cost * km * RoadGround(nodes) + bridges * cat.BridgeCost;
        double workers = kind.Workers * km;
        int years = Math.Max(1, (int)Math.Ceiling(km / (cat.KmPerYear * RoadBuildSpeed(realmId))));
        string? problem = rs.Treasury < silver ? $"Not enough silver: {Money(silver)} needed."
            : rs.Manpower < workers ? $"Needs {workers:N0} workers from your manpower ({rs.Manpower:N0} free)." : null;
        return new RoadPlan { Kind = kind, Nodes = nodes, From = fromName, To = toName, Km = km, Bridges = bridges, Silver = silver, Workers = workers, Years = years, Problem = problem };
    }

    /// <summary>Draws a wall on to a new point: the way there through your own land is added to it.</summary>
    public RoadPlan? ExtendWall(int realmId, RoadKind kind, RoadPlan? sofar, int fromNode, int toNode, string fromName, string toName)
    {
        var leg = RoadPath(realmId, fromNode, toNode, false);
        if (leg == null || leg.Count < 2)
            return null;
        var nodes = new List<int>(sofar?.Nodes ?? new List<int>());
        nodes.AddRange(nodes.Count > 0 ? leg.Skip(1) : leg);
        return PriceRoad(realmId, kind, nodes, sofar?.From ?? fromName, toName);
    }

    /// <summary>Orders a planned road: silver paid, workers taken from manpower.</summary>
    public Road? BuildRoad(int realmId, RoadPlan plan, string history = "", bool free = false)
    {
        if (plan.Problem != null && !free)
            return null;
        if (!free)
        {
            var rs = Game.Realm(realmId);
            rs.Treasury -= plan.Silver;
            rs.Manpower -= plan.Workers;
        }
        var road = new Road
        {
            Id = Game.NextRoadId++, Kind = plan.Kind.Id, Realm = realmId, Name = $"{plan.Kind.Name}, {plan.From} to {plan.To}",
            Left = free ? 0 : plan.Years, Workers = free ? 0 : plan.Workers, History = history, Km = plan.Km,
        };
        road.Nodes.AddRange(plan.Nodes);
        Game.Roads.Add(road);
        _roadsDirty = true;
        _roadLayerDirty = true;
        _census = null;
        return road;
    }

    /// <summary>A finished wall takes its garrison from manpower, if the men are there.</summary>
    void ManWall(Road r)
    {
        if (RoadCatalog.Instance[r.Kind] is not { Wall: true } kind || r.Manned || !r.Built)
            return;
        var rs = Game.Realm(r.Realm);
        double men = kind.Garrison * r.Km;
        if (rs.Manpower < men)
            return;
        rs.Manpower -= men;
        r.Workers = men;
        r.Manned = true;
        _roadsDirty = true;
    }

    /// <summary>Breaks your own road to slow an invader (it stays broken until repaired).</summary>
    public string? BreakRoad(Road road)
    {
        if (road.Realm != PlayerRealmId || !road.Built || road.Broken)
            return "Only your own finished roads can be broken.";
        road.Broken = true;
        _roadsDirty = _roadLayerDirty = true;
        return null;
    }

    /// <summary>What repairing a broken road costs.</summary>
    public double RepairCost(Road road) => (RoadCatalog.Instance[road.Kind]?.Cost ?? 1) * road.Km * RoadRepairShare;

    public string? RepairRoad(Road road)
    {
        if (!road.Broken)
            return "It isn't broken.";
        double cost = RepairCost(road);
        if (PlayerState.Treasury < cost)
            return $"Not enough silver: {Money(cost)} needed.";
        PlayerState.Treasury -= cost;
        road.Broken = false;
        road.Left = 1;
        _roadsDirty = _roadLayerDirty = true;
        return null;
    }

    /// <summary>A realm's roads' upkeep a year (part of its buildings' upkeep).</summary>
    public double RoadUpkeep(int realmId) =>
        Game.Roads.Where(r => r.Realm == realmId && r.Built).Sum(r => (RoadCatalog.Instance[r.Kind]?.Cost ?? 0) * r.Km * RoadCatalog.Instance.UpkeepShare);

    /// <summary>The best built road kind through each province (for trade and the panels).</summary>
    public Dictionary<int, RoadKind> ProvinceRoads()
    {
        var result = new Dictionary<int, RoadKind>();
        var cat = RoadCatalog.Instance;
        var np = NodeProvinces();
        foreach (var r in Game.Roads)
        {
            if (!r.Built || r.Broken || cat[r.Kind] is not { } kind)
                continue;
            foreach (int node in r.Nodes)
            {
                int prov = node >= 0 && node < np.Length ? np[node] : 0;
                if (prov != 0 && (!result.TryGetValue(prov, out var have) || have.Rank < kind.Rank))
                    result[prov] = kind;
            }
        }
        return result;
    }

    /// <summary>
    /// Provinces of a realm joined to its capital by its roads, with the best
    /// kind the way allows (the weakest kind on the way decides).
    /// </summary>
    public Dictionary<int, RoadKind> RoadsToCapital(int realmId)
    {
        var result = new Dictionary<int, RoadKind>();
        int cap = CapitalNode(realmId);
        if (Population == null || cap < 0)
            return result;
        var ranks = RoadRanks();
        var pop = Population;
        int w = pop.Width, h = pop.Height;
        // Start from the road nearest the capital (within a few nodes).
        int start = -1;
        for (int r = 0; r <= 4 && start < 0; r++)
            for (int dy = -r; dy <= r && start < 0; dy++)
                for (int dx = -r; dx <= r && start < 0; dx++)
                {
                    int x = cap % w + dx, y = cap / w + dy;
                    if (x >= 0 && y >= 0 && x < w && y < h && ranks[y * w + x] >= 0 && pop.NodeOwner[y * w + x] == realmId)
                        start = y * w + x;
                }
        if (start < 0)
            return result;
        // Widest path: each node keeps the best "weakest kind" by which it can be reached.
        var best = new Dictionary<int, int> { [start] = ranks[start] };
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(start, -ranks[start]);
        var np = NodeProvinces();
        while (queue.TryDequeue(out int i, out int negRank))
        {
            int rank = -negRank;
            if (best[i] > rank)
                continue;
            int prov = np[i];
            if (prov != 0 && (!result.TryGetValue(prov, out var have) || have.Rank < rank))
                result[prov] = RoadCatalog.Instance.Kinds[rank];
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    if (ranks[j] < 0 || pop.NodeOwner[j] != realmId)
                        continue;
                    int through = Math.Min(rank, ranks[j]);
                    if (!best.TryGetValue(j, out int old) || through > old)
                    {
                        best[j] = through;
                        queue.Enqueue(j, -through);
                    }
                }
        }
        return result;
    }

    readonly Dictionary<int, Dictionary<int, RoadKind>> _toCapital = new();
    int _toCapitalYear = int.MinValue;

    /// <summary>RoadsToCapital, worked out once a year per realm.</summary>
    Dictionary<int, RoadKind> RoadsToCapitalCached(int realmId)
    {
        if (_toCapitalYear != DemoYear || _roadsDirty)
        {
            _toCapital.Clear();
            _toCapitalYear = DemoYear;
        }
        if (!_toCapital.TryGetValue(realmId, out var m))
            _toCapital[realmId] = m = RoadsToCapital(realmId);
        return m;
    }

    /// <summary>Research from roads: ideas travel along them.</summary>
    public double RoadResearch(int realmId) =>
        Math.Min(RoadResearchMax, Game.Roads.Where(r => r.Realm == realmId && r.Built && !r.Broken).Sum(r => r.Km) / RoadResearchKm * RoadResearchMax);

    /// <summary>History's roads that already stand in the game's first year, for whoever holds their land.</summary>
    void StartRoads()
    {
        if (Population == null)
            return;
        var cat = RoadCatalog.Instance;
        foreach (var h in cat.History.Where(h => h.Year <= DemoYear && (h.Fall == null || h.Fall > DemoYear)))
            for (int leg = 0; leg + 1 < h.Points.Length; leg++)
                LayHistoryLeg(h, leg, builder: 0, free: true);
        _roadsDirty = _roadLayerDirty = true;
    }

    /// <summary>
    /// Lays one leg of a history road. A realm (builder) lays it through its
    /// own land and pays; at the start (free) it stands already, for whoever holds its first place.
    /// </summary>
    bool LayHistoryLeg(HistoryRoad h, int leg, int builder, bool free)
    {
        string kindId = RoadCatalog.KindOfHistory(h);
        if (RoadCatalog.Instance[kindId] is not { } kind || Population == null)
            return false;
        string tag = $"{h.Id}:{leg}";
        if (Game.Roads.Any(r => r.History == tag))
            return false;
        var a = h.Points[leg];
        var b = h.Points[leg + 1];
        int from = Population.NodeAtLonLat(a.Lon, a.Lat, LonMin, LonMax, LatMin, LatMax);
        int to = Population.NodeAtLonLat(b.Lon, b.Lat, LonMin, LonMax, LatMin, LatMax);
        int realm = free ? Population.NodeOwner[from] : builder;
        var nodes = RoadPath(realm, from, to, anyLand: free);
        if (nodes == null || nodes.Count < 2)
            return false;
        var (km, bridges) = RoadMeasure(nodes);
        var plan = new RoadPlan
        {
            Kind = kind, Nodes = nodes, From = a.Name, To = b.Name, Km = km, Bridges = bridges,
            Silver = kind.Cost * km * RoadGround(nodes) + bridges * RoadCatalog.Instance.BridgeCost,
            Workers = kind.Workers * km,
            Years = Math.Max(1, (int)Math.Ceiling(km / (RoadCatalog.Instance.KmPerYear * (realm > 0 ? RoadBuildSpeed(realm) : 1)))),
        };
        var road = BuildRoad(Math.Max(realm, 0), plan, tag, free);
        if (road == null)
            return false;
        road.Name = $"{h.Name}, {a.Name} to {b.Name}";
        if (free && kind.Wall)
            road.Manned = true;   // standing and held in the first year
        if (!free)
        {
            // History's own: the builder finds the silver (into debt if need be) and what men it has.
            var rs = Game.Realm(builder);
            rs.Treasury -= plan.Silver;
            double men = Math.Min(plan.Workers, Math.Max(0, rs.Manpower));
            rs.Manpower -= men;
            road.Workers = men;
        }
        return true;
    }

    /// <summary>
    /// A year of roads: work goes on, finished roads give their workers back;
    /// unpaid roads decay a kind at a time; other realms begin history's roads
    /// on their dates where they hold both ends of a leg.
    /// </summary>
    internal List<ChronicleEvent> RoadsYear()
    {
        var events = new List<ChronicleEvent>();
        if (Population == null)
            return events;
        int years = Math.Max(1, Game.YearsPerTurn);
        var cat = RoadCatalog.Instance;
        foreach (var r in Game.Roads.ToList())
        {
            var rs = Game.Realm(r.Realm);
            if (r.Left > 0)
            {
                r.Left = Math.Max(0, r.Left - years);
                if (r.Left == 0)
                {
                    rs.Manpower += r.Workers;
                    r.Workers = 0;
                    ManWall(r);
                    _roadsDirty = _roadLayerDirty = true;
                    if (r.Realm == PlayerRealmId)
                        events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"The {r.Name} is finished: {r.Km:0} km."));
                }
                continue;
            }
            // Neglect: years without the silver for upkeep (or, for a wall, without its garrison).
            bool isWall = cat[r.Kind]?.Wall == true;
            if (isWall && !r.Manned)
                ManWall(r);
            r.Neglect = rs.UnpaidYears > 0 || (isWall && !r.Manned) ? r.Neglect + years : 0;
            if (r.Neglect < RoadNeglectYears)
                continue;
            r.Neglect = 0;
            var kind = cat[r.Kind];
            _roadsDirty = _roadLayerDirty = true;
            if (kind == null || kind.Rank == 0 || kind.Water || kind.Wall)
            {
                Game.Roads.Remove(r);
                if (kind?.Wall == true && r.Manned)
                    rs.Manpower += r.Workers;
                if (kind?.Wall == true)
                {
                    if (r.Realm == PlayerRealmId)
                        events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"Unmanned and unkept, the {r.Name} falls into ruin."));
                    continue;
                }
                if (r.Realm == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, kind?.Water == true
                        ? $"Unkept for years, the {r.Name} silts up."
                        : $"Unkept for years, the {r.Name} is lost to the fields."));
                continue;
            }
            var lower = cat.Kinds[kind.Rank - 1];
            r.Kind = lower.Id;
            r.Name = r.Name.Replace(kind.Name, lower.Name);
            if (r.Realm == PlayerRealmId)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"Unkept for years, the {kind.Name.ToLowerInvariant()} from {r.Name.Split(", ").Last()} falls to a {lower.Name.ToLowerInvariant()}."));
        }
        // History's walls fall on their dates (the Long Walls to Sulla).
        foreach (var h in cat.History.Where(h => h.Fall is int f && f <= DemoYear))
            foreach (var r in Game.Roads.Where(r => r.History.StartsWith(h.Id + ":")).ToList())
            {
                Game.Roads.Remove(r);
                if (r.Manned)
                    Game.Realm(r.Realm).Manpower += r.Workers;
                _roadsDirty = _roadLayerDirty = true;
                events.Add(new ChronicleEvent(ChronicleKind.War, PlayerRealmId, $"The {h.Name}: {h.FallWhy}."));
            }
        // History's builders lay their roads on their dates, a leg at a time where they hold both ends.
        foreach (var h in cat.History)
        {
            if (h.Year > DemoYear || h.Fall <= DemoYear || h.Builder == null || !CivRealmIds.TryGetValue(h.Builder, out int builder) || builder == PlayerRealmId
                || !Game.Realms.ContainsKey(builder) || RoadCatalog.KindOfHistory(h) == "")
                continue;
            for (int leg = 0; leg + 1 < h.Points.Length; leg++)
            {
                var a = h.Points[leg];
                var b = h.Points[leg + 1];
                int from = Population.NodeAtLonLat(a.Lon, a.Lat, LonMin, LonMax, LatMin, LatMax);
                int to = Population.NodeAtLonLat(b.Lon, b.Lat, LonMin, LonMax, LatMin, LatMax);
                if (Population.NodeOwner[from] != builder || Population.NodeOwner[to] != builder)
                    continue;
                if (LayHistoryLeg(h, leg, builder, free: false) && leg == 0)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"{RealmName(builder)} begins the {h.Name}."));
            }
        }
        return events;
    }

    internal double RoadPaceForTest(int i, int j) => RoadPace(i, j);
    internal bool TouchesWaterForTest(int node) => TouchesWater(node);
    internal double WallCrossingForTest(int i, int j, int realm) { _roadsDirty = true; return WallCrossing(i, j, realm); }
    internal bool CanalStepForTest(int i, int j) { _roadsDirty = true; return CanalStep(i, j); }
    internal List<ChronicleEvent> RoadsYearForTest() => RoadsYear();

    // --- On the map ----------------------------------------------------------------

    Node2D? _roadLayer;
    bool _roadLayerDirty = true;

    /// <summary>Draws the roads: built ones by kind, those under way faint, broken ones red.</summary>
    void RefreshRoadLayer()
    {
        if (Population == null)
            return;
        if (_roadLayer == null)
        {
            _roadLayer = new Node2D { ZIndex = 3 };
            AddChild(_roadLayer);
        }
        if (_roadLayerDirty)
        {
            foreach (Node child in _roadLayer.GetChildren())
                child.QueueFree();
            var cat = RoadCatalog.Instance;
            foreach (var r in Game.Roads)
            {
                int rank = cat[r.Kind]?.Rank ?? 0;
                bool water = cat[r.Kind]?.Water == true;
                var color = r.Broken ? new Color(0.75f, 0.2f, 0.15f, 0.9f)
                    : water ? new Color(0.35f, 0.65f, 0.95f, r.Built ? 0.95f : 0.4f)
                    : cat[r.Kind]?.Wall == true ? new Color(0.85f, 0.8f, 0.7f, r.Built ? (r.Manned ? 1f : 0.5f) : 0.35f)
                    : new Color(0.3f + 0.12f * rank, 0.22f + 0.06f * rank, 0.12f, r.Built ? 0.95f : 0.4f);
                var line = new Line2D
                {
                    Points = r.Nodes.Where((_, k) => k % 3 == 0 || k == r.Nodes.Count - 1).Select(n => NodeCell(n) * CellPixels).ToArray(),
                    DefaultColor = color,
                    JointMode = Line2D.LineJointMode.Round,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                };
                line.SetMeta("rank", rank);
                _roadLayer.AddChild(line);
            }
            if (_roadPreview != null)
                _roadLayer.AddChild(new Line2D
                {
                    Points = _roadPreview.Nodes.Where((_, k) => k % 3 == 0 || k == _roadPreview.Nodes.Count - 1).Select(n => NodeCell(n) * CellPixels).ToArray(),
                    DefaultColor = _roadPreview.Problem == null ? new Color(1f, 0.9f, 0.5f, 0.9f) : new Color(1f, 0.5f, 0.4f, 0.9f),
                });
            _roadLayerDirty = false;
        }
        ScaleRoadLines();
    }

    void ScaleRoadLines()
    {
        if (_roadLayer == null || _camera == null)
            return;
        float px = 1f / Math.Max(_camera.Zoom.X, 0.02f);
        foreach (Node child in _roadLayer.GetChildren())
            if (child is Line2D line)
                line.Width = (1.5f + 0.4f * (line.HasMeta("rank") ? line.GetMeta("rank").AsInt32() : 3)) * px;
    }

    // --- Drawing a road with the mouse ---------------------------------------------------

    [Signal] public delegate void RoadPlannedEventHandler(string text);

    /// <summary>The kind of road being drawn, or null when not drawing.</summary>
    public RoadKind? DrawingRoad { get; private set; }
    (string Name, int Node)? _roadStart;
    RoadPlan? _roadPreview;
    public RoadPlan? RoadPreview => _roadPreview;

    /// <summary>Starts drawing a road: click a town of yours, then another.</summary>
    public void StartDrawingRoad(RoadKind kind)
    {
        _wallEnd = -1;
        DrawingRoad = kind;
        _roadStart = null;
        _roadPreview = null;
        _roadLayerDirty = true;
        RefreshRoadLayer();
        EmitSignal(SignalName.RoadPlanned, kind.Wall
            ? $"Drawing a {kind.Name.ToLowerInvariant()}: click your land where it starts, then along your border (right-click to stop)."
            : kind.Water
            ? $"Digging a {kind.Name.ToLowerInvariant()}: click your land by the sea or a river to start (right-click to stop)."
            : $"Drawing a {kind.Name.ToLowerInvariant()}: click a town of yours to start (right-click to stop).");
    }

    public void StopDrawingRoad()
    {
        _wallEnd = -1;
        DrawingRoad = null;
        _roadStart = null;
        _roadPreview = null;
        _roadLayerDirty = true;
        RefreshRoadLayer();
        EmitSignal(SignalName.RoadPlanned, "");
    }

    /// <summary>A click while drawing a road: the first picks the start, the second plans the road.</summary>
    void RoadClick(Vector2 world)
    {
        if (DrawingRoad == null)
            return;
        if (DrawingRoad.Wall)
        {
            WallClick(world);
            return;
        }
        var town = DrawingRoad.Water ? WaterNear(world, PlayerRealmId) : TownNear(world, PlayerRealmId);
        if (town == null)
        {
            EmitSignal(SignalName.RoadPlanned, DrawingRoad.Water ? "Click your own land by the sea or a river." : "Click one of your towns (zoom in to see their names).");
            return;
        }
        if (_roadStart == null || _roadPreview != null)
        {
            _roadStart = town;
            _roadPreview = null;
            _roadLayerDirty = true;
            RefreshRoadLayer();
            EmitSignal(SignalName.RoadPlanned, $"From {town.Value.Name}: now click the town it should reach.");
            return;
        }
        _roadPreview = PlanRoad(PlayerRealmId, DrawingRoad, _roadStart.Value.Node, town.Value.Node, _roadStart.Value.Name, town.Value.Name);
        _roadLayerDirty = true;
        RefreshRoadLayer();
        EmitSignal(SignalName.RoadPlanned, DescribePlan(_roadPreview));
    }

    int _wallEnd = -1;

    /// <summary>A click while drawing a wall: each click adds a stretch along your land.</summary>
    void WallClick(Vector2 world)
    {
        var pop = Population!;
        int node = pop.NodeAtCell((int)(world.X / CellPixels), (int)(world.Y / CellPixels), GridWidth, GridHeight);
        if (node < 0 || pop.NodeRegion[node] == 0 || pop.NodeOwner[node] != PlayerRealmId)
        {
            EmitSignal(SignalName.RoadPlanned, "Click your own land, along the border you want walled.");
            return;
        }
        string name = Provinces?.Provinces.TryGetValue(ProvinceOfNode(node), out var p) == true ? p.Name : "here";
        if (_wallEnd < 0)
        {
            _wallEnd = node;
            _roadPreview = null;
            _roadStart = (name, node);
            EmitSignal(SignalName.RoadPlanned, $"The wall starts in {name}: click along your border to draw it, then Build.");
            return;
        }
        var next = ExtendWall(PlayerRealmId, DrawingRoad!, _roadPreview, _wallEnd, node, _roadStart!.Value.Name, name);
        if (next == null)
        {
            EmitSignal(SignalName.RoadPlanned, "No way along your own land to there.");
            return;
        }
        _roadPreview = next;
        _wallEnd = node;
        _roadLayerDirty = true;
        RefreshRoadLayer();
        EmitSignal(SignalName.RoadPlanned, DescribePlan(next) + $" Garrison {DrawingRoad!.Garrison * next.Km:N0} men once finished. Click on to lengthen it.");
    }

    public string DescribePlan(RoadPlan p) =>
        p.Problem != null && p.Nodes.Count == 0
            ? $"{p.Kind.Name} from {p.From} to {p.To}: {p.Problem}"
            : $"{p.Kind.Name} from {p.From} to {p.To}: {p.Km:0} km, {p.Bridges} bridge{(p.Bridges == 1 ? "" : "s")}; {Money(p.Silver)}, {p.Workers:N0} workers, " +
              $"{p.Years} year{(p.Years == 1 ? "" : "s")}; upkeep {Money(p.Kind.Cost * p.Km * RoadCatalog.Instance.UpkeepShare)} a year." + (p.Problem != null ? " " + p.Problem : "");

    /// <summary>Orders the road last planned with the mouse.</summary>
    public string? OrderPlannedRoad()
    {
        if (_roadPreview == null)
            return "Draw a road first.";
        if (_roadPreview.Problem != null)
            return _roadPreview.Problem;
        var road = BuildRoad(PlayerRealmId, _roadPreview);
        StopDrawingRoad();
        return road == null ? "It can't be built." : null;
    }
}
