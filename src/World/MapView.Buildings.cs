using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Buildings on the map (decision "Playable 32": a core set): where each can
/// be built, construction over the years, and what finished buildings do
/// to production, trade, integration, unrest, walls, levies and health.
/// </summary>
public partial class MapView
{
    int[] _nodeProvince = Array.Empty<int>();
    int _nodeProvinceYear = int.MinValue;

    /// <summary>The province of every node (0 = none), refreshed once a year.</summary>
    int[] NodeProvinces()
    {
        if (Population == null || Provinces == null)
            return Array.Empty<int>();
        if (_nodeProvince.Length != Population.Pop.Length || _nodeProvinceYear != DemoYear)
        {
            _nodeProvince = new int[Population.Pop.Length];
            foreach (int i in Population.LandNodes)
                _nodeProvince[i] = ProvinceOfNode(i);
            _nodeProvinceYear = DemoYear;
        }
        return _nodeProvince;
    }

    /// <summary>Why a building can't be started in a province now, or null.</summary>
    public string? CanBuild(Province p, BuildingDef b)
    {
        if (p.RealmId != PlayerRealmId)
            return "Only in your own provinces.";
        return CanBuildFor(p, b, PlayerState);
    }

    string? CanBuildFor(Province p, BuildingDef b, RealmState realm)
    {
        var ps = ProvinceStateOf(p.Id);
        if (b.Requires != null && !realm.Techs.Contains(b.Requires))
            return $"Needs the technology: {TechCatalog.Instance[b.Requires]?.Name ?? b.Requires}.";
        int next = NextLevel(ps, b);
        if (next > b.Max)
            return b.Max == 1 ? "Already built." : "Built as far as it goes.";
        if (ps.Works.Count >= MaxQueue)
            return $"The queue is full ({MaxQueue}).";
        if (b.Needs != null && !ProvinceHas(p.Id, b.Needs))
            return b.Needs switch
            {
                "river" => "Needs a river or floodplain.",
                "hills" => "Needs hilly land.",
                "ore" => "Needs a mineral deposit.",
                "coast" => "Needs a coast.",
                "town" => "Needs a town of 10,000 people or more.",
                _ when b.Needs.StartsWith("has:") => $"Needs land that yields {GoodsCatalog()?.Goods.FirstOrDefault(g => g.Id == b.Needs[4..])?.Name.ToLowerInvariant() ?? b.Needs[4..]}.",
                _ => "Can't be built here.",
            };
        if (realm.Treasury < b.CostOf(next))
            return $"Not enough silver: {Money(b.CostOf(next))} needed.";
        return null;
    }

    /// <summary>At most this many buildings wait in a province's queue.</summary>
    public const int MaxQueue = 6;
    /// <summary>A town of this many people works on a second building at once, and this many on a third.</summary>
    public const double SecondSlotTown = 50000, ThirdSlotTown = 200000;
    /// <summary>Buildings neglected this many years lose a level.</summary>
    public const int NeglectYearsToDecay = 3;
    /// <summary>A province with fewer people than this leaves its buildings unused.</summary>
    public const double AbandonedPeople = 1000;
    /// <summary>Each building in a province taken by siege loses a level with this chance (walls always).</summary>
    public const double SiegeDamage = 0.3;

    /// <summary>The level a building would reach next, counting what is queued.</summary>
    public static int NextLevel(ProvinceState ps, BuildingDef b) => ps.Level(b.Id) + ps.Works.Count(w => w.Id == b.Id) + 1;

    /// <summary>How many buildings a province works on at once: one, two with a town of 50,000, three with 200,000.</summary>
    public int BuildSlots(int provinceId)
    {
        ProvinceHas(provinceId, "town");   // fills the cache
        double town = _largestPlace.GetValueOrDefault(provinceId);
        return 1 + (town >= SecondSlotTown ? 1 : 0) + (town >= ThirdSlotTown ? 1 : 0);
    }

    readonly Dictionary<int, double> _largestPlace = new();
    Dictionary<int, HashSet<string>>? _provinceNeeds;
    int _provinceNeedsYear = int.MinValue;

