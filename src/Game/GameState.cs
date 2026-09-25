using System;
using System.Collections.Generic;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>How hard a realm taxes (decision "Playable 5": one rate the ruler sets).</summary>
public enum TaxRate { Low = 0, Normal = 1, Heavy = 2, Crushing = 3 }

/// <summary>
/// Everything a realm has beyond its land, people and ruling family: its
/// treasury, taxes, debt and army. Money is silver by weight, in talents
/// (decision "Playable 4"): 1 talent = 6,000 drachmae = about 26 kg.
/// </summary>
public sealed class RealmState
{
    public int RealmId { get; }
    public double Treasury { get; set; }   // talents
    public double Debt { get; set; }       // talents owed to temples and bankers
    public TaxRate Tax { get; set; } = TaxRate.Normal;
    public int[] Units { get; } = new int[UnitTypes.Count];   // how many of each (see UnitTypes)
    public double Manpower { get; set; }   // men available to recruit
    public double Fatigue { get; set; }    // 0 rested .. 1 exhausted: campaigning wears armies down
    public string CivKey { get; set; } = "";
    /// <summary>How much more of its people a realm can call up (Rome's Italian allies: 3x; tribal levies: 2x).</summary>
    public double ManpowerMultiplier { get; set; } = 1;
    /// <summary>Share of income a realm is willing to spend on its army in peace (Rome, militarised: more).</summary>
    public double ArmyShare { get; set; } = 0.5;   // which historical realm this is (data/start_realms.json, history_goals.json)
    public bool ElephantSource { get; set; }
    /// <summary>Share of its army's upkeep the realm pays (Rome's allies paid their own contingents: 0.5).</summary>
    public double UpkeepShare { get; set; } = 1;   // can raise elephants without elephant country (Seleucid India trade)

    // Last year's accounts, for the treasury panel.
    public double LastTax { get; set; }
    public double LastTribute { get; set; }
    public double LastAdmin { get; set; }
    public double LastUpkeep { get; set; }
    public double LastInterest { get; set; }
    public double LastCustoms { get; set; }
    /// <summary>Talents from selling captives as slaves last year.</summary>
    public double LastCaptiveSales { get; set; }
    public double LastNet => LastTax + LastTribute + LastCustoms + LastCaptiveSales - LastAdmin - LastUpkeep - LastInterest;
    /// <summary>People taken in war and held as slaves.</summary>
    public double Captives { get; set; }
    /// <summary>The realm's people when the game began (for the goals).</summary>
    public double StartPeople { get; set; }
    /// <summary>Goals reached: goal id -> the year.</summary>
    public Dictionary<string, int> GoalsDone { get; } = new();

    public RealmState(int realmId) => RealmId = realmId;

    public GDictionary ToDict()
    {
        var units = new GArray();
        foreach (int u in Units)
            units.Add(u);
        return new GDictionary
        {
            ["realm"] = RealmId, ["treasury"] = Treasury, ["debt"] = Debt, ["tax"] = (int)Tax,
            ["units"] = units, ["manpower"] = Manpower, ["fatigue"] = Fatigue, ["elephant_source"] = ElephantSource, ["civ"] = CivKey, ["manpower_mult"] = ManpowerMultiplier, ["army_share"] = ArmyShare, ["upkeep_share"] = UpkeepShare,
            ["last"] = new GArray { LastTax, LastTribute, LastAdmin, LastUpkeep, LastInterest, LastCustoms, LastCaptiveSales }, ["captives"] = Captives, ["start_people"] = StartPeople, ["goals"] = GoalsDict(),
        };
    }

    GDictionary GoalsDict()
    {
        var g = new GDictionary();
        foreach (var (id, year) in GoalsDone)
            g[id] = year;
        return g;
    }

