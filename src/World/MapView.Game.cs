using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>
/// The game on top of the map: every realm's treasury and army (GameState),
/// set up from data/start_realms.json in 300 BC and advanced each year after
/// the population.
/// </summary>
public partial class MapView
{
    public const string StartRealmsPath = "res://data/start_realms.json";

    public GameState Game { get; private set; } = new();
    Dictionary<int, RealmCensus>? _census;

    /// <summary>This year's count of every realm's people, provinces and resources.</summary>
    public Dictionary<int, RealmCensus> RealmCensus()
    {
        if (_census == null)
        {
            _census = Population != null
                ? Facsimilia.Game.Census.Take(Population, Provinces, GridWidth, GridHeight, Land, ProvinceYield,
                    prov => Game.Provinces.TryGetValue(prov, out var ps) ? ps.Tax : null)
                : new Dictionary<int, RealmCensus>();
            CountLabour(_census);
            ApplyBuildingsToCensus(_census);
            var cat = GoodsCatalog();
            if (cat != null && Population != null && Land != null && Crops != null)
            {
                // Production is counted once a year (it takes a tenth of a second).
                if (_goodsYear != DemoYear || _realmGoods == null)
                {
                    var census = _census;
                    var crafts = census.ToDictionary(kv => kv.Key, kv => RealmBuildingEffects(kv.Key, kv.Value.People).Crafts);
                    _realmGoods = GoodsEngine.Compute(cat, Population, f => Land.Has(f) ? Land.Bytes(f) : null,
                        workFactor: WorkFactors(), fieldYield: Crops.FieldKcalPerHa, nodeFactor: BuildingNodeFactors(),
                        craftBoost: crafts, realmWorkBoost: (realm, work) =>
                            TechCatalog.Instance.Effect(Game.Realm(realm), work is "earth" ? "mine" : work));
                    foreach (var (realm, goods) in _realmGoods)
                        if (census.TryGetValue(realm, out var rc))
                            goods.TradeBonus = RealmBuildingEffects(realm, rc.People).Trade;
                    PriceFactors = RunTrade(cat, _realmGoods, _census);
                    _goodsYear = DemoYear;
                }
                foreach (var (realm, goods) in _realmGoods)
                    if (_census.TryGetValue(realm, out var c))
                        c.Goods = goods;
            }
        }
        return _census;
    }

    GoodsCatalog? _goods;
    /// <summary>This year's price of each good against its usual price (trade).</summary>
    public double[] PriceFactors { get; private set; } = Array.Empty<double>();
    Dictionary<int, RealmGoods>? _realmGoods;
    int _goodsYear = int.MinValue;

    /// <summary>The goods and their recipes (data/goods.json), or null if missing.</summary>
    public GoodsCatalog? GoodsCatalog()
    {
        if (_goods == null && Godot.FileAccess.FileExists(Facsimilia.Game.GoodsCatalog.Path))
            _goods = Facsimilia.Game.GoodsCatalog.Parse(Godot.FileAccess.GetFileAsString(Facsimilia.Game.GoodsCatalog.Path));
        return _goods;
    }

    public RealmCensus CensusOf(int realmId) =>
        RealmCensus().TryGetValue(realmId, out var c) ? c : new RealmCensus();

    public RealmState PlayerState => Game.Realm(PlayerRealmId);