    /// <summary>Whether a province has what a building needs (worked out for every province once a year).</summary>
    bool ProvinceHas(int provinceId, string need)
    {
        if (Population == null || Land == null)
            return false;
        if (_provinceNeeds == null || _provinceNeedsYear != DemoYear)
        {
            _provinceNeeds = new Dictionary<int, HashSet<string>>();
            _largestPlace.Clear();
            var np = NodeProvinces();
            var goodsCat = GoodsCatalog();
            var haveFields = goodsCat == null ? new List<(string Id, string Field)>()
                : BuildingCatalog.Instance.All.Where(b => b.Needs != null && b.Needs.StartsWith("has:"))
                    .Select(b => goodsCat.Goods.FirstOrDefault(g => g.Id == b.Needs![4..])).Where(g => g != null && Land.Has(g.Field))
                    .Select(g => (g!.Id, g.Field)).Distinct().ToList();
            var ores = Census.TrackedResources.Concat(new[] { "res_marble", "res_granite", "res_limestone" }).Where(Land.Has).ToArray();
            foreach (int i in Population.LandNodes)
            {
                int prov = np[i];
                if (prov == 0)
                    continue;
                if (!_provinceNeeds.TryGetValue(prov, out var set))
                    _provinceNeeds[prov] = set = new HashSet<string>();
                if (Land.Value("river", i) >= 0.3 || Land.Value("floodplain", i) >= 0.2 || Land.Value("irrigation", i) >= 0.3)
                    set.Add("river");
                if (Land.Value("ruggedness", i) >= 60)
                    set.Add("hills");
                if (Land.Value("coast_km", i) < 15)
                    set.Add("coast");
                if (Population.Pop[i] >= Census.UrbanThreshold)
                    set.Add("town");
                if (!set.Contains("ore") && ores.Any(r => Land.Value(r, i) >= Census.ResourceThreshold))
                    set.Add("ore");
                foreach (var (gid, gfield) in haveFields)
                    if (!set.Contains("has:" + gid) && Land.Value(gfield, i) >= Census.ResourceThreshold)
                        set.Add("has:" + gid);
                _largestPlace[prov] = Math.Max(_largestPlace.GetValueOrDefault(prov), Population.Pop[i]);
            }
            _provinceNeedsYear = DemoYear;
        }
        return _provinceNeeds.TryGetValue(provinceId, out var has) && has.Contains(need);
    }

    public bool StartBuilding(Province p, BuildingDef b)
    {
        if (CanBuild(p, b) != null)
            return false;
        StartBuildingFor(p, b, PlayerState);
        return true;
    }

    void StartBuildingFor(Province p, BuildingDef b, RealmState realm)
    {
        var ps = ProvinceStateOf(p.Id);
        realm.Treasury -= b.CostOf(NextLevel(ps, b));
        ps.Works.Add(new BuildWork(b.Id, b.Years));
    }

