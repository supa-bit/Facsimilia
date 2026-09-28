using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Armies on the march (decisions "Next 3-4", "Next 7"): every month each army
/// with a route moves along it, slowed by mountains, marsh, forest, desert,
/// rivers and winter; it eats from its own land, from supply lines near its
/// borders, from the land it crosses, or from its baggage, and starves when
/// all run out; armies of realms at war that meet fight; and each realm sees
/// only what its land, its armies and its allies can see.
/// </summary>
public partial class MapView
{
    /// <summary>A month's march on open ground in the campaigning season (about 20 km a day on 4-5 marching days a week, with halts to forage).</summary>
    public const double MarchKm = 90;
    /// <summary>Winter (November to February): armies march this share of the summer pace.</summary>
    public const double WinterPace = 0.5;
    /// <summary>The most months of food the baggage train carries.</summary>
    public const double BaggageMonths = 4;
    /// <summary>Beyond its own land an army is fed by supply lines up to this far (more with a good quartermaster).</summary>
    public const double SupplyLineKm = 120;
    /// <summary>People per node that can feed 1,000 soldiers a month by foraging (in summer).</summary>
    public const double ForagePeoplePer1000 = 6000;
    /// <summary>Share of an army lost each month it starves.</summary>
    public const double StarvationLoss = 0.04;
    /// <summary>Armies of realms at war within this many nodes (about 9 km each) meet in battle.</summary>
    public const int MeetNodes = 3;
    /// <summary>A besieging army counts as before the walls within this many nodes of the place.</summary>
    public const int SiegeNodes = 25;
    /// <summary>How far a realm sees beyond its borders, and around its armies (km).</summary>
    public const double SeeBorderKm = 150, SeeArmyKm = 120;

    float[]? _moveCost;
    double _stepKmY;
    double[] _stepKmX = Array.Empty<double>();

    public static bool Winter(int month) => month is 10 or 11 or 0 or 1;

