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
            _census = Population != null
                ? Facsimilia.Game.Census.Take(Population, Provinces, GridWidth, GridHeight, Land)
                : new Dictionary<int, RealmCensus>();
        return _census;
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
            var c = CensusOf(realmId);
            state.Manpower = Economy.SustainableManpower(c);
            double years = 1;
            if (starts.TryGetValue(key, out var s))
            {
                int t = 0;
                foreach (var u in s.GetProperty("units").EnumerateArray())
                    if (t < UnitTypes.Count)
                        state.Units[t++] = u.GetInt32();
                years = s.TryGetProperty("treasury_years", out var y) ? y.GetDouble() : 1;
                state.ElephantSource = s.TryGetProperty("elephant_source", out var e) && e.GetBoolean();
            }
            else
            {
                // No recorded army (a save from before the economy): a modest
                // force in proportion to the realm's people.
                state.Units[UnitTypes.HeavyInfantry] = (int)Math.Max(1, c.People / 300000);
                state.Units[UnitTypes.LightInfantry] = (int)Math.Max(1, c.People / 300000);
                state.Units[UnitTypes.Cavalry] = (int)(c.People / 1000000);
            }
            var (tax, tribute) = Economy.Revenue(c, state.Tax);
            state.Treasury = Math.Round((tax + tribute) * years);
        }
    }

    /// <summary>The game's yearly step, after population: census, then every realm's economy.</summary>
    internal List<ChronicleEvent> GameYear()
    {
        var events = new List<ChronicleEvent>();
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
    }

    public Culture CultureOf(int realmId) =>
        Registry.Realms.TryGetValue(realmId, out var r) ? r.Culture : Culture.Greek;
}