    /// <summary>
    /// The year's building: work goes on, finished buildings open, and other
    /// realms with silver to spare begin something useful.
    /// </summary>
    internal List<ChronicleEvent> BuildingsYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Provinces == null)
            return events;
        var cat = BuildingCatalog.Instance;
        foreach (var p in Provinces.Provinces.Values)
        {
            var ps = ProvinceStateOf(p.Id);
            if (ps.Works.Count == 0)
                continue;
            if (Game.Sieges.Any(s => s.ProvinceId == p.Id))
                continue;   // no building under siege
            // The first few in the queue are worked on, as many as the province's slots.
            foreach (var work in ps.Works.Take(BuildSlots(p.Id)).ToList())
            {
                if (--work.Left > 0)
                    continue;
                int level = ps.Level(work.Id) + 1;
                ps.Buildings[work.Id] = level;
                ps.Works.Remove(work);
                if (p.RealmId == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                        $"{cat[work.Id]?.LevelName(level) ?? work.Id} finished in {p.Name}."));
            }
        }
        events.AddRange(NeglectYear(rng));
        // Other realms: a rich realm builds where it helps most.
        var census = RealmCensus();
        foreach (var (id, state) in Game.Realms)
        {
            if (id == PlayerRealmId || !census.TryGetValue(id, out var c) || rng.NextDouble() > 0.5)
                continue;
            var (tax, trib) = Economy.Revenue(c, state.Tax);
            if (state.Treasury < 1.5 * (tax + trib) || Game.Wars.Of(id).Any())
                continue;
            var options = Provinces.Provinces.Values.Where(p => p.RealmId == id)
                .SelectMany(p => cat.All.Select(b => (p, b)))
                .Where(x => CanBuildFor(x.p, x.b, state) == null)
                .OrderByDescending(x => ProvincePopulation(x.p.Id) * (x.b.Id is "workshops" or "market" or "irrigation" or "granary" ? 1.5 : 1))
                .Take(8).ToList();
            if (options.Count > 0)
            {
                var (prov, b) = options[rng.Next(options.Count)];
                StartBuildingFor(prov, b, state);
            }
        }
        return events;
    }

    /// <summary>Production multipliers per node from the buildings of each province (for the goods).</summary>
    Dictionary<string, float[]>? BuildingNodeFactors()
    {
        if (Population == null || Provinces == null)
            return null;
        var cat = BuildingCatalog.Instance;
        var np = NodeProvinces();
        var byProvince = new Dictionary<int, (double Field, double Orchard, double Mine, double Gather)>();
        foreach (var p in Provinces.Provinces.Values)
        {
            var ps = ProvinceStateOf(p.Id);
            if (ps.Buildings.Count == 0)
                continue;
            double famine = cat.Effect(ps, "famine");
            // A granary saves a share of a bad harvest's loss.
            double harvest = NatureOf(RegionOfProvince(p)).Harvest;
            double granary = harvest < 1 && famine > 0 ? (1 - (1 - harvest) * (1 - famine)) / harvest : 1;
            byProvince[p.Id] = ((1 + cat.Effect(ps, "field")) * granary, 1 + cat.Effect(ps, "orchard"),
                1 + cat.Effect(ps, "mine"), 1 + cat.Effect(ps, "gather"));
        }
        if (byProvince.Count == 0)
            return null;
        int n = Population.Pop.Length;
        var field = new float[n];
        var orchard = new float[n];
        var mine = new float[n];
        var gather = new float[n];
        for (int i = 0; i < n; i++)
        {
            var f = byProvince.TryGetValue(np.Length > i ? np[i] : 0, out var v) ? v : (1, 1, 1, 1);
            field[i] = (float)f.Item1;
            orchard[i] = (float)f.Item2;
            mine[i] = (float)f.Item3;
            gather[i] = (float)f.Item4;
        }
        return new Dictionary<string, float[]> { ["field"] = field, ["orchard"] = orchard, ["mine"] = mine, ["earth"] = mine, ["gather"] = gather };
    }

    /// <summary>
    /// A realm's building effects that act on the whole realm, as shares
    /// weighted by the people of the provinces that have them.
    /// </summary>
    (double Crafts, double Trade, double Manpower, double Upkeep, bool Harbour, double Research) RealmBuildingEffects(int realmId, double realmPeople)
    {
        var tech = TechCatalog.Instance;
        var rs = Game.Realm(realmId);
        if (Provinces == null || realmPeople <= 0)
            return (tech.Effect(rs, "crafts"), tech.Effect(rs, "trade"), tech.Effect(rs, "manpower"), 0, false, 0);
        var cat = BuildingCatalog.Instance;
        double crafts = tech.Effect(rs, "crafts"), trade = tech.Effect(rs, "trade"), manpower = tech.Effect(rs, "manpower"), upkeep = 0, research = 0;
        bool harbour = false;
        foreach (var p in Provinces.Provinces.Values)
        {
            if (p.RealmId != realmId)
                continue;
            var ps = ProvinceStateOf(p.Id);
            if (ps.Buildings.Count == 0)
                continue;
            double share = ProvincePopulation(p.Id) / realmPeople;
            crafts += share * cat.Effect(ps, "crafts");
            trade += share * cat.Effect(ps, "trade");
            manpower += share * cat.Effect(ps, "manpower");
            research += share * cat.Effect(ps, "research");
            upkeep += cat.Upkeep(ps);
            harbour |= ps.Level("harbour") > 0;
        }
        return (crafts, trade, manpower, upkeep, harbour, research);
    }

    /// <summary>Upkeep, levies and warship prices from the buildings, into this year's census.</summary>
    void ApplyBuildingsToCensus(Dictionary<int, RealmCensus> census)
    {
        foreach (var (id, c) in census)
        {
            var e = RealmBuildingEffects(id, c.People);
            c.BuildingUpkeep = e.Upkeep;
            c.ManpowerBonus = e.Manpower;
            c.BuildingResearch = e.Research;
            if (Game.Realms.TryGetValue(id, out var s))
                s.ShipDiscount = (e.Harbour ? BuildingCatalog.Instance["harbour"]?.Effect("ships") ?? 0 : 0)
                    + (Provinces?.Provinces.Values.Any(p => p.RealmId == id && ProvinceStateOf(p.Id).Level("shipyards") > 0) == true ? BuildingCatalog.Instance["shipyards"]?.Effect("ships") ?? 0 : 0);
        }
    }

    /// <summary>The Health growth factor of each region from its aqueducts, weighted by people.</summary>
    void ApplyBuildingGrowth()
    {
        if (Population == null || Provinces == null)
            return;
        var cat = BuildingCatalog.Instance;
        var weighted = new double[Population.RegionCount + 1];
        foreach (var p in Provinces.Provinces.Values)
        {
            double g = cat.Effect(ProvinceStateOf(p.Id), "growth");
            if (g <= 0)
                continue;
            int r = RegionOfProvince(p);
            if (r > 0 && r < weighted.Length)
                weighted[r] += g * ProvincePopulation(p.Id);
        }
        // Medicine and the like: each region's people, weighted by their ruler's knowledge.
        var growthOf = Game.Realms.ToDictionary(kv => kv.Key, kv => TechCatalog.Instance.Effect(kv.Value, "growth"));
        foreach (int i in Population.LandNodes)
            if (growthOf.TryGetValue(Population.NodeOwner[i], out double tg) && tg > 0)
                weighted[Population.RegionOf(i)] += tg * Population.Pop[i];
        for (int r = 1; r < weighted.Length; r++)
        {
            double pop = Population.RegionPopulation(r);
            Population.SetFactor(r, GrowthFactor.Health, pop > 0 ? weighted[r] / pop : 0);
        }
    }

    /// <summary>Forget this year's census (a tax rate changed).</summary>
    public void InvalidateCensus() => _census = null;

    /// <summary>Forget this year's goods too (tests: a building was added by hand).</summary>
    internal void InvalidateGoods()
    {
        _census = null;
        _goodsYear = int.MinValue;
        _nodeProvinceYear = int.MinValue;
        _provinceNeedsYear = int.MinValue;
    }

    /// <summary>The garrison a province's walls and militia give against a siege.</summary>
    public double GarrisonOf(Province p) => Garrison(p.RealmId, p.Id, ProvincePopulation(p.Id), -1);

    /// <summary>Resources found in a province (the goods a mine or gatherer would find), by name.</summary>
    public List<string> ProvinceResources(int provinceId)
    {
        var found = new List<string>();
        var cat = GoodsCatalog();
        if (Population == null || Land == null || cat == null)
            return found;
        var np = NodeProvinces();
        var fields = cat.Goods.Where(g => g.Source == GoodSource.Land && g.Field.StartsWith("res_") && Land.Has(g.Field)).ToList();
        foreach (var g in fields)
            foreach (int i in Population.LandNodes)
                if (np[i] == provinceId && Land.Value(g.Field, i) >= Census.ResourceThreshold)
                {
                    found.Add(g.Name);
                    break;
                }
        return found;
    }

    /// <summary>A province's effective tax rate: its own, or its realm's.</summary>
    public TaxRate TaxOf(Province p) => ProvinceStateOf(p.Id).Tax ?? Game.Realm(p.RealmId).Tax;
}