    void EnsureMoveCost()
    {
        if (_moveCost != null || Population == null)
            return;
        var pop = Population;
        int w = pop.Width, h = pop.Height;
        _stepKmY = (LatMax - LatMin) / h * 111.2;
        _stepKmX = new double[h];
        for (int row = 0; row < h; row++)
        {
            double lat = LatMax - (row + 0.5) * (LatMax - LatMin) / h;
            _stepKmX[row] = (LonMax - LonMin) / w * 111.2 * Math.Cos(lat * Math.PI / 180);
        }
        _moveCost = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            if (pop.NodeRegion[i] == 0)
            {
                _moveCost[i] = -1;   // sea
                continue;
            }
            double V(string f) => Land != null && Land.Has(f) ? Land.Value(f, i) : 0;
            _moveCost[i] = (float)(1 + V("ruggedness") / 250 + 1.5 * V("marsh") + 0.6 * V("woodland") + 0.4 * V("desert") + 0.5 * V("river"));
        }
    }

    /// <summary>The ground's cost of a step from node i to node j in km (sea steps are cheap for a fleet, closed to others).</summary>
    double StepCost(int i, int j, bool fleet)
    {
        var pop = Population!;
        int w = pop.Width;
        int dx = Math.Abs(i % w - j % w), dy = Math.Abs(i / w - j / w);
        double ex = dx != 0 ? _stepKmX[i / w] : 0, ey = dy != 0 ? _stepKmY : 0;
        double km = Math.Sqrt(ex * ex + ey * ey);
        float c = _moveCost![j];
        if (c < 0)
            return fleet ? km * Conquest.SeaCostShare : double.PositiveInfinity;
        return km * c;
    }

    /// <summary>Whose land an army of this realm may march through: its own, its friends', its enemies' at war, and land no realm holds.</summary>
    public bool MayEnter(int realmId, int owner) =>
        owner <= 0 || owner == realmId || Game.Wars.AtWar(realmId, owner) || Game.Treaties.Allied(realmId, owner)
        || Game.Treaties.OverlordOf(owner) == realmId || Game.Treaties.OverlordOf(realmId) == owner;

    /// <summary>The cheapest route for an army to a node, or null (no way through, or no fleet for the sea).</summary>
    public List<int>? FindRoute(int realmId, Army army, int target)
    {
        if (Population == null || army.Node < 0 || target < 0 || target == army.Node)
            return target == army.Node ? new List<int>() : null;
        EnsureMoveCost();
        var pop = Population;
        int w = pop.Width, h = pop.Height, n = w * h;
        if (pop.NodeRegion[target] == 0 && Military.RawMight(army, Domain.Naval) <= 0)
            return null;   // only a fleet can be sent out to sea
        bool fleet = Military.RawMight(army, Domain.Naval) > 0;
        var cost = new Dictionary<int, double> { [army.Node] = 0 };
        var from = new Dictionary<int, int>();
        int[] owners = pop.NodeOwner;
        var entry = new Dictionary<int, bool>();
        bool Enter(int owner)
        {
            if (!entry.TryGetValue(owner, out var ok))
                entry[owner] = ok = MayEnter(realmId, owner);
            return ok;
        }
        // An estimate for A*: straight distance at the cheapest cost.
        int tx = target % w, ty = target / w;
        double minX = _stepKmX.Min() * (fleet ? Conquest.SeaCostShare : 1), minY = _stepKmY * (fleet ? Conquest.SeaCostShare : 1);
        double Guess(int i) => Math.Max(Math.Abs(i % w - tx) * minX, Math.Abs(i / w - ty) * minY);
        var queued = new PriorityQueue<int, double>();
        queued.Enqueue(army.Node, Guess(army.Node));
        while (queued.TryDequeue(out int i, out _))
        {
            if (i == target)
                break;
            double c = cost[i];
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    if (pop.NodeRegion[j] != 0 && !Enter(owners[j]) && j != target)
                        continue;
                    double step = StepCost(i, j, fleet);
                    if (double.IsInfinity(step))
                        continue;
                    double next = c + step;
                    if (!cost.TryGetValue(j, out var old) || next < old - 1e-9)
                    {
                        cost[j] = next;
                        from[j] = i;
                        queued.Enqueue(j, next + Guess(j));
                    }
                }
            if (cost.Count > n)
                break;
        }
        if (!from.ContainsKey(target))
            return null;
        var path = new List<int>();
        for (int k = target; k != army.Node; k = from[k])
            path.Add(k);
        path.Reverse();
        return path;
    }

    /// <summary>A month's march for an army, in km of open ground: slower in winter, weary, or under a poor quartermaster.</summary>
    double MonthBudget(RealmState s, Army a, int month)
    {
        var g = s.GeneralOf(a);
        return MarchKm * (Winter(month) ? WinterPace : 1) * (1 - 0.3 * a.Fatigue)
            * Math.Max(0.6, 1 + 0.05 * ((g?.Skill(Skills.Logistics) ?? 4) - 5)) * (1 + TechCatalog.Instance.Effect(s, "reach_km") / Conquest.ReachKm);
    }

    /// <summary>About how many months the army's route will take (for the panels).</summary>
    public double RouteMonths(RealmState s, Army a)
    {
        if (!a.Marching || Population == null)
            return 0;
        EnsureMoveCost();
        bool fleet = Military.RawMight(a, Domain.Naval) > 0;
        double km = -a.MarchCarry;
        int at = a.Node;
        foreach (int j in a.Route)
        {
            km += StepCost(at, j, fleet);
            at = j;
        }
        return Math.Max(0.1, km / Math.Max(1, MonthBudget(s, a, 5)));
    }

    /// <summary>Sends an army along a route to a node. Returns why not, or null.</summary>
    public string? SendArmy(int realmId, Army army, int node)
    {
        var route = FindRoute(realmId, army, node);
        if (route == null)
            return Population != null && node >= 0 && Population.NodeRegion[node] == 0
                ? "Armies march on land."
                : "There is no way there: the land of realms you are not at war with is closed, and the sea needs a fleet.";
        army.Route.Clear();
        army.Route.AddRange(route);
        army.MarchCarry = 0;
        return null;
    }

    /// <summary>Adds a leg to an army's route (a waypoint), from where its route now ends.</summary>
    public string? ExtendRoute(int realmId, Army army, int node)
    {
        if (!army.Marching)
            return SendArmy(realmId, army, node);
        int at = army.Node;
        army.Node = army.Route[^1];
        var leg = FindRoute(realmId, army, node);
        army.Node = at;
        if (leg == null)
            return "There is no way on from there.";
        army.Route.AddRange(leg);
        return null;
    }

    /// <summary>
    /// The months not yet marched this year (all twelve when a whole turn is
    /// played with the turn button). Returns what happened.
    /// </summary>
    internal List<ChronicleEvent> MarchRestOfYear()
    {
        var events = new List<ChronicleEvent>();
        while (Game.MonthsMarched < 12 && Pending == null)
            events.AddRange(MarchMonth());
        if (Pending == null)
            Game.MonthsMarched = 0;
        return events;
    }

    /// <summary>One month: plans made by the player go out, armies march, eat, and meet.</summary>
    public List<ChronicleEvent> MarchMonth()
    {
        var events = new List<ChronicleEvent>();
        if (Population == null)
        {
            Game.MonthsMarched++;
            return events;
        }
        int month = Game.MonthsMarched;
        events.AddRange(ResolvePlayerPlan());
        EnsureMoveCost();
        var rng = new Random(StableHash.Of(DemoYear, month, 6421));
        foreach (var s in Game.Realms.Values)
            foreach (var a in s.Armies)
            {
                if (a.IsEmpty)
                {
                    a.Route.Clear();
                    continue;
                }
                if (a.Marching)
                    Advance(s, a, month);
            }
        events.AddRange(Supply(month, rng));
        events.AddRange(Meetings(rng));
        Blockades(month);
        events.AddRange(MercenariesMonth(rng));
        foreach (var siege in Game.Sieges)
        {
            var army = Game.Realm(siege.Attacker).ArmyById(siege.ArmyId);
            if (army != null && !army.Marching && NodeDistance(army.Node, siege.Node) <= SiegeNodes)
                siege.MonthsPresent++;
        }
        Game.MonthsMarched++;
        _known = null;
        return events;
    }

    void Advance(RealmState s, Army a, int month)
    {
        bool fleet = Military.RawMight(a, Domain.Naval) > 0;
        double budget = MonthBudget(s, a, month) + a.MarchCarry;
        while (a.Route.Count > 0)
        {
            int next = a.Route[0];
            int owner = Population!.NodeOwner[next];
            if (Population.NodeRegion[next] != 0 && !MayEnter(s.RealmId, owner) && a.Route.Count > 1)
            {
                a.Route.Clear();   // the way closed (a peace, a new border): halt
                break;
            }
            double step = StepCost(a.Node, next, fleet);
            if (double.IsInfinity(step))
            {
                a.Route.Clear();
                break;
            }
            if (budget < step)
            {
                a.MarchCarry = budget;
                return;
            }
            budget -= step;
            a.Node = next;
            a.Route.RemoveAt(0);
        }
        a.MarchCarry = 0;
    }

    /// <summary>Distance between two nodes in grid steps (the larger of the two axes).</summary>
    int NodeDistance(int a, int b)
    {
        if (Population == null || a < 0 || b < 0)
            return int.MaxValue;
        int w = Population.Width;
        return Math.Max(Math.Abs(a % w - b % w), Math.Abs(a / w - b / w));
    }

    /// <summary>
    /// Food for every army abroad: its own and friendly land feed it and fill
    /// the baggage; near its borders supply lines feed it; further off it
    /// forages from the people around it and eats the baggage; with the
    /// baggage gone it starves.
    /// </summary>
    List<ChronicleEvent> Supply(int month, Random rng)
    {
        var events = new List<ChronicleEvent>();
        var pop = Population!;
        foreach (var s in Game.Realms.Values)
        {
            double[]? nearHome = null;
            foreach (var a in s.Armies)
            {
                if (a.IsEmpty || a.Node < 0)
                    continue;
                int owner = pop.NodeOwner[a.Node];
                if (owner == s.RealmId || Game.Treaties.Allied(s.RealmId, owner) || Game.Treaties.OverlordOf(owner) == s.RealmId)
                {
                    a.Supply = Math.Min(BaggageMonths, a.Supply + 1);
                    continue;
                }
                var g = s.GeneralOf(a);
                double lineKm = SupplyLineKm + 20 * ((g?.Skill(Skills.Logistics) ?? 5) - 5) + TechCatalog.Instance.Effect(s, "reach_km") / 2;
                nearHome ??= HomeDistance(s.RealmId, SupplyLineKm + 150);
                double men = Math.Max(1, Military.Soldiers(a));
                double people = 0;
                int w = pop.Width, x = a.Node % w, y = a.Node / w;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < pop.Height)
                            people += pop.Pop[ny * w + nx];
                    }
                double fed = Math.Min(1, people / ForagePeoplePer1000 / (men / 1000)) * (Winter(month) ? 0.4 : 1);
                if (nearHome[a.Node] <= lineKm)
                    fed = Math.Min(1, fed + 0.6);   // the supply line
                a.Supply -= 1 - fed;
                if (a.Supply >= 0)
                    continue;
                a.Supply = 0;
                double lost = Military.TakeLosses(a, StarvationLoss * (1 - fed), rng);
                a.Fatigue = Math.Min(1, a.Fatigue + 0.05);
                if (s.RealmId == PlayerRealmId && lost > 0)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, s.RealmId,
                        $"{a.Name} is starving in {PlaceOfNode(a.Node)}: {ThemeAncient.GroupThousands((long)lost)} men lost to hunger and desertion."));
            }
        }
        return events;
    }

    /// <summary>Km from a realm's own land to every node, up to a limit (farther: infinity).</summary>
    double[] HomeDistance(int realmId, double limit)
    {
        EnsureMoveCost();
        var pop = Population!;
        int w = pop.Width, h = pop.Height, n = w * h;
        var cost = new double[n];
        Array.Fill(cost, double.PositiveInfinity);
        var queue = new PriorityQueue<int, double>();
        // Only the border nodes need to seed: every own node is at 0.
        foreach (int i in pop.LandNodes)
            if (pop.NodeOwner[i] == realmId)
                cost[i] = 0;
        foreach (int i in pop.LandNodes)
        {
            if (cost[i] != 0)
                continue;
            int x = i % w, y = i / w;
            if ((x > 0 && cost[i - 1] != 0) || (x < w - 1 && cost[i + 1] != 0) || (y > 0 && cost[i - w] != 0) || (y < h - 1 && cost[i + w] != 0))
                queue.Enqueue(i, 0);
        }
        while (queue.TryDequeue(out int i, out double c))
        {
            if (c > cost[i] || c > limit)
                continue;
            int x = i % w, y = i / w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h)
                        continue;
                    int j = ny * w + nx;
                    double next = c + StepCost(i, j, false);
                    if (next < cost[j])
                    {
                        cost[j] = next;
                        queue.Enqueue(j, next);
                    }
                }
        }
        return cost;
    }

    /// <summary>
    /// Armies of realms at war that come within reach of each other fight
    /// (one battle per pair a month): on land when both carry soldiers, at sea
    /// when one is a fleet alone. A battle of the player's, when they fight
    /// their own battles, waits for them on the battle map (Pending).
    /// </summary>
    List<ChronicleEvent> Meetings(Random rng)
    {
        var events = new List<ChronicleEvent>();
        var armies = Game.Realms.Values.SelectMany(s => s.Armies.Where(a => !a.IsEmpty && a.Node >= 0)
            .Select(a => (State: s, Army: a))).ToList();
        var fought = new HashSet<Army>();
        for (int p = 0; p < armies.Count; p++)
            for (int q = p + 1; q < armies.Count; q++)
            {
                if (Pending != null)
                    return events;
                var (sa, a) = armies[p];
                var (sb, b) = armies[q];
                if (sa == sb || fought.Contains(a) || fought.Contains(b) || !Game.Wars.AtWar(sa.RealmId, sb.RealmId))
                    continue;
                if (NodeDistance(a.Node, b.Node) > MeetNodes)
                    continue;
                bool aLand = Military.RawMight(a, Domain.Land) > 0, bLand = Military.RawMight(b, Domain.Land) > 0;
                bool aSea = Military.RawMight(a, Domain.Naval) > 0, bSea = Military.RawMight(b, Domain.Naval) > 0;
                bool naval = (!aLand || !bLand) && aSea && bSea;
                if (!naval && !(aLand && bLand))
                    continue;   // a fleet alone can't fight soldiers ashore
                // The one on the move attacks; if both stand, the one on foreign soil.
                bool aAttacks = a.Marching || (!b.Marching && Population!.NodeOwner[a.Node] != sa.RealmId);
                var (att, attArmy, def, defArmy) = aAttacks ? (sa, a, sb, b) : (sb, b, sa, a);
                events.AddRange(FieldBattle(att, attArmy, def, defArmy, naval, rng));
                fought.Add(a);
                fought.Add(b);
            }
        return events;
    }

    /// <summary>A battle waiting for the player on the battle map.</summary>
    public sealed class PendingBattle
    {
        public required RealmState Att { get; init; }
        public required Army AttArmy { get; init; }
        public required RealmState Def { get; init; }
        public required Army DefArmy { get; init; }
        public required BattleSide A { get; init; }
        public required BattleSide D { get; init; }
        public required Ground Ground { get; init; }
        public required string Place { get; init; }
        public required TacticalBattle Tactics { get; init; }
        /// <summary>The side the player commands (0 attacker, 1 defender).</summary>
        public int Side { get; init; }
    }

    /// <summary>The player's battle waiting on the battle map, or null.</summary>
    public PendingBattle? Pending { get; private set; }
    /// <summary>The player fights their own battles on the battle map (Settings > Gameplay); off in tests.</summary>
    public bool FightBattlesYourself { get; set; }

    /// <summary>Two armies (or fleets) fight where they meet; the beaten one falls back toward home and gives up its sieges.</summary>
    List<ChronicleEvent> FieldBattle(RealmState att, Army attArmy, RealmState def, Army defArmy, bool naval, Random rng)
    {
        var a = new BattleSide { Realm = att.RealmId, RealmName = RealmName(att.RealmId), Armies = new() { (attArmy, 1.0) }, General = att.GeneralOf(attArmy) };
        var d = new BattleSide
        {
            Realm = def.RealmId, RealmName = RealmName(def.RealmId), Armies = new() { (defArmy, 1.0) }, General = def.GeneralOf(defArmy),
            Extra = !naval && Population!.NodeOwner[defArmy.Node] == def.RealmId ? 0.1 * Military.Might(defArmy, Domain.Land) : 0,   // on home ground
        };
        string place = PlaceOfNode(defArmy.Node);
        var ground = Ground.At(Land, defArmy.Node);
        if (FightBattlesYourself && (att.RealmId == PlayerRealmId || def.RealmId == PlayerRealmId))
        {
            Pending = new PendingBattle
            {
                Att = att, AttArmy = attArmy, Def = def, DefArmy = defArmy, A = a, D = d, Ground = ground, Place = place,
                Tactics = new TacticalBattle(a, d, ground, naval, StableHash.Of(DemoYear, Game.MonthsMarched, attArmy.Id, defArmy.Id)),
                Side = att.RealmId == PlayerRealmId ? 0 : 1,
            };
            return new List<ChronicleEvent>();
        }
        var report = naval ? Battle.FightNaval(a, d, place, DemoYear, rng) : Battle.Fight(a, d, ground, place, DemoYear, rng);
        return Conclude(report, att, attArmy, def, defArmy, naval, rng);
    }

    /// <summary>The player's battle on the battle map is over (or left to the general): its result goes into the world.</summary>
    public List<ChronicleEvent> FinishPendingBattle()
    {
        var p = Pending;
        if (p == null)
            return new List<ChronicleEvent>();
        Pending = null;
        p.Tactics.PlayOut();   // anything left undecided, the generals finish
        var rng = new Random(StableHash.Of(DemoYear, Game.MonthsMarched, 9127));
        var report = Battle.FromTactics(p.Tactics, p.A, p.D, p.Ground, p.Place, DemoYear, rng);
        return Conclude(report, p.Att, p.AttArmy, p.Def, p.DefArmy, p.Tactics.Naval, rng);
    }

    List<ChronicleEvent> Conclude(BattleReport report, RealmState att, Army attArmy, RealmState def, Army defArmy, bool naval, Random rng)
    {
        var events = new List<ChronicleEvent>();
        Game.AddBattle(report);
        var (winS, winA, loseS, loseA) = report.AttackerWon ? (att, attArmy, def, defArmy) : (def, defArmy, att, attArmy);
        MaybeFall(loseS, loseS.GeneralOf(loseA), rng, events);
        var war = Game.Wars.Between(att.RealmId, def.RealmId);
        if (war != null)
        {
            double swing = Math.Clamp(10 * (report.AttackerLost + report.DefenderLost) / 20000.0, 3, 15);
            bool attackerWonWar = war.Attacker == winS.RealmId;
            war.Score = Math.Clamp(war.Score + (attackerWonWar ? swing : -swing), -100, 100);
            bool attIsWarAttacker = war.Attacker == att.RealmId;
            war.AttackerLosses += attIsWarAttacker ? report.AttackerLost : report.DefenderLost;
            war.DefenderLosses += attIsWarAttacker ? report.DefenderLost : report.AttackerLost;
        }
        Game.Sieges.RemoveAll(x => x.Attacker == loseS.RealmId && x.ArmyId == loseA.Id);
        loseA.Route.Clear();
        if (!loseA.IsEmpty)
            SendArmy(loseS.RealmId, loseA, CapitalNode(loseS.RealmId));
        string what = naval ? "fleet" : "army";
        string text = $"{report.Title}: {winA.Name} of {RealmName(winS.RealmId)} defeats {loseA.Name} of {RealmName(loseS.RealmId)}" +
            $" ({ThemeAncient.GroupThousands(report.AttackerLost + report.DefenderLost)} {(naval ? "men lost with their ships" : "men fall")}); the beaten {what} falls back.";
        events.Add(new ChronicleEvent(ChronicleKind.War, att.RealmId, text));
        events.Add(new ChronicleEvent(ChronicleKind.War, def.RealmId, text));
        return events;
    }

    // --- Blockades (decision "Next 5") ------------------------------------------------

    /// <summary>A fleet this close to an enemy's shore blockades it.</summary>
    public const int BlockadeNodes = 6;

    /// <summary>
    /// Each month: every realm's coast watched by enemy warships at least half
    /// as strong as its own fleet. Each such fleet cuts a quarter of the
    /// realm's sea trade (at most 80%); the year's customs fall by the average.
    /// </summary>
    void Blockades(int month)
    {
        var pop = Population!;
        int w = pop.Width, h = pop.Height;
        foreach (var s in Game.Realms.Values)
        {
            if (month == 0)
                s.BlockadeMonths = 0;
            double own = Military.RawMight(s, Domain.Naval);
            int fleets = 0;
            foreach (var enemy in Game.Wars.Of(s.RealmId).Select(war => war.Attacker == s.RealmId ? war.Defender : war.Attacker).Distinct())
                foreach (var a in Game.Realm(enemy).Armies)
                {
                    double might = Military.Might(a, Domain.Naval);
                    if (a.Node < 0 || might <= 0 || might < 0.5 * own)
                        continue;
                    int x = a.Node % w, y = a.Node / w;
                    bool shore = false, sea = pop.NodeRegion[a.Node] == 0;
                    for (int dy = -BlockadeNodes; dy <= BlockadeNodes && !shore; dy++)
                        for (int dx = -BlockadeNodes; dx <= BlockadeNodes; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            int j = ny * w + nx;
                            sea |= pop.NodeRegion[j] == 0;
                            if (pop.NodeRegion[j] != 0 && pop.NodeOwner[j] == s.RealmId)
                                shore = true;
                        }
                    if (shore && sea)
                        fleets++;
                }
            s.Blockade = Math.Min(0.8, 0.25 * fleets);
            s.BlockadeMonths += s.Blockade;
        }
    }

    /// <summary>Is there sea within a few nodes of this node (a coastal place)?</summary>
    public bool Coastal(int node, int radius = 3)
    {
        if (Population == null || node < 0)
            return false;
        var pop = Population;
        int w = pop.Width, x = node % w, y = node / w;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < pop.Height && pop.NodeRegion[ny * w + nx] == 0)
                    return true;
            }
        return false;
    }

    // --- What a realm knows (decision "Next 7": partial fog of war) ------------------

    bool[]? _known;

    /// <summary>
    /// The land the player's realm knows now: its own and its allies' and
    /// vassals' land, a short march beyond its borders, and around its armies.
    /// </summary>
    public bool[] Known()
    {
        if (_known != null)
            return _known;
        var pop = Population!;
        int w = pop.Width, h = pop.Height;
        var known = new bool[w * h];
        var friends = new HashSet<int> { PlayerRealmId };
        foreach (int r in Game.Treaties.AlliesOf(PlayerRealmId))
            friends.Add(r);
        foreach (int r in Game.Treaties.VassalsOf(PlayerRealmId))
            friends.Add(r);
        foreach (int r in friends)
        {
            var dist = HomeDistance(r, SeeBorderKm);
            for (int i = 0; i < known.Length; i++)
                if (dist[i] <= SeeBorderKm)
                    known[i] = true;
        }
        int radius = (int)Math.Ceiling(SeeArmyKm / Math.Max(_stepKmY, 1));
        foreach (int r in friends)
            foreach (var a in Game.Realm(r).Armies)
            {
                if (a.IsEmpty || a.Node < 0)
                    continue;
                int x = a.Node % w, y = a.Node / w;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && ny >= 0 && nx < w && ny < h && dx * dx + dy * dy <= radius * radius)
                            known[ny * w + nx] = true;
                    }
            }
        return _known = known;
    }

    Sprite2D? _fog;
    /// <summary>The fog can be turned off in the settings of a game (for watching the whole world).</summary>
    public bool FogOn { get; set; } = true;

    /// <summary>Dims the land the realm doesn't know (redrawn each month).</summary>
    void RefreshFog()
    {
        if (Population == null || MapSprite == null)
            return;
        var pop = Population;
        int w = pop.Width, h = pop.Height;
        if (_fog == null)
        {
            _fog = new Sprite2D { Centered = false, ZIndex = 4, TextureFilter = TextureFilterEnum.Linear };
            AddChild(_fog);
        }
        _fog.Visible = FogOn;
        if (!FogOn)
            return;
        _fog.Scale = new Vector2((float)GridWidth / w, (float)GridHeight / h);
        var known = Known();
        var img = Image.CreateEmpty(w, h, false, Image.Format.La8);
        for (int i = 0; i < w * h; i++)
            if (!known[i] && pop.NodeRegion[i] != 0)
                img.SetPixel(i % w, i / w, new Color(0.08f, 0.08f, 0.08f, 0.38f));
        _fog.Texture = ImageTexture.CreateFromImage(img);
    }

    public bool KnowsNode(int node) => !FogOn || Population == null || node < 0 || Known()[node];
}