    public static RealmState FromDict(GDictionary d)
    {
        var r = new RealmState(d["realm"].AsInt32())
        {
            Treasury = d["treasury"].AsDouble(),
            Debt = d["debt"].AsDouble(),
            Tax = (TaxRate)d["tax"].AsInt32(),
            Manpower = d.TryGetValue("manpower", out var m) ? m.AsDouble() : 0,
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
            ElephantSource = d.TryGetValue("elephant_source", out var e) && e.AsBool(),
            CivKey = d.TryGetValue("civ", out var k) ? k.AsString() : "",
            ManpowerMultiplier = d.TryGetValue("manpower_mult", out var mm) ? mm.AsDouble() : 1,
            ArmyShare = d.TryGetValue("army_share", out var ash) ? ash.AsDouble() : 0.5,
            UpkeepShare = d.TryGetValue("upkeep_share", out var ush) ? ush.AsDouble() : 1,
            Captives = d.TryGetValue("captives", out var cap) ? cap.AsDouble() : 0,
            StartPeople = d.TryGetValue("start_people", out var sp) ? sp.AsDouble() : 0,
        };
        if (d.TryGetValue("goals", out var goals))
            foreach (var (goal, year) in goals.AsGodotDictionary())
                r.GoalsDone[goal.AsString()] = year.AsInt32();
        if (d.TryGetValue("units", out var units))
        {
            var a = units.AsGodotArray();
            for (int i = 0; i < Math.Min(a.Count, r.Units.Length); i++)
                r.Units[i] = a[i].AsInt32();
        }
        if (d.TryGetValue("last", out var last))
        {
            var a = last.AsGodotArray();
            if (a.Count >= 5)
            {
                if (a.Count >= 6)
                    r.LastCustoms = a[5].AsDouble();
                if (a.Count >= 7)
                    r.LastCaptiveSales = a[6].AsDouble();
                r.LastTax = a[0].AsDouble(); r.LastTribute = a[1].AsDouble(); r.LastAdmin = a[2].AsDouble();
                r.LastUpkeep = a[3].AsDouble(); r.LastInterest = a[4].AsDouble();
            }
        }
        return r;
    }
}

/// <summary>
/// The game's own state on top of the map, people and families: every
/// realm's treasury and army, the turn length, and (later) wars and
/// treaties. Saved with the game.
/// </summary>
public sealed class GameState
{
    /// <summary>Turn lengths the player can pick (decision "Playable 2").</summary>
    public static readonly int[] TurnLengths = { 1, 5, 10, 25 };

    public Dictionary<int, RealmState> Realms { get; } = new();
    public int YearsPerTurn { get; set; } = 1;
    public Wars Wars { get; } = new();
    /// <summary>Realms at war with the player that have sued for peace.</summary>
    public HashSet<int> PeaceOffers { get; } = new();
    /// <summary>Every province's culture, religion, integration and unrest, by province id.</summary>
    public Dictionary<int, ProvinceState> Provinces { get; } = new();
    /// <summary>Every geographic region's soil, forest, pasture and fish, and this year's harvest, by region id.</summary>
    public Dictionary<int, RegionNature> Nature { get; } = new();

    public RealmState Realm(int id)
    {
        if (!Realms.TryGetValue(id, out var r))
            Realms[id] = r = new RealmState(id);
        return r;
    }

    public GDictionary ToDict()
    {
        var realms = new GArray();
        foreach (var r in Realms.Values)
            realms.Add(r.ToDict());
        var offers = new GArray();
        foreach (int o in PeaceOffers)
            offers.Add(o);
        var nature = new GDictionary();
        foreach (var (id, n) in Nature)
            nature[id.ToString()] = n.ToDict();
        var provinces = new GDictionary();
        foreach (var (id, p) in Provinces)
            provinces[id.ToString()] = p.ToDict();
        return new GDictionary
        {
            ["provinces"] = provinces, ["nature"] = nature,
            ["realms"] = realms, ["years_per_turn"] = YearsPerTurn, ["wars"] = Wars.ToArray(), ["peace_offers"] = offers,
        };
    }

    public static GameState FromDict(GDictionary d)
    {
        var g = new GameState();
        if (d.TryGetValue("realms", out var realms))
            foreach (Variant v in realms.AsGodotArray())
            {
                var r = RealmState.FromDict(v.AsGodotDictionary());
                g.Realms[r.RealmId] = r;
            }
        if (d.TryGetValue("years_per_turn", out var y) && Array.IndexOf(TurnLengths, y.AsInt32()) >= 0)
            g.YearsPerTurn = y.AsInt32();
        if (d.TryGetValue("wars", out var w))
            g.Wars.Load(w.AsGodotArray());
        if (d.TryGetValue("peace_offers", out var po))
            foreach (Variant v in po.AsGodotArray())
                g.PeaceOffers.Add(v.AsInt32());
        if (d.TryGetValue("nature", out var na))
            foreach (var (k, v) in na.AsGodotDictionary())
                g.Nature[int.Parse(k.AsString())] = RegionNature.FromDict(v.AsGodotDictionary());
        if (d.TryGetValue("provinces", out var pr))
            foreach (var (k, v) in pr.AsGodotDictionary())
                g.Provinces[int.Parse(k.AsString())] = ProvinceState.FromDict(v.AsGodotDictionary());
        return g;
    }
}