/// <summary>Remedies for an empty treasury, on the map (they need the census and the other realms).</summary>
public partial class MapView
{
    /// <summary>The remedies open to the player now, each with why not if it isn't.</summary>
    public List<(Remedy Remedy, string? Problem, double Silver)> RemediesNow()
    {
        var list = new List<(Remedy, string?, double)>();
        var s = PlayerState;
        var c = CensusOf(PlayerRealmId);
        var (tax, trib) = Economy.Revenue(c, TaxRate.Normal);
        double income = tax + trib;
        bool trouble = Remedies.InTrouble(s);
        foreach (var r in Remedies.All)
        {
            string? problem = !trouble ? "Only when the treasury is in trouble (in debt, or short of a year's costs)."
                : Remedies.Active(s, r.Id) ? "Already done: its effects are still felt."
                : r == Remedies.TaxFarms && c.Provinces < 3 ? "Needs at least three provinces."
                : null;
            double silver = r.IncomeShare * income;
            if (r == Remedies.AllyGift)
            {
                int friend = BestFriend();
                if (friend == 0 && problem == null)
                    problem = "No ally or overlord thinks well enough of you (+30).";
                silver = friend != 0 ? Math.Min(Game.Realm(friend).Treasury * 0.2, silver) : 0;
            }
            list.Add((r, problem, Math.Round(silver)));
        }
        return list;
    }

