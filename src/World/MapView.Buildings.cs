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
        if (ps.Building != "")
            return $"Already building: {BuildingCatalog.Instance[ps.Building]?.Name}.";
        if (b.Requires != null && !realm.Techs.Contains(b.Requires))
            return $"Needs the technology: {TechCatalog.Instance[b.Requires]?.Name ?? b.Requires}.";
        if (ps.Level(b.Id) >= b.Max)
            return b.Max == 1 ? "Already built." : "Built as far as it goes.";
        if (b.Needs != null && !ProvinceHas(p.Id, b.Needs))
            return b.Needs switch
            {
                "river" => "Needs a river or floodplain.",
                "hills" => "Needs hilly land.",
                "ore" => "Needs a mineral deposit.",
                "coast" => "Needs a coast.",
                "town" => "Needs a town of 10,000 people or more.",
                _ => "Can't be built here.",
            };
        if (realm.Treasury < b.Cost)
            return $"Not enough silver: {Money(b.Cost)} needed.";
        return null;
    }

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
            var np = NodeProvinces();
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
        realm.Treasury -= b.Cost;
        ps.Building = b.Id;
        ps.BuildingYearsLeft = b.Years;
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
            if (ps.Building == "")
                continue;
            if (Game.Sieges.Any(s => s.ProvinceId == p.Id))
                continue;   // no building under siege
            if (--ps.BuildingYearsLeft > 0)
                continue;
            ps.Buildings[ps.Building] = ps.Level(ps.Building) + 1;
            if (p.RealmId == PlayerRealmId)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                    $"{cat[ps.Building]?.Name} finished in {p.Name}."));
            ps.Building = "";
        }
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
    (double Crafts, double Trade, double Manpower, double Upkeep, bool Harbour) RealmBuildingEffects(int realmId, double realmPeople)
    {
        var tech = TechCatalog.Instance;
        var rs = Game.Realm(realmId);
        if (Provinces == null || realmPeople <= 0)
            return (tech.Effect(rs, "crafts"), tech.Effect(rs, "trade"), tech.Effect(rs, "manpower"), 0, false);
        var cat = BuildingCatalog.Instance;
        double crafts = tech.Effect(rs, "crafts"), trade = tech.Effect(rs, "trade"), manpower = tech.Effect(rs, "manpower"), upkeep = 0;
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
            upkeep += cat.Upkeep(ps);
            harbour |= ps.Level("harbour") > 0;
        }
        return (crafts, trade, manpower, upkeep, harbour);
    }

    /// <summary>Upkeep, levies and warship prices from the buildings, into this year's census.</summary>
    void ApplyBuildingsToCensus(Dictionary<int, RealmCensus> census)
    {
        foreach (var (id, c) in census)
        {
            var e = RealmBuildingEffects(id, c.People);
            c.BuildingUpkeep = e.Upkeep;
            c.ManpowerBonus = e.Manpower;
            if (Game.Realms.TryGetValue(id, out var s))
                s.ShipDiscount = e.Harbour ? BuildingCatalog.Instance["harbour"]?.Effect("ships") ?? 0 : 0;
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
        {
            double repay = Math.Min(s.Debt, s.Treasury);
            s.Debt -= repay;
            s.Treasury -= repay;
        }
        s.RemedyYears[remedy.Id] = remedy.Years;
        return $"{RealmName(PlayerRealmId)}: {remedy.Name.ToLowerInvariant()} ({Money(silver)}).";
    }
}
