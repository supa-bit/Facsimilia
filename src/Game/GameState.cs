using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>How much the fog of war hides (your answer: off, light or full).</summary>
public enum FogLevel { Off, Light, Full }

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
    /// <summary>All the realm owes (its loans together). Setting it replaces them with one bankers' loan (old saves, tests).</summary>
    public double Debt
    {
        get => Loans.Sum(l => l.Amount);
        set
        {
            Loans.Clear();
            if (value > 0)
                Loans.Add(new Loan { Lender = Loan.BankersLender, Amount = value, Rate = Finance.BankerRate });
        }
    }
    /// <summary>Markets where its merchants work (market ids; decision "Merchants").</summary>
    public List<string> Merchants { get; } = new();
    /// <summary>Grain prices limited by edict (decision "Setting prices").</summary>
    public bool PriceLimit { get; set; }
    /// <summary>Last year's shortage under a price limit: the value of grain it went without, drachmae.</summary>
    public double LastShortage { get; set; }
    /// <summary>Realms wronged by this year's default (not saved: turned into grievances the same year).</summary>
    public List<int> Wronged { get; } = new();
    /// <summary>Its debts, lender by lender (decision "Who lends").</summary>
    public List<Loan> Loans { get; } = new();
    /// <summary>Bankers won't lend again until this year (after a default).</summary>
    public int BankersRefuseUntil { get; set; } = int.MinValue;
    /// <summary>Years the gods' curse still lies on the realm (after keeping the temples' silver).</summary>
    public int TempleCurseYears { get; set; }
    /// <summary>Years in a row the realm could neither pay nor borrow; at two it defaults.</summary>
    public int UnpaidYears { get; set; }
    /// <summary>Each spending line, 0 (nothing) .. 2 (double); 1 is the usual (decision "Setting your spending").</summary>
    public double[] Spending { get; } = Finance.DefaultSpending();
    /// <summary>Who collects the taxes, and years left on a tax-farming contract.</summary>
    public Collectors Collectors { get; set; }
    public int ContractYears { get; set; }
    /// <summary>The coin's silver content, 1 = full (decision "How debasing works").</summary>
    public double CoinPurity { get; set; } = 1;
    /// <summary>The price level in the realm's coin, 1 = as in 300 BC; follows a debased coin over the years.</summary>
    public double PriceLevel { get; set; } = 1;
    /// <summary>Last year's mint profit from a debased coin.</summary>
    public double LastMint { get; set; }
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
    public double LastNet => LastTax + LastTribute + LastCustoms + LastMonopoly + LastCaptiveSales + LastVassalTribute - LastCivil - LastAdmin - LastUpkeep - LastInterest;
    /// <summary>How much other realms fear this one's conquests, 0..100; fades by itself.</summary>
    public double Aggression { get; set; }
    /// <summary>
    /// Whether this realm keeps its word (your answer "Trust and fear": trust
    /// kept apart from fear), 0 to 100, 50 to begin with: broken alliances,
    /// unpaid indemnities and wars without cause lower it, alliances honoured
    /// raise it, and it drifts back toward 50 over the years.
    /// </summary>
    public double Trust { get; set; } = 50;
    /// <summary>Last year's tribute paid (negative) or received from vassals, talents.</summary>
    public double LastVassalTribute { get; set; }
    /// <summary>How strong an ambitious rival within the realm has grown, 0..100 (at 100 he rises).</summary>
    public double RivalStrength { get; set; }
    /// <summary>Technologies known, the one being studied, and the points gathered toward it.</summary>
    public HashSet<string> Techs { get; } = new();
    /// <summary>The effects of the wonders the realm holds, worked out each year (not saved).</summary>
    public Dictionary<string, double> WonderEffects { get; } = new();
    /// <summary>The effects of the ruler's skills and traits (CharacterRegistry.RulerEffects), worked out each year (not saved).</summary>
    public Dictionary<string, double> RulerEffects { get; } = new();
    /// <summary>
    /// Research by branch (decision "Researching several at once"): the share
    /// of effort each branch gets, the points it has gathered, and what it studies.
    /// </summary>
    public double[] BranchShare { get; } = Enumerable.Repeat(1.0, TechCatalog.Branches.Length).ToArray();
    public double[] BranchPoints { get; } = new double[TechCatalog.Branches.Length];
    public string[] BranchResearching { get; } = Enumerable.Repeat("", TechCatalog.Branches.Length).ToArray();
    /// <summary>Is anything being studied?</summary>
    public bool Studying => BranchResearching.Any(x => x != "");
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
    public double RoleCount(int role) => Armies.Sum(a => a.RoleCount(UnitCatalog.Instance, role));

    public int TotalUnits => Armies.Sum(a => a.Count) + Musters.Count;
    /// <summary>Units being raised and on their way to an army.</summary>
    public List<Muster> Musters { get; } = new();

    /// <summary>The realm's generals (decision "Next 2"): those leading an army and those waiting for one.</summary>
    public List<General> Generals { get; } = new();
    public int NextGeneralId { get; set; } = 1;
    /// <summary>Military reforms: enacted (id: year), under way (id: years left), offered and not yet taken.</summary>
    public Dictionary<string, int> Reforms { get; } = new();
    public Dictionary<string, int> ReformsPending { get; } = new();
    /// <summary>State monopolies on goods (Treasury and money), and what they brought in last year.</summary>
    public List<Monopoly> Monopolies { get; } = new();
    public double LastMonopoly { get; set; }
    public HashSet<string> ReformsOffered { get; } = new();
    /// <summary>What history's causes look for: the last heavy defeat, defeats by each realm, peoples fought, and the last army to turn on the realm (years).</summary>
    public int LastHeavyDefeat { get; set; }
    public Dictionary<int, int> DefeatedBy { get; } = new();
    public Dictionary<string, int> FoughtCultures { get; } = new();
    public int LastMutinyYear { get; set; }
    /// <summary>Army traditions held (id: the last year it was used); victories won chiefly by each arm, and cities taken, toward earning one.</summary>
    public Dictionary<string, int> Traditions { get; } = new();
    public int[] ArmWins { get; } = new int[UnitRoles.Count];
    public int CitiesTaken { get; set; }
    /// <summary>Veterans discharged and waiting for land: how many, how hardened, led by a general, since when, and whether they will take silver.</summary>
    public double VeteransWaiting { get; set; }
    public double VeteransXp { get; set; }
    public bool VeteransLed { get; set; }
    public int VeteransSince { get; set; }
    public bool VeteransOfferSilver { get; set; }
    /// <summary>A capital you chose (lon, lat, town), or NaN for history's own seat.</summary>
    public double CapitalLon { get; set; } = double.NaN;
    public double CapitalLat { get; set; } = double.NaN;
    public string CapitalName { get; set; } = "";
    /// <summary>A capital being moved: the province, the town and its place, and years left (0: none).</summary>
    public int MovingCapitalTo { get; set; }
    public string MovingCapitalName { get; set; } = "";
    public double MovingCapitalLon { get; set; }
    public double MovingCapitalLat { get; set; }
    public int MovingCapitalYears { get; set; }
    /// <summary>Until this year cities fear the realm (it razed one): its sieges go faster.</summary>
    public int TerrorUntil { get; set; } = int.MinValue;
    /// <summary>The last year this realm was at war (0: not yet known; the first year sets it). Long peace helps growth.</summary>
    public int LastWarYear { get; set; }
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
            ["realm"] = RealmId, ["treasury"] = Treasury, ["tax"] = (int)Tax,
            ["loans"] = new GArray(Loans.Select(l => (Variant)l.ToArray()).ToArray()),
            ["finance"] = new GArray { BankersRefuseUntil, TempleCurseYears, UnpaidYears, (int)Collectors, ContractYears, CoinPurity, PriceLevel, LastMint },
            ["spending"] = new GArray(Spending.Select(x => (Variant)x).ToArray()),
            ["merchants"] = new GArray(Merchants.Select(x => (Variant)x).ToArray()), ["price_limit"] = PriceLimit,
            ["armies"] = armies, ["next_army"] = NextArmyId, ["manpower"] = Manpower, ["elephant_source"] = ElephantSource, ["civ"] = CivKey, ["manpower_mult"] = ManpowerMultiplier, ["army_share"] = ArmyShare, ["upkeep_share"] = UpkeepShare,
            ["last"] = new GArray { LastTax, LastTribute, LastAdmin, LastUpkeep, LastInterest, LastCustoms, LastCaptiveSales, LastCivil }, ["tax_reach"] = TaxReach, ["blockade"] = new GArray { Blockade, BlockadeMonths }, ["captives"] = Captives, ["remedies"] = RemedyDict(), ["techs"] = new GArray(Techs.Select(t => (Variant)t).ToArray()),
            ["branch_share"] = new GArray(BranchShare.Select(x => (Variant)x).ToArray()), ["branch_points"] = new GArray(BranchPoints.Select(x => (Variant)x).ToArray()),
            ["branch_researching"] = new GArray(BranchResearching.Select(x => (Variant)x).ToArray()), ["rival"] = RivalStrength, ["aggression"] = Aggression, ["trust"] = Trust, ["vassal_tribute"] = LastVassalTribute, ["start_people"] = StartPeople, ["goals"] = GoalsDict(),
            ["history"] = new GArray(History.Select(h => (Variant)h).ToArray()),
            ["merc_pay"] = new GArray { MercPaidThisYear, LastMercPay },
            ["generals"] = new GArray(Generals.Select(g => (Variant)g.ToDict()).ToArray()), ["next_general"] = NextGeneralId, ["last_war"] = LastWarYear, ["terror"] = TerrorUntil,
            ["reforms"] = new GArray(Reforms.Select(t => (Variant)new GArray { t.Key, t.Value }).ToArray()),
            ["reforms_pending"] = new GArray(ReformsPending.Select(t => (Variant)new GArray { t.Key, t.Value }).ToArray()),
            ["reforms_offered"] = new GArray(ReformsOffered.Select(x => (Variant)x).ToArray()),
            ["monopolies"] = new GArray(Monopolies.Select(m => (Variant)new GArray { m.Good, m.Farmed, m.Historical, m.Since, m.Staple }).ToArray()), ["last_monopoly"] = LastMonopoly,
            ["war_memory"] = new GArray { LastHeavyDefeat, LastMutinyYear,
                new GArray(DefeatedBy.Select(t => (Variant)new GArray { t.Key, t.Value }).ToArray()),
                new GArray(FoughtCultures.Select(t => (Variant)new GArray { t.Key, t.Value }).ToArray()) },
            ["musters"] = new GArray(Musters.Select(m => (Variant)m.ToArray()).ToArray()),
            ["traditions"] = new GArray(Traditions.Select(t => (Variant)new GArray { t.Key, t.Value }).ToArray()),
            ["arm_wins"] = new GArray(ArmWins.Select(x => (Variant)x).ToArray()), ["cities_taken"] = CitiesTaken,
            ["veterans"] = new GArray { VeteransWaiting, VeteransXp, VeteransLed, VeteransSince, VeteransOfferSilver },
            ["capital"] = new GArray { double.IsNaN(CapitalLon) ? 0 : CapitalLon, double.IsNaN(CapitalLat) ? 0 : CapitalLat, CapitalName, MovingCapitalTo, MovingCapitalName, MovingCapitalLon, MovingCapitalLat, MovingCapitalYears },
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
            Debt = d.TryGetValue("debt", out var debt) ? debt.AsDouble() : 0,   // a save from before lenders
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
            Trust = d.TryGetValue("trust", out var trust) ? trust.AsDouble() : 50,
            LastVassalTribute = d.TryGetValue("vassal_tribute", out var vt) ? vt.AsDouble() : 0,
            StartPeople = d.TryGetValue("start_people", out var sp) ? sp.AsDouble() : 0,
        };
        if (d.TryGetValue("techs", out var techs))
            foreach (Variant t in techs.AsGodotArray())
                r.Techs.Add(t.AsString());
        r.RivalStrength = d.TryGetValue("rival", out var rv) ? rv.AsDouble() : 0;
        if (d.TryGetValue("branch_share", out var bs))
        {
            var sh = bs.AsGodotArray(); var pts = d["branch_points"].AsGodotArray(); var res = d["branch_researching"].AsGodotArray();
            for (int b = 0; b < Math.Min(sh.Count, r.BranchShare.Length); b++)
            {
                r.BranchShare[b] = sh[b].AsDouble();
                r.BranchPoints[b] = pts[b].AsDouble();
                r.BranchResearching[b] = res[b].AsString();
            }
        }
        else if (d.TryGetValue("researching", out var rs) && TechCatalog.Instance[rs.AsString()] is { } old)
        {
            // A save from before branches: the one tech studied keeps its points in its branch.
            int b = TechCatalog.BranchIndex(old.Branch);
            r.BranchResearching[b] = old.Id;
            r.BranchPoints[b] = d.TryGetValue("research_points", out var rp) ? rp.AsDouble() : 0;
        }
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
                army.AddRaw(cat[UnitRoles.Generic[i]].Index, a[i].AsInt32());
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
        if (d.TryGetValue("loans", out var loans))
        {
            r.Loans.Clear();
            foreach (var v in loans.AsGodotArray())
                r.Loans.Add(Loan.FromArray(v.AsGodotArray()));
        }
        if (d.TryGetValue("finance", out var fin))
        {
            var f = fin.AsGodotArray();
            r.BankersRefuseUntil = f[0].AsInt32(); r.TempleCurseYears = f[1].AsInt32(); r.UnpaidYears = f[2].AsInt32();
            r.Collectors = (Collectors)f[3].AsInt32(); r.ContractYears = f[4].AsInt32();
            r.CoinPurity = f[5].AsDouble(); r.PriceLevel = f[6].AsDouble(); r.LastMint = f[7].AsDouble();
        }
        if (d.TryGetValue("merchants", out var mer))
            foreach (var v in mer.AsGodotArray())
                r.Merchants.Add(v.AsString());
        r.PriceLimit = d.TryGetValue("price_limit", out var pl) && pl.AsBool();
        if (d.TryGetValue("spending", out var spd))
        {
            var lines = spd.AsGodotArray();
            for (int i = 0; i < Math.Min(lines.Count, r.Spending.Length); i++)
                r.Spending[i] = lines[i].AsDouble();
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
        r.LastWarYear = d.TryGetValue("last_war", out var lw) ? lw.AsInt32() : 0;
        r.TerrorUntil = d.TryGetValue("terror", out var te) ? te.AsInt32() : int.MinValue;
        if (d.TryGetValue("reforms", out var rf))
            foreach (var v in rf.AsGodotArray())
                r.Reforms[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        if (d.TryGetValue("reforms_pending", out var rpend))
            foreach (var v in rpend.AsGodotArray())
                r.ReformsPending[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        if (d.TryGetValue("reforms_offered", out var ro))
            foreach (var v in ro.AsGodotArray())
                r.ReformsOffered.Add(v.AsString());
        if (d.TryGetValue("monopolies", out var mo))
            foreach (var v in mo.AsGodotArray())
            {
                var a = v.AsGodotArray();
                r.Monopolies.Add(new Monopoly { Good = a[0].AsString(), Farmed = a[1].AsBool(), Historical = a[2].AsBool(), Since = a[3].AsInt32(), Staple = a.Count > 4 && a[4].AsBool() });
            }
        r.LastMonopoly = d.TryGetValue("last_monopoly", out var lmo) ? lmo.AsDouble() : 0;
        if (d.TryGetValue("war_memory", out var wm))
        {
            var w = wm.AsGodotArray();
            r.LastHeavyDefeat = w[0].AsInt32();
            r.LastMutinyYear = w[1].AsInt32();
            foreach (var v in w[2].AsGodotArray())
                r.DefeatedBy[v.AsGodotArray()[0].AsInt32()] = v.AsGodotArray()[1].AsInt32();
            foreach (var v in w[3].AsGodotArray())
                r.FoughtCultures[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        }
        if (d.TryGetValue("musters", out var mus))
            foreach (var v in mus.AsGodotArray())
                r.Musters.Add(Muster.FromArray(v.AsGodotArray()));
        if (d.TryGetValue("traditions", out var trad))
            foreach (var v in trad.AsGodotArray())
                r.Traditions[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        if (d.TryGetValue("arm_wins", out var aw))
        {
            var awa = aw.AsGodotArray();
            for (int i = 0; i < Math.Min(awa.Count, r.ArmWins.Length); i++)
                r.ArmWins[i] = awa[i].AsInt32();
        }
        r.CitiesTaken = d.TryGetValue("cities_taken", out var ct) ? ct.AsInt32() : 0;
        if (d.TryGetValue("veterans", out var vets))
        {
            var va = vets.AsGodotArray();
            r.VeteransWaiting = va[0].AsDouble(); r.VeteransXp = va[1].AsDouble(); r.VeteransLed = va[2].AsBool();
            r.VeteransSince = va[3].AsInt32(); r.VeteransOfferSilver = va[4].AsBool();
        }
        if (d.TryGetValue("capital", out var capital))
        {
            var ca = capital.AsGodotArray();
            r.CapitalName = ca[2].AsString();
            bool chosen = r.CapitalName != "";   // no chosen capital: history's own seat (saved as 0, read back as NaN)
            r.CapitalLon = chosen ? ca[0].AsDouble() : double.NaN;
            r.CapitalLat = chosen ? ca[1].AsDouble() : double.NaN;
            r.MovingCapitalTo = ca[3].AsInt32(); r.MovingCapitalName = ca[4].AsString();
            r.MovingCapitalLon = ca[5].AsDouble(); r.MovingCapitalLat = ca[6].AsDouble(); r.MovingCapitalYears = ca[7].AsInt32();
        }
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
    /// <summary>
    /// How much the fog hides (your answer "Fog setting choices": separate
    /// for map and diplomacy, each off, light or full). Map: off shows all;
    /// light hides only armies beyond your sight; full also covers the land
    /// your realm has never seen. Diplomacy: off shows every realm's figures;
    /// light shows your neighbours' and partners' exactly and the rest roughly;
    /// full shows those you know roughly and the rest not at all.
    /// </summary>
    public FogLevel MapFog { get; set; } = FogLevel.Full;
    public FogLevel DiplomacyFog { get; set; } = FogLevel.Full;
    /// <summary>The land the player's realm has ever seen, one bit a map node (empty: everything, for old saves).</summary>
    public byte[] Explored { get; set; } = Array.Empty<byte>();
    public bool IsExplored(int node) => Explored.Length == 0 || (node >> 3) < Explored.Length && (Explored[node >> 3] & (1 << (node & 7))) != 0;
    public void SetExplored(int node) => Explored[node >> 3] |= (byte)(1 << (node & 7));
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
    /// <summary>Embargoes: (imposer, target) — no trade between them (decision "Trade treaties and embargoes").</summary>
    public HashSet<(int Imposer, int Target)> Embargoes { get; } = new();
    /// <summary>Pirate havens suppressed before history's date (decision "Pirates").</summary>
    public HashSet<string> SuppressedHavens { get; } = new();
    /// <summary>When each haven was last suppressed, or first appeared again (for its return and its growing strength).</summary>
    public Dictionary<string, int> HavenYear { get; } = new();
    /// <summary>Wonders standing in the world, by id (the Ledger topic "Buildings and works": wonders).</summary>
    public Dictionary<string, WonderSite> Wonders { get; } = new();
    /// <summary>Wonders under way, by any realm.</summary>
    public List<WonderWork> WonderWorks { get; } = new();
    /// <summary>Wonders fallen, never to be built again.</summary>
    public HashSet<string> FallenWonders { get; } = new();
    /// <summary>Roads on the map, built or being built (the Ledger topic "Buildings and works": roads).</summary>
    public List<Road> Roads { get; } = new();
    public int NextRoadId { get; set; } = 1;
    /// <summary>Months warships have patrolled each haven this year and before.</summary>
    public Dictionary<string, int> PatrolMonths { get; } = new();
    /// <summary>How ravaged by war each region is (region id: 0..1), fading over the years (Security).</summary>
    public Dictionary<int, double> Ravaged { get; } = new();
    /// <summary>Cities razed and not yet resettled (Sacking, Ruins).</summary>
    public List<Ruin> Ruins { get; } = new();
    /// <summary>War indemnities being paid (decision "War indemnities").</summary>
    public List<Indemnity> Indemnities { get; } = new();
    /// <summary>
    /// Old wrongs a realm remembers against another (Other realms: "Grudges":
    /// yes, fading over generations): (holder, target) -> how bitter, and the
    /// year it was last added to. Half forgotten every GrudgeHalfLife years.
    /// </summary>
    public Dictionary<(int Holder, int Target), (double Weight, int Year)> Grudges { get; } = new();
    public const int GrudgeHalfLife = 30;
    public const double GrudgeMax = 60;

    /// <summary>Adds a wrong to a realm's memory of another.</summary>
    public void AddGrudge(int holder, int target, double weight, int year)
    {
        if (holder <= 0 || target <= 0 || holder == target)
            return;
        double now = GrudgeNow(holder, target, year);
        Grudges[(holder, target)] = (Math.Min(GrudgeMax, now + weight), year);
    }

    /// <summary>How bitter a realm still is about another this year.</summary>
    public double GrudgeNow(int holder, int target, int year) =>
        Grudges.TryGetValue((holder, target), out var g) ? g.Weight * Math.Pow(0.5, Math.Max(0, year - g.Year) / (double)GrudgeHalfLife) : 0;

    /// <summary>Grievances over unpaid debts and indemnities: (holder, target) -> the last year they give a pretext.</summary>
    public Dictionary<(int Holder, int Target), int> Grievances { get; } = new();
    /// <summary>History's own buildings already begun by their realm (HistoryBuilding keys: "rome:aqueduct:Latium:-312").</summary>
    public HashSet<string> HistoryBuilt { get; } = new();

    /// <summary>Affronts (an insult, a just demand refused): (holder, target) -> the last year they give a pretext.</summary>
    public Dictionary<(int Holder, int Target), int> Affronts { get; } = new();
    public int NextCompanyId { get; set; } = 1;
    /// <summary>History's companies already brought into the game (each appears once).</summary>
    public HashSet<string> CompaniesSeen { get; } = new();
    /// <summary>History's campaigns checked at their end date (index in history_goals.json -> it happened as in history).</summary>
    public Dictionary<int, bool> HistoryChecks { get; } = new();
    /// <summary>The battles fought, newest last.</summary>
    public List<BattleReport> Battles { get; } = new();
    /// <summary>Months of the present year already marched (armies move month by month).</summary>
    public int MonthsMarched { get; set; }
    /// <summary>Every battle is kept (your answer "Save size": keep everything); thinning old years drops only their blow-by-blow.</summary>
    public void AddBattle(BattleReport r) => Battles.Add(r);

    /// <summary>
    /// The chronicle as you read it, the whole game long (your answer "Save
    /// size": keep everything): year, kind (a ChronicleKind), realm and text.
    /// </summary>
    public List<(int Year, int Kind, int Realm, string Text)> Chronicle { get; } = new();

    /// <summary>
    /// News still on the road to you (your answer "News that travels"): the
    /// month it arrives and the month it set out (both counted as year * 12 +
    /// month), its kind (a ChronicleKind), realm, text and spoken cue.
    /// </summary>
    public List<(int Due, int Sent, int Kind, int Realm, string Text, string Cue)> NewsOnTheRoad { get; } = new();

    /// <summary>
    /// Where enemy armies were last seen (your answer "Last-seen news": shown
    /// as last seen, with how old the news is): by realm and army, the node
    /// and the month (year * 12 + month).
    /// </summary>
    public Dictionary<(int Realm, int Army), (int Node, int Month)> LastSeen { get; } = new();

    /// <summary>What you pinned to the outliner (your answer "Pinning"): "province:12", "realm:5", "character:44".</summary>
    public List<string> Pins { get; } = new();

    /// <summary>Your notes on the map (your answer "Your notes"): the province, the year written, the words.</summary>
    public List<(int Province, int Year, string Text)> Notes { get; } = new();

    /// <summary>With "Thin out old years" on, years older than this are kept in brief.</summary>
    public const int ThinAfterYears = 100;

    /// <summary>
    /// Thins years older than ThinAfterYears (your answer "Keeping history
    /// in saves": a setting): the chronicle keeps only its great events
    /// (wars, peace, conquests, revolts, disasters, successions), battle
    /// reports lose their phases, provinces keep one entry a decade, and
    /// each realm's graphs keep every 25th year.
    /// </summary>
    public void ThinOldYears(int year, IReadOnlyCollection<int> greatKinds)
    {
        int before = year - ThinAfterYears;
        Chronicle.RemoveAll(e => e.Year < before && !greatKinds.Contains(e.Kind));
        foreach (var b in Battles)
            if (b.Year < before)
                b.Phases.Clear();
        foreach (var p in Provinces.Values)
        {
            var seen = new HashSet<int>();
            p.History.RemoveAll(h => h.Year < before && !seen.Add(h.Year / 10));
        }
        foreach (var r in Realms.Values)
            r.History.RemoveAll(h => (int)h[0] < before && (int)h[0] % 25 != 0);
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
            ["realms"] = realms, ["years_per_turn"] = YearsPerTurn, ["fog"] = new GArray { (int)MapFog, (int)DiplomacyFog, Convert.ToBase64String(Explored) }, ["wars"] = Wars.ToArray(), ["peace_offers"] = offers,
            ["treaties"] = Treaties.ToArray(), ["lost"] = LostArray(),
            ["history_checks"] = HistoryChecksDict(), ["months_marched"] = MonthsMarched, ["battles"] = new GArray(Battles.Select(b => (Variant)b.ToDict()).ToArray()),
            ["chronicle"] = new GArray(Chronicle.Select(e => (Variant)new GArray { e.Year, e.Kind, e.Realm, e.Text }).ToArray()),
            ["notes"] = new GArray(Notes.Select(n => (Variant)new GArray { n.Province, n.Year, n.Text }).ToArray()),
            ["pins"] = new GArray(Pins.Select(x => (Variant)x).ToArray()),
            ["last_seen"] = new GArray(LastSeen.Select(e => (Variant)new GArray { e.Key.Realm, e.Key.Army, e.Value.Node, e.Value.Month }).ToArray()),
            ["news_on_the_road"] = new GArray(NewsOnTheRoad.Select(e => (Variant)new GArray { e.Due, e.Sent, e.Kind, e.Realm, e.Text, e.Cue }).ToArray()),
            ["ruler_log"] = new GArray(RulerLog.Select(r => (Variant)new GArray { r.Year, r.Name }).ToArray()), ["offers"] = new GArray(Offers.Select(o => (Variant)o.ToDict()).ToArray()),
            ["companies"] = new GArray(Companies.Select(c => (Variant)c.ToDict()).ToArray()), ["next_company"] = NextCompanyId,
            ["companies_seen"] = new GArray(CompaniesSeen.Select(c => (Variant)c).ToArray()),
            ["indemnities"] = new GArray(Indemnities.Select(x => (Variant)x.ToArray()).ToArray()),
            ["embargoes"] = new GArray(Embargoes.Select(e => (Variant)new GArray { e.Imposer, e.Target }).ToArray()),
            ["suppressed_havens"] = new GArray(SuppressedHavens.Select(x => (Variant)x).ToArray()),
            ["wonders"] = new GArray(Wonders.Select(w => (Variant)new GArray { w.Key, w.Value.ToArray() }).ToArray()),
            ["wonder_works"] = new GArray(WonderWorks.Select(w => (Variant)w.ToArray()).ToArray()),
            ["roads"] = new GArray(Roads.Select(r => (Variant)r.ToDict()).ToArray()), ["next_road"] = NextRoadId,
            ["fallen_wonders"] = new GArray(FallenWonders.Select(x => (Variant)x).ToArray()),
            ["haven_years"] = new GArray(HavenYear.Select(p => (Variant)new GArray { p.Key, p.Value }).ToArray()),
            ["patrols"] = new GArray(PatrolMonths.Select(p => (Variant)new GArray { p.Key, p.Value }).ToArray()),
            ["ravaged"] = new GArray(Ravaged.Select(p => (Variant)new GArray { p.Key, p.Value }).ToArray()),
            ["ruins"] = new GArray(Ruins.Select(r => (Variant)r.ToArray()).ToArray()),
            ["grievances"] = new GArray(Grievances.Select(g => (Variant)new GArray { g.Key.Holder, g.Key.Target, g.Value }).ToArray()),
            ["history_built"] = new GArray(HistoryBuilt.OrderBy(k => k, System.StringComparer.Ordinal).Select(k => (Variant)k).ToArray()),
            ["affronts"] = new GArray(Affronts.Select(g => (Variant)new GArray { g.Key.Holder, g.Key.Target, g.Value }).ToArray()),
            ["grudges"] = new GArray(Grudges.Select(g => (Variant)new GArray { g.Key.Holder, g.Key.Target, g.Value.Weight, g.Value.Year }).ToArray()),
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
        if (d.TryGetValue("embargoes", out var emb))
            foreach (var v in emb.AsGodotArray())
                g.Embargoes.Add((v.AsGodotArray()[0].AsInt32(), v.AsGodotArray()[1].AsInt32()));
        if (d.TryGetValue("suppressed_havens", out var sh))
            foreach (var v in sh.AsGodotArray())
                g.SuppressedHavens.Add(v.AsString());
        if (d.TryGetValue("wonders", out var wds))
            foreach (var v in wds.AsGodotArray())
                g.Wonders[v.AsGodotArray()[0].AsString()] = WonderSite.FromArray(v.AsGodotArray()[1].AsGodotArray());
        if (d.TryGetValue("wonder_works", out var wws))
            foreach (var v in wws.AsGodotArray())
                g.WonderWorks.Add(WonderWork.FromArray(v.AsGodotArray()));
        if (d.TryGetValue("roads", out var rds))
            foreach (var v in rds.AsGodotArray())
                g.Roads.Add(Road.FromDict(v.AsGodotDictionary()));
        g.NextRoadId = d.TryGetValue("next_road", out var nrd) ? nrd.AsInt32() : g.Roads.Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;
        if (d.TryGetValue("fallen_wonders", out var fws))
            foreach (var v in fws.AsGodotArray())
                g.FallenWonders.Add(v.AsString());
        if (d.TryGetValue("haven_years", out var hy))
            foreach (var v in hy.AsGodotArray())
                g.HavenYear[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        if (d.TryGetValue("ruins", out var ruins))
            foreach (var v in ruins.AsGodotArray())
                g.Ruins.Add(Ruin.FromArray(v.AsGodotArray()));
        if (d.TryGetValue("ravaged", out var rav))
            foreach (var v in rav.AsGodotArray())
                g.Ravaged[v.AsGodotArray()[0].AsInt32()] = v.AsGodotArray()[1].AsDouble();
        if (d.TryGetValue("patrols", out var pat))
            foreach (var v in pat.AsGodotArray())
                g.PatrolMonths[v.AsGodotArray()[0].AsString()] = v.AsGodotArray()[1].AsInt32();
        if (d.TryGetValue("indemnities", out var ind))
            foreach (var v in ind.AsGodotArray())
                g.Indemnities.Add(Indemnity.FromArray(v.AsGodotArray()));
        if (d.TryGetValue("grievances", out var grv))
            foreach (var v in grv.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.Grievances[(a[0].AsInt32(), a[1].AsInt32())] = a[2].AsInt32();
            }
        if (d.TryGetValue("history_built", out var hb))
            foreach (var v in hb.AsGodotArray())
                g.HistoryBuilt.Add(v.AsString());
        if (d.TryGetValue("affronts", out var aff))
            foreach (var v in aff.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.Affronts[(a[0].AsInt32(), a[1].AsInt32())] = a[2].AsInt32();
            }
        if (d.TryGetValue("grudges", out var gru))
            foreach (Variant v in gru.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.Grudges[(a[0].AsInt32(), a[1].AsInt32())] = (a[2].AsDouble(), a[3].AsInt32());
            }
        if (d.TryGetValue("companies_seen", out var csn))
            foreach (Variant v in csn.AsGodotArray())
                g.CompaniesSeen.Add(v.AsString());
        if (d.TryGetValue("battles", out var bt))
            foreach (Variant v in bt.AsGodotArray())
                g.Battles.Add(BattleReport.FromDict(v.AsGodotDictionary()));
        if (d.TryGetValue("chronicle", out var chr))
            foreach (Variant v in chr.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.Chronicle.Add((a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsString()));
            }
        if (d.TryGetValue("notes", out var notes))
            foreach (Variant v in notes.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.Notes.Add((a[0].AsInt32(), a[1].AsInt32(), a[2].AsString()));
            }
        if (d.TryGetValue("pins", out var pins))
            foreach (Variant v in pins.AsGodotArray())
                g.Pins.Add(v.AsString());
        if (d.TryGetValue("last_seen", out var seen))
            foreach (Variant v in seen.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.LastSeen[(a[0].AsInt32(), a[1].AsInt32())] = (a[2].AsInt32(), a[3].AsInt32());
            }
        if (d.TryGetValue("news_on_the_road", out var road))
            foreach (Variant v in road.AsGodotArray())
            {
                var a = v.AsGodotArray();
                g.NewsOnTheRoad.Add((a[0].AsInt32(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsInt32(), a[4].AsString(), a[5].AsString()));
            }
        if (d.TryGetValue("sieges", out var sg))
            foreach (Variant v in sg.AsGodotArray())
                g.Sieges.Add(Siege.FromDict(v.AsGodotDictionary()));
        if (d.TryGetValue("realms", out var realms))
            foreach (Variant v in realms.AsGodotArray())
            {
                var r = RealmState.FromDict(v.AsGodotDictionary());
                g.Realms[r.RealmId] = r;
            }
        if (d.TryGetValue("fog", out var fog))
        {
            var f = fog.AsGodotArray();
            g.MapFog = (FogLevel)f[0].AsInt32();
            g.DiplomacyFog = (FogLevel)f[1].AsInt32();
            g.Explored = Convert.FromBase64String(f[2].AsString());
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