    int BestFriend() => Game.Treaties.AlliesOf(PlayerRealmId).Append(Game.Treaties.OverlordOf(PlayerRealmId))
        .Where(f => f > 0 && Opinion(f, PlayerRealmId).Value >= 30 && Game.Realm(f).Treasury > 0)
        .OrderByDescending(f => Game.Realm(f).Treasury).FirstOrDefault();

    /// <summary>Takes a remedy; returns the chronicle line, or null if it can't be taken.</summary>
    public string? TakeRemedy(string id)
    {
        var (remedy, problem, silver) = RemediesNow().FirstOrDefault(x => x.Remedy.Id == id);
        if (remedy == null || problem != null)
            return null;
        var s = PlayerState;
        if (remedy == Remedies.AllyGift)
        {
            int friend = BestFriend();
            Game.Realm(friend).Treasury -= silver;
            s.Treasury += silver;
            return $"{RealmName(friend)} sends {Money(silver)} to help.";
        }
        s.Treasury += silver;
        if (s.Debt > 0)
            Finance.Repay(s, Math.Min(s.Debt, s.Treasury));   // the dearest loans first
        s.RemedyYears[remedy.Id] = remedy.Years;
        return $"{RealmName(PlayerRealmId)}: {remedy.Name.ToLowerInvariant()} ({Money(silver)}).";
    }

