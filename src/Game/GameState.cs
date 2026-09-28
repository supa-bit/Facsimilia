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
    /// <summary>Last year's court, temples and public works.</summary>
    public double LastCivil { get; set; }
    /// <summary>
    /// How much of the usual tax this realm's state can take (start_realms.json
    /// "tax_reach"): the Ptolemies' royal monopolies more, Republican Rome,
    /// whose citizens paid only a war tax, far less.
    /// </summary>
    public double TaxReach { get; set; } = 1;
    public double LastUpkeep { get; set; }
    public double LastInterest { get; set; }
    /// <summary>Mercenary pay so far this year (paid month by month, straight from the treasury).</summary>
    public double MercPaidThisYear { get; set; }
    /// <summary>Last year's mercenary pay (shown in the accounts; already out of the treasury, so not in LastNet).</summary>
    public double LastMercPay { get; set; }
    public double LastCustoms { get; set; }
    /// <summary>Share of its sea trade enemy fleets cut off this month (decision "Next 5": blockades).</summary>
    public double Blockade { get; set; }
    /// <summary>This year's blockade so far, summed month by month (the year's customs fall by its average).</summary>
    public double BlockadeMonths { get; set; }
    /// <summary>Talents from selling captives as slaves last year.</summary>
    public double LastCaptiveSales { get; set; }
    public double LastNet => LastTax + LastTribute + LastCustoms + LastCaptiveSales + LastVassalTribute - LastCivil - LastAdmin - LastUpkeep - LastInterest;
    /// <summary>How much other realms fear this one's conquests, 0..100; fades by itself.</summary>
    public double Aggression { get; set; }
    /// <summary>Last year's tribute paid (negative) or received from vassals, talents.</summary>
    public double LastVassalTribute { get; set; }
    /// <summary>How strong an ambitious rival within the realm has grown, 0..100 (at 100 he rises).</summary>
    public double RivalStrength { get; set; }
    /// <summary>Technologies known, the one being studied, and the points gathered toward it.</summary>
    public HashSet<string> Techs { get; } = new();
    public string Researching { get; set; } = "";
    public double ResearchPoints { get; set; }
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

    /// <summary>The realm's generals (decision "Next 2"): those leading an army and those waiting for one.</summary>
    public List<General> Generals { get; } = new();
    public int NextGeneralId { get; set; } = 1;
    public General? GeneralOf(Army a) => a.GeneralId == 0 ? null : Generals.FirstOrDefault(g => g.Id == a.GeneralId);

    /// <summary>Every 5 years: year, people, silver (talents), soldiers, score — for the realms table's graphs.</summary>
    public List<float[]> History { get; } = new();

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
            ["last"] = new GArray { LastTax, LastTribute, LastAdmin, LastUpkeep, LastInterest, LastCustoms, LastCaptiveSales, LastCivil }, ["tax_reach"] = TaxReach, ["blockade"] = new GArray { Blockade, BlockadeMonths }, ["captives"] = Captives, ["remedies"] = RemedyDict(), ["techs"] = new GArray(Techs.Select(t => (Variant)t).ToArray()),
            ["researching"] = Researching, ["rival"] = RivalStrength, ["research_points"] = ResearchPoints, ["aggression"] = Aggression, ["vassal_tribute"] = LastVassalTribute, ["start_people"] = StartPeople, ["goals"] = GoalsDict(),
            ["history"] = new GArray(History.Select(h => (Variant)h).ToArray()),
            ["merc_pay"] = new GArray { MercPaidThisYear, LastMercPay },
            ["generals"] = new GArray(Generals.Select(g => (Variant)g.ToDict()).ToArray()), ["next_general"] = NextGeneralId,
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
            TaxReach = d.TryGetValue("tax_reach", out var txr) ? txr.AsDouble() : 1,
            Blockade = d.TryGetValue("blockade", out var bk) ? bk.AsGodotArray()[0].AsDouble() : 0,
            BlockadeMonths = d.TryGetValue("blockade", out var bk2) ? bk2.AsGodotArray()[1].AsDouble() : 0,
            Captives = d.TryGetValue("captives", out var cap) ? cap.AsDouble() : 0,
            Aggression = d.TryGetValue("aggression", out var agg) ? agg.AsDouble() : 0,
            LastVassalTribute = d.TryGetValue("vassal_tribute", out var vt) ? vt.AsDouble() : 0,
            StartPeople = d.TryGetValue("start_people", out var sp) ? sp.AsDouble() : 0,
        };
        if (d.TryGetValue("techs", out var techs))
            foreach (Variant t in techs.AsGodotArray())
                r.Techs.Add(t.AsString());
        r.Researching = d.TryGetValue("researching", out var rs) ? rs.AsString() : "";
        r.RivalStrength = d.TryGetValue("rival", out var rv) ? rv.AsDouble() : 0;
        r.ResearchPoints = d.TryGetValue("research_points", out var rp) ? rp.AsDouble() : 0;
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
                if (a.Count >= 8)
                    r.LastCivil = a[7].AsDouble();
                r.LastTax = a[0].AsDouble(); r.LastTribute = a[1].AsDouble(); r.LastAdmin = a[2].AsDouble();
                r.LastUpkeep = a[3].AsDouble(); r.LastInterest = a[4].AsDouble();
            }
        }
        if (d.TryGetValue("merc_pay", out var mp))
        {
            var mpa = mp.AsGodotArray();
            r.MercPaidThisYear = mpa[0].AsDouble();
            r.LastMercPay = mpa[1].AsDouble();
        }
        if (d.TryGetValue("generals", out var gens))
            foreach (Variant v in gens.AsGodotArray())
                r.Generals.Add(General.FromDict(v.AsGodotDictionary()));
        r.NextGeneralId = d.TryGetValue("next_general", out var ng) ? ng.AsInt32() : r.Generals.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
        if (d.TryGetValue("history", out var hist))
            foreach (Variant v in hist.AsGodotArray())
                r.History.Add(v.AsFloat32Array());
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
    /// <summary>Mercenary companies, waiting at their hiring grounds or serving a realm.</summary>
    public List<Company> Companies { get; } = new();
    public int NextCompanyId { get; set; } = 1;
    /// <summary>History's companies already brought into the game (each appears once).</summary>
    public HashSet<string> CompaniesSeen { get; } = new();
    /// <summary>History's campaigns checked at their end date (index in history_goals.json -> it happened as in history).</summary>
    public Dictionary<int, bool> HistoryChecks { get; } = new();
    /// <summary>The battles fought, newest last (the last BattlesKept).</summary>
    public List<BattleReport> Battles { get; } = new();
    /// <summary>Months of the present year already marched (armies move month by month).</summary>
    public int MonthsMarched { get; set; }
    public const int BattlesKept = 300;
    public void AddBattle(BattleReport r)
    {
        Battles.Add(r);
        if (Battles.Count > BattlesKept)
            Battles.RemoveRange(0, Battles.Count - BattlesKept);
    }
    GDictionary HistoryChecksDict()
    {
        var d = new GDictionary();
        foreach (var (k, v) in HistoryChecks)
            d[k] = v;
        return d;
    }

    /// <summary>The player's rulers in turn: the year each took the throne, and their name.</summary>
    public List<(int Year, string Name)> RulerLog { get; } = new();
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
            ["treaties"] = Treaties.ToArray(), ["lost"] = LostArray(),
            ["history_checks"] = HistoryChecksDict(), ["months_marched"] = MonthsMarched, ["battles"] = new GArray(Battles.Select(b => (Variant)b.ToDict()).ToArray()),
            ["ruler_log"] = new GArray(RulerLog.Select(r => (Variant)new GArray { r.Year, r.Name }).ToArray()), ["offers"] = new GArray(Offers.Select(o => (Variant)o.ToDict()).ToArray()),
            ["companies"] = new GArray(Companies.Select(c => (Variant)c.ToDict()).ToArray()), ["next_company"] = NextCompanyId,
            ["companies_seen"] = new GArray(CompaniesSeen.Select(c => (Variant)c).ToArray()),
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
        g.MonthsMarched = d.TryGetValue("months_marched", out var mm) ? mm.AsInt32() : 0;
        if (d.TryGetValue("companies", out var cos))
            foreach (Variant v in cos.AsGodotArray())
                g.Companies.Add(Company.FromDict(v.AsGodotDictionary()));
        g.NextCompanyId = d.TryGetValue("next_company", out var nco) ? nco.AsInt32() : g.Companies.Select(c => c.Id).DefaultIfEmpty(0).Max() + 1;
        if (d.TryGetValue("companies_seen", out var csn))
            foreach (Variant v in csn.AsGodotArray())
                g.CompaniesSeen.Add(v.AsString());
        if (d.TryGetValue("battles", out var bt))
            foreach (Variant v in bt.AsGodotArray())
                g.Battles.Add(BattleReport.FromDict(v.AsGodotDictionary()));
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
        if (d.TryGetValue("history_checks", out var hc))
            foreach (var (k, v) in hc.AsGodotDictionary())
                g.HistoryChecks[k.AsInt32()] = v.AsBool();
        if (d.TryGetValue("ruler_log", out var rl))
            foreach (Variant v in rl.AsGodotArray())
                g.RulerLog.Add((v.AsGodotArray()[0].AsInt32(), v.AsGodotArray()[1].AsString()));
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
