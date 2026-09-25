using System;
using System.Collections.Generic;
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
                ? Facsimilia.Game.Census.Take(Population, Provinces, GridWidth, GridHeight, Land, ProvinceYield)
                : new Dictionary<int, RealmCensus>();
            var cat = GoodsCatalog();
            if (cat != null && Population != null && Land != null && Crops != null)
            {
                // Production is counted once a year (it takes a tenth of a second).
                if (_goodsYear != DemoYear || _realmGoods == null)
                {
                    _realmGoods = GoodsEngine.Compute(cat, Population, f => Land.Has(f) ? Land.Bytes(f) : null,
                        workFactor: WorkFactors(), fieldYield: Crops.FieldKcalPerHa);
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
        if (Godot.FileAccess.FileExists(StartRealmsPath))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(StartRealmsPath));
            foreach (var p in doc.RootElement.GetProperty("realms").EnumerateObject())
                starts[p.Name] = p.Value.Clone();
        }
        foreach (var (key, realmId) in CivRealmIds)
        {
            var state = Game.Realm(realmId);
            state.CivKey = key;
            var c = CensusOf(realmId);
            double years = 1;
            if (starts.TryGetValue(key, out var s))
            {
                int t = 0;
                foreach (var u in s.GetProperty("units").EnumerateArray())
                    if (t < UnitTypes.Count)
                        state.Units[t++] = u.GetInt32();
                years = s.TryGetProperty("treasury_years", out var y) ? y.GetDouble() : 1;
                state.ElephantSource = s.TryGetProperty("elephant_source", out var e) && e.GetBoolean();
                state.ManpowerMultiplier = s.TryGetProperty("manpower", out var m) ? m.GetDouble() : 1;
                state.ArmyShare = s.TryGetProperty("army_share", out var a) ? a.GetDouble() : 0.5;
            }
            else
            {
                // No recorded army (a save from before the economy): a modest
                // force in proportion to the realm's people.
                state.Units[UnitTypes.HeavyInfantry] = (int)Math.Max(1, c.People / 300000);
                state.Units[UnitTypes.LightInfantry] = (int)Math.Max(1, c.People / 300000);
                state.Units[UnitTypes.Cavalry] = (int)(c.People / 1000000);
            }
            state.Manpower = Economy.SustainableManpower(c, state.ManpowerMultiplier);
            var (tax, tribute) = Economy.Revenue(c, state.Tax);
            state.Treasury = Math.Round((tax + tribute) * years);
        }
        StartLoyalty();
        _census = null;
    }

    /// <summary>The game's yearly step, after population: census, then every realm's economy.</summary>
    internal List<ChronicleEvent> GameYear()
    {
        var events = new List<ChronicleEvent>();
        events.AddRange(NatureYear());
        _census = null;
        events.AddRange(BotsYear(new Random(HashCode.Combine(DemoYear, 7919))));
        Game.PeaceOffers.RemoveWhere(o => !Game.Wars.AtWar(o, PlayerRealmId));
        events.AddRange(LoyaltyYear(new Random(HashCode.Combine(DemoYear, 104729))));
        _census = null;
        var census = RealmCensus();
        foreach (var realm in Registry.Realms.Values)
        {
            if (!census.TryGetValue(realm.Id, out var c))
                continue;   // no land left
            var state = Game.Realm(realm.Id);
            string? news = Economy.Tick(state, c);
            if (news != null)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, realm.Id, $"{realm.Name}: {news}"));
        }
        if (Population != null)
            Economy.ApplyBurden(Population, Game.Realms);
        return events;
    }

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
    }

    public Culture CultureOf(int realmId) =>
        Registry.Realms.TryGetValue(realmId, out var r) ? r.Culture : Culture.Greek;
}