    /// <summary>
    /// Neglect (decision "Neglect": unpaid or unused buildings decay): a
    /// province whose realm could not pay its way, or with almost no one left
    /// to use its buildings, counts the years; after three, one building loses a level.
    /// </summary>
    List<ChronicleEvent> NeglectYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        var cat = BuildingCatalog.Instance;
        foreach (var p in Provinces!.Provinces.Values)
        {
            var ps = ProvinceStateOf(p.Id);
            if (ps.Buildings.Count == 0)
                continue;
            bool unpaid = p.RealmId > 0 && Game.Realms.TryGetValue(p.RealmId, out var owner) && owner.UnpaidYears > 0;
            bool unused = ProvincePopulation(p.Id) < AbandonedPeople;
            ps.NeglectYears = unpaid || unused ? ps.NeglectYears + 1 : 0;
            if (ps.NeglectYears < NeglectYearsToDecay)
                continue;
            ps.NeglectYears = 0;
            var built = ps.Buildings.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(k => k).ToList();
            if (built.Count == 0)
                continue;
            string id = built[rng.Next(built.Count)];
            LowerBuilding(ps, id);
            if (p.RealmId == PlayerRealmId)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                    $"Neglected for years, the {cat[id]?.Name.ToLowerInvariant() ?? id} of {p.Name} fall{(ps.Level(id) == 0 ? " into ruin" : " into disrepair")}."));
        }
        return events;
    }

    internal void NeglectYearForTest() => NeglectYear(new Random(1));
    internal void DamageBuildingsForTest(int provinceId) => DamageBuildings(provinceId, new Random(1));

    static void LowerBuilding(ProvinceState ps, string id)
    {
        int level = ps.Level(id) - 1;
        if (level <= 0)
            ps.Buildings.Remove(id);
        else
            ps.Buildings[id] = level;
    }

    /// <summary>
    /// War damage (decision "War damages buildings": sieges and sacks damage
    /// or destroy them): taking a province by siege breaches its walls and
    /// damages each other building with a chance.
    /// </summary>
    List<string> DamageBuildings(int provinceId, Random rng)
    {
        var damaged = new List<string>();
        var ps = ProvinceStateOf(provinceId);
        foreach (var id in ps.Buildings.Keys.OrderBy(k => k).ToList())
            if (id == "walls" || rng.NextDouble() < SiegeDamage)
            {
                LowerBuilding(ps, id);
                damaged.Add(BuildingCatalog.Instance[id]?.Name.ToLowerInvariant() ?? id);
            }
        return damaged;
    }

    /// <summary>Each land good's output factor per node from the province's buildings (good index -> per-node factor).</summary>
    Dictionary<int, float[]>? GoodNodeFactors()
    {
        var goods = GoodsCatalog();
        if (Population == null || Provinces == null || goods == null)
            return null;
        var cat = BuildingCatalog.Instance;
        var index = goods.Goods.ToDictionary(g => g.Id, g => g);
        var perProvince = new Dictionary<int, Dictionary<int, double>>();
        foreach (var p in Provinces.Provinces.Values)
        {
            var ps = ProvinceStateOf(p.Id);
            foreach (var (id, level) in ps.Buildings)
                if (cat[id] is { } b)
                    foreach (var (gid, share) in b.Goods)
                        if (index.TryGetValue(gid, out var g) && g.Source == GoodSource.Land)
                        {
                            if (!perProvince.TryGetValue(p.Id, out var m))
                                perProvince[p.Id] = m = new Dictionary<int, double>();
                            m[g.Index] = m.GetValueOrDefault(g.Index) + share * level;
                        }
        }
        if (perProvince.Count == 0)
            return null;
        var np = NodeProvinces();
        var result = new Dictionary<int, float[]>();
        foreach (int i in Population.LandNodes)
            if (np.Length > i && perProvince.TryGetValue(np[i], out var m))
                foreach (var (g, share) in m)
                {
                    if (!result.TryGetValue(g, out var arr))
                    {
                        result[g] = arr = new float[Population.Pop.Length];
                        Array.Fill(arr, 1f);
                    }
                    arr[i] = (float)(1 + share);
                }
        return result;
    }

    /// <summary>Each realm's extra output of made goods from its workshops, weighted by the provinces' people (realm -> good -> share).</summary>
    Dictionary<int, double[]> MadeGoodBoosts(Dictionary<int, RealmCensus> census)
    {
        var result = new Dictionary<int, double[]>();
        var goods = GoodsCatalog();
        if (Provinces == null || goods == null)
            return result;
        var cat = BuildingCatalog.Instance;
        var index = goods.Goods.ToDictionary(g => g.Id, g => g);
        foreach (var p in Provinces.Provinces.Values)
        {
            if (!census.TryGetValue(p.RealmId, out var c) || c.People <= 0)
                continue;
            var ps = ProvinceStateOf(p.Id);
            if (ps.Buildings.Count == 0)
                continue;
            double weight = ProvincePopulation(p.Id) / c.People;
            foreach (var (id, level) in ps.Buildings)
                if (cat[id] is { } b)
                    foreach (var (gid, share) in b.Goods)
                        if (index.TryGetValue(gid, out var g) && g.Source == GoodSource.Made)
                        {
                            if (!result.TryGetValue(p.RealmId, out var arr))
                                result[p.RealmId] = arr = new double[goods.Goods.Count];
                            arr[g.Index] += share * level * weight;
                        }
        }
        return result;
    }
}