    /// <summary>A new game in 300 BC: armies and treasuries from data/start_realms.json.</summary>
    internal void StartGame()
    {
        Game = new GameState();
        _census = null;
        var starts = new Dictionary<string, JsonElement>();
        JsonElement? startRoot = null;
        if (Godot.FileAccess.FileExists(StartRealmsPath))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(StartRealmsPath));
            foreach (var p in doc.RootElement.GetProperty("realms").EnumerateObject())
                starts[p.Name] = p.Value.Clone();
            startRoot = doc.RootElement.Clone();
        }
        var roles = new Dictionary<int, int[]>();
        foreach (var (key, realmId) in CivRealmIds)
        {
            var state = Game.Realm(realmId);
            state.CivKey = key;
            var c = CensusOf(realmId);
            double years = 1;
            var counts = roles[realmId] = new int[UnitRoles.Count];
            if (starts.TryGetValue(key, out var s))
            {
                int t = 0;
                foreach (var u in s.GetProperty("units").EnumerateArray())
                    if (t < UnitRoles.Count)
                        counts[t++] = u.GetInt32();
                years = s.TryGetProperty("treasury_years", out var y) ? y.GetDouble() : 1;
                state.ElephantSource = s.TryGetProperty("elephant_source", out var e) && e.GetBoolean();
                state.ManpowerMultiplier = s.TryGetProperty("manpower", out var m) ? m.GetDouble() : 1;
                state.ArmyShare = s.TryGetProperty("army_share", out var a) ? a.GetDouble() : 0.5;
                state.UpkeepShare = s.TryGetProperty("upkeep_share", out var us) ? us.GetDouble() : 1;
            }
            else
            {
                // No recorded army (a save from before the economy): a modest
                // force in proportion to the realm's people.
                counts[UnitRoles.HeavyInfantry] = (int)Math.Max(1, c.People / 300000);
                counts[UnitRoles.LightInfantry] = (int)Math.Max(1, c.People / 300000);
                counts[UnitRoles.Cavalry] = (int)(c.People / 1000000);
            }
            state.Manpower = Economy.SustainableManpower(c, state.ManpowerMultiplier);
            state.StartPeople = c.People;
            var (tax, tribute) = Economy.Revenue(c, state.Tax);
            state.Treasury = Math.Round((tax + tribute) * years);
        }
        StartLoyalty();
        StartTechs();
        if (startRoot is { } root)
            StartTreaties(root);
        foreach (var (realmId, counts) in roles)
            StartArmy(Game.Realm(realmId), counts);
        RefreshRoleBoosts();
        _census = null;
        RefreshArmyMarkers();
    }

    /// <summary>The game's yearly step, after population: census, then every realm's economy.</summary>
    internal List<ChronicleEvent> GameYear()
    {
        var events = new List<ChronicleEvent>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string what)
        {
            if (Profile)
                GD.Print($"  {what}: {sw.ElapsedMilliseconds} ms");
            sw.Restart();
        }
        events.AddRange(NatureYear());
        Lap("nature");
        events.AddRange(DisastersYear());
        Lap("disasters");
        _census = null;
        events.AddRange(BotsYear(new Random(StableHash.Of(DemoYear, 7919))));
        Lap("other realms");
        events.AddRange(SiegesYear(new Random(StableHash.Of(DemoYear, 5381))));
        Lap("sieges");
        events.AddRange(DiplomacyYear(new Random(StableHash.Of(DemoYear, 2903))));
        Lap("diplomacy");
        events.AddRange(BuildingsYear(new Random(StableHash.Of(DemoYear, 6007))));
        ApplyBuildingGrowth();
        Lap("buildings");
        events.AddRange(TechYear());
        events.AddRange(RivalsYear(new Random(StableHash.Of(DemoYear, 8111))));
        Lap("research, rivals");
        events.AddRange(LoyaltyYear(new Random(StableHash.Of(DemoYear, 104729))));
        Lap("loyalty");
        _census = null;
        var census = RealmCensus();
        Lap("census and goods");
        foreach (var realm in Registry.Realms.Values)
        {
            if (!census.TryGetValue(realm.Id, out var c))
                continue;   // no land left
            var state = Game.Realm(realm.Id);
            string? news = Economy.Tick(state, c, Corruption(realm.Id));
            if (news != null)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, realm.Id, $"{realm.Name}: {news}"));
        }
        events.AddRange(LabourYear());
        events.AddRange(GoalsYear());
        if (Population != null)
            Economy.ApplyBurden(Population, Game.Realms);
        Lap("economy, labour, goals");
        return events;
    }

    /// <summary>Prints how long each part of the year takes (for finding slow spots).</summary>
    public static bool Profile { get; set; }

    internal void LoadGameState(GDictionary? data)
    {
        _census = null;
        if (data != null && data.Count > 0)
            Game = GameState.FromDict(data);
        else
        {
            // A save from before the economy: start every realm afresh.
            foreach (var realm in Registry.Realms.Values)
                if (!CivRealmIds.ContainsValue(realm.Id))
                    CivRealmIds[realm.Id.ToString()] = realm.Id;
            StartGame();
        }
        if (Game.Provinces.Count == 0)
            StartLoyalty();   // a save from before cultures
        PlaceUnplacedArmies();
        if (Game.Realms.Values.All(r => r.Techs.Count == 0))
            StartTechs();   // a save from before technology
        RefreshRoleBoosts();
        if (Godot.FileAccess.FileExists(StartRealmsPath))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(StartRealmsPath));
            _rivals.Clear();
            LoadRivals(doc.RootElement);
        }
    }

    public Culture CultureOf(int realmId) =>
        Registry.Realms.TryGetValue(realmId, out var r) ? r.Culture : Culture.Greek;
}
