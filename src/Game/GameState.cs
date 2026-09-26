using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>The realm's named armies (and fleets), each with its own units.</summary>
    public List<Army> Armies { get; } = new();
    public int NextArmyId { get; set; } = 1;
    public double Manpower { get; set; }   // men available to recruit
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
    public double LastNet => LastTax + LastTribute + LastCustoms + LastCaptiveSales + LastVassalTribute - LastAdmin - LastUpkeep - LastInterest;
    /// <summary>How much other realms fear this one's conquests, 0..100; fades by itself.</summary>
    public double Aggression { get; set; }
    /// <summary>Last year's tribute paid (negative) or received from vassals, talents.</summary>
    public double LastVassalTribute { get; set; }
    /// <summary>Remedies for an empty treasury still being felt: id -> years left.</summary>
    public Dictionary<string, int> RemedyYears { get; } = new();
    /// <summary>Harbours make warships this much cheaper (not saved: set each year from the buildings).</summary>
    public double ShipDiscount { get; set; }
    /// <summary>People taken in war and held as slaves.</summary>
    public double Captives { get; set; }
    /// <summary>The realm's people when the game began (for the goals).</summary>
    public double StartPeople { get; set; }
    /// <summary>Goals reached: goal id -> the year.</summary>
    public Dictionary<string, int> GoalsDone { get; } = new();

    public RealmState(int realmId) => RealmId = realmId;

    public Army? ArmyById(int id) => Armies.FirstOrDefault(a => a.Id == id);

    /// <summary>A new, empty army standing on a node.</summary>
    public Army NewArmy(string name, int node)
    {
        var a = new Army(UnitCatalog.Instance.Count) { Id = NextArmyId++, Name = name, Node = node };
        Armies.Add(a);
        return a;
    }

    /// <summary>How many units of a role the realm has in all its armies.</summary>
    public int RoleCount(int role) => Armies.Sum(a => a.RoleCount(UnitCatalog.Instance, role));

    public int TotalUnits => Armies.Sum(a => a.Count);

    public GDictionary ToDict()
    {
        var cat = UnitCatalog.Instance;
        var armies = new GArray();
        foreach (var a in Armies)
            armies.Add(a.ToDict(cat));
        return new GDictionary
        {
            ["realm"] = RealmId, ["treasury"] = Treasury, ["debt"] = Debt, ["tax"] = (int)Tax,
            ["armies"] = armies, ["next_army"] = NextArmyId, ["manpower"] = Manpower, ["elephant_source"] = ElephantSource, ["civ"] = CivKey, ["manpower_mult"] = ManpowerMultiplier, ["army_share"] = ArmyShare, ["upkeep_share"] = UpkeepShare,
            ["last"] = new GArray { LastTax, LastTribute, LastAdmin, LastUpkeep, LastInterest, LastCustoms, LastCaptiveSales }, ["captives"] = Captives, ["remedies"] = RemedyDict(), ["aggression"] = Aggression, ["vassal_tribute"] = LastVassalTribute, ["start_people"] = StartPeople, ["goals"] = GoalsDict(),
        };
    }

    GDictionary RemedyDict()
    {
        var g = new GDictionary();
        foreach (var (id, y) in RemedyYears)
            g[id] = y;
        return g;
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
            ElephantSource = d.TryGetValue("elephant_source", out var e) && e.AsBool(),
            CivKey = d.TryGetValue("civ", out var k) ? k.AsString() : "",
            ManpowerMultiplier = d.TryGetValue("manpower_mult", out var mm) ? mm.AsDouble() : 1,
            ArmyShare = d.TryGetValue("army_share", out var ash) ? ash.AsDouble() : 0.5,
            UpkeepShare = d.TryGetValue("upkeep_share", out var ush) ? ush.AsDouble() : 1,
            Captives = d.TryGetValue("captives", out var cap) ? cap.AsDouble() : 0,
            Aggression = d.TryGetValue("aggression", out var agg) ? agg.AsDouble() : 0,
            LastVassalTribute = d.TryGetValue("vassal_tribute", out var vt) ? vt.AsDouble() : 0,
            StartPeople = d.TryGetValue("start_people", out var sp) ? sp.AsDouble() : 0,
        };
        if (d.TryGetValue("remedies", out var rem))
            foreach (var (id, y) in rem.AsGodotDictionary())
                r.RemedyYears[id.AsString()] = y.AsInt32();
        if (d.TryGetValue("goals", out var goals))
            foreach (var (goal, year) in goals.AsGodotDictionary())
                r.GoalsDone[goal.AsString()] = year.AsInt32();
        var cat = UnitCatalog.Instance;
        if (d.TryGetValue("armies", out var armies))
            foreach (Variant v in armies.AsGodotArray())
                r.Armies.Add(Army.FromDict(v.AsGodotDictionary(), cat));
        else if (d.TryGetValue("units", out var units))
        {
            // A save from before named armies: the realm's pool becomes one army.
            var a = units.AsGodotArray();
            var army = new Army(cat.Count) { Id = 1, Name = "The army", Node = -1 };
            for (int i = 0; i < Math.Min(a.Count, UnitRoles.Count); i++)
                army.Units[cat[UnitRoles.Generic[i]].Index] += a[i].AsInt32();
            r.Armies.Add(army);
        }
        r.NextArmyId = d.TryGetValue("next_army", out var na) ? na.AsInt32() : r.Armies.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
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
    public Treaties Treaties { get; } = new();
    /// <summary>Provinces each realm lost, and when (for the Reconquest pretext): realm -> province -> year.</summary>
    public Dictionary<int, Dictionary<int, int>> Lost { get; } = new();

    public void RecordLoss(int realm, int province, int year)
    {
        if (realm <= 0 || province == 0)
            return;
        if (!Lost.TryGetValue(realm, out var m))
            Lost[realm] = m = new Dictionary<int, int>();
        m[province] = year;
    }
    /// <summary>Offers other realms make the player (peace with tribute, ransom for a siege), answered in Diplomacy.</summary>
    public List<Offer> Offers { get; } = new();
    /// <summary>Realms at war with the player that have sued for peace.</summary>
    public HashSet<int> PeaceOffers { get; } = new();
    /// <summary>Every province's culture, religion, integration and unrest, by province id.</summary>
    public Dictionary<int, ProvinceState> Provinces { get; } = new();
    /// <summary>Every geographic region's soil, forest, pasture and fish, and this year's harvest, by region id.</summary>
    public Dictionary<int, RegionNature> Nature { get; } = new();
    /// <summary>Sieges under way, by any realm.</summary>
    public List<Siege> Sieges { get; } = new();

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
            ["provinces"] = provinces, ["nature"] = nature, ["sieges"] = SiegesArray(),
            ["realms"] = realms, ["years_per_turn"] = YearsPerTurn, ["wars"] = Wars.ToArray(), ["peace_offers"] = offers,
            ["treaties"] = Treaties.ToArray(), ["lost"] = LostArray(), ["offers"] = new GArray(Offers.Select(o => (Variant)o.ToDict()).ToArray()),
        };
    }

    GArray LostArray()
    {
        var a = new GArray();
        foreach (var (realm, m) in Lost)
            foreach (var (prov, year) in m)
                a.Add(new GArray { realm, prov, year });
        return a;
    }

    GArray SiegesArray()
    {
        var a = new GArray();
        foreach (var s in Sieges)
            a.Add(s.ToDict());
        return a;
    }

    public static GameState FromDict(GDictionary d)
    {
        var g = new GameState();
        if (d.TryGetValue("sieges", out var sg))
            foreach (Variant v in sg.AsGodotArray())
                g.Sieges.Add(Siege.FromDict(v.AsGodotDictionary()));
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
        if (d.TryGetValue("lost", out var lost))
            foreach (Variant v in lost.AsGodotArray())
            {
                var t = v.AsGodotArray();
                g.RecordLoss(t[0].AsInt32(), t[1].AsInt32(), t[2].AsInt32());
            }
        if (d.TryGetValue("treaties", out var tr))
            g.Treaties.Load(tr.AsGodotArray());
        if (d.TryGetValue("offers", out var of))
            foreach (Variant v in of.AsGodotArray())
                g.Offers.Add(Offer.FromDict(v.AsGodotDictionary()));
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
