using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>The ground a battle is fought on, each 0..1.</summary>
public readonly record struct Ground(double Hills, double Forest, double Marsh, double Desert, double River)
{
    public static readonly Ground Open = new(0, 0, 0, 0, 0);

    /// <summary>The ground at a node, from the land layer (ruggedness, woodland, marsh, desert, rivers).</summary>
    public static Ground At(Facsimilia.World.LandLayer? land, int node)
    {
        if (land == null || node < 0)
            return Open;
        double V(string f) => land.Has(f) ? land.Value(f, node) : 0;
        return new Ground(
            Math.Clamp(V("ruggedness") / 400.0, 0, 1),
            Math.Clamp(V("woodland"), 0, 1),
            Math.Clamp(V("marsh") * 2, 0, 1),
            Math.Clamp(V("desert"), 0, 1),
            Math.Clamp(V("river"), 0, 1));
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (Hills > 0.5) parts.Add("mountains");
        else if (Hills > 0.2) parts.Add("hills");
        if (Forest > 0.4) parts.Add("forest");
        else if (Forest > 0.15) parts.Add("woods");
        if (Marsh > 0.3) parts.Add("marsh");
        if (Desert > 0.4) parts.Add("desert");
        if (River > 0.3) parts.Add("a river");
        return parts.Count == 0 ? "open plain" : string.Join(", ", parts);
    }
}

/// <summary>One side of a battle: the armies it brings (with the share of each committed) and its general.</summary>
public sealed class BattleSide
{
    public int Realm { get; init; }
    public string RealmName { get; init; } = "";
    public List<(Army Army, double Share)> Armies { get; init; } = new();
    public General? General { get; init; }
    /// <summary>Walls and levies fighting beside the armies (a garrison), as Might.</summary>
    public double Extra { get; init; }
    /// <summary>What the realm's techs add to its generals' tactics (not used yet: 0).</summary>
    public string Label => Armies.Count > 0 ? string.Join(" and ", Armies.Select(a => a.Army.Name).Distinct()) : "levies and garrison";
}

/// <summary>What happened in a battle, for the chronicle and the battle report screen (decision "Next 6").</summary>
public sealed class BattleReport
{
    public int Year { get; set; }
    public string Place { get; set; } = "";
    public string Ground { get; set; } = "";
    public int AttackerRealm { get; set; }
    public int DefenderRealm { get; set; }
    public string Attacker { get; set; } = "";
    public string Defender { get; set; } = "";
    public string AttackerArmy { get; set; } = "";
    public string DefenderArmy { get; set; } = "";
    public string AttackerGeneral { get; set; } = "";
    public string DefenderGeneral { get; set; } = "";
    public int AttackerMen { get; set; }
    public int DefenderMen { get; set; }
    public int AttackerLost { get; set; }
    public int DefenderLost { get; set; }
    public double AttackerStrength { get; set; }
    public double DefenderStrength { get; set; }
    public double Chance { get; set; }
    public bool AttackerWon { get; set; }
    public List<string> Phases { get; } = new();

    public string Title => $"Battle of {Place}, {(Year < 0 ? $"{-Year} BC" : $"AD {Year}")}";
    public string Winner => AttackerWon ? Attacker : Defender;

    public GDictionary ToDict() => new()
    {
        ["year"] = Year, ["place"] = Place, ["ground"] = Ground, ["ar"] = AttackerRealm, ["dr"] = DefenderRealm,
        ["a"] = Attacker, ["d"] = Defender, ["aa"] = AttackerArmy, ["da"] = DefenderArmy,
        ["ag"] = AttackerGeneral, ["dg"] = DefenderGeneral, ["am"] = AttackerMen, ["dm"] = DefenderMen,
        ["al"] = AttackerLost, ["dl"] = DefenderLost, ["as"] = AttackerStrength, ["ds"] = DefenderStrength,
        ["chance"] = Chance, ["won"] = AttackerWon, ["phases"] = new GArray(Phases.Select(p => (Variant)p).ToArray()),
    };

    public static BattleReport FromDict(GDictionary d)
    {
        var r = new BattleReport
        {
            Year = d["year"].AsInt32(), Place = d["place"].AsString(), Ground = d["ground"].AsString(),
            AttackerRealm = d["ar"].AsInt32(), DefenderRealm = d["dr"].AsInt32(),
            Attacker = d["a"].AsString(), Defender = d["d"].AsString(), AttackerArmy = d["aa"].AsString(), DefenderArmy = d["da"].AsString(),
            AttackerGeneral = d["ag"].AsString(), DefenderGeneral = d["dg"].AsString(),
            AttackerMen = d["am"].AsInt32(), DefenderMen = d["dm"].AsInt32(), AttackerLost = d["al"].AsInt32(), DefenderLost = d["dl"].AsInt32(),
            AttackerStrength = d["as"].AsDouble(), DefenderStrength = d["ds"].AsDouble(), Chance = d["chance"].AsDouble(), AttackerWon = d["won"].AsBool(),
        };
        foreach (var p in d["phases"].AsGodotArray())
            r.Phases.Add(p.AsString());
        return r;
    }
}

/// <summary>
/// A field battle (decision "Next 1", first part): each side's strength by
/// arm, changed by the ground, by what the enemy brings (elephants frighten
/// horses, light troops harry elephants), by its general's tactics and perks,
/// by veterans and by weariness; then the day is fought in three phases
/// (skirmish, the horse on the wings, the main lines) and the beaten side
/// pursued. The tactical battle map will let the player fight the phases.
/// </summary>
public static class Battle
{
    /// <summary>Each point of tactics above the enemy's general: this much stronger.</summary>
    public const double TacticsStep = 0.07;
    /// <summary>A fully veteran army fights this much better.</summary>
    public const double VeteranBonus = 0.3;
    public const double LoserLosses = 0.15, WinnerLosses = 0.05;

    static UnitCatalog Cat => UnitCatalog.Instance;

    /// <summary>The Might each side brings by role (land), after weariness and experience.</summary>
    public static double[] RoleMight(BattleSide side)
    {
        var m = new double[UnitRoles.Count];
        foreach (var (a, share) in side.Armies)
            foreach (var r in a.Regiments)
            {
                var u = Cat[r.Type];
                if (u.Domain == Domain.Land)
                    m[u.Role] += share * r.Men / (double)u.Men * u.Might * (1 + a.RoleBoost[u.Role])
                        * (1 - 0.5 * a.Fatigue) * (1 + VeteranBonus * r.Xp);
            }
        return m;
    }

    /// <summary>How the ground suits each arm (1 = as on an open plain).</summary>
    public static double GroundFactor(int role, Ground g, General? gen)
    {
        double f = role switch
        {
            UnitRoles.HeavyInfantry => 1 - 0.2 * g.Hills - 0.25 * g.Forest - 0.2 * g.Marsh - 0.1 * g.Desert,
            UnitRoles.LightInfantry => 1 + 0.3 * g.Hills + 0.3 * g.Forest + 0.1 * g.Marsh,
            UnitRoles.Missile => 1 - 0.25 * g.Forest + 0.1 * g.Hills,
            UnitRoles.Cavalry => 1 - 0.4 * g.Hills - 0.4 * g.Forest - 0.35 * g.Marsh,
            UnitRoles.HorseArchers => 1 - 0.4 * g.Hills - 0.45 * g.Forest - 0.35 * g.Marsh + 0.2 * g.Desert,
            UnitRoles.Elephants => 1 - 0.5 * g.Hills - 0.4 * g.Forest - 0.5 * g.Marsh,
            _ => 1,
        };
        if (gen != null)
            f += gen.Effect("hills") * g.Hills + gen.Effect("forest") * g.Forest + gen.Effect("marsh") * g.Marsh
                + gen.Effect("desert") * g.Desert + gen.Effect("river") * g.River;
        return Math.Max(0.2, f);
    }

    static double ArmBonus(int role, General? gen) => gen == null ? 0 : role switch
    {
        UnitRoles.HeavyInfantry or UnitRoles.LightInfantry => gen.Effect("infantry"),
        UnitRoles.Missile => gen.Effect("missile"),
        UnitRoles.Cavalry or UnitRoles.HorseArchers => gen.Effect("cavalry"),
        UnitRoles.Elephants => gen.Effect("elephants"),
        _ => 0,
    };

    /// <summary>
    /// A side's strength on the day against a given enemy: ground, arms,
    /// the enemy's mix, and the general's tactics against the other's.
    /// </summary>
    public static double Strength(double[] mine, double[] theirs, Ground g, General? gen, General? enemyGen, double extra, bool crossing)
    {
        double total = theirs.Sum();
        double enemyLight = total > 0 ? (theirs[UnitRoles.LightInfantry] + theirs[UnitRoles.Missile]) / total : 0;
        double enemyElephants = total > 0 ? theirs[UnitRoles.Elephants] / total : 0;
        double s = extra;
        for (int r = 0; r < UnitRoles.Count; r++)
        {
            if (mine[r] <= 0 || r == UnitRoles.Warships)
                continue;
            double f = GroundFactor(r, g, gen) * (1 + ArmBonus(r, gen));
            if (r == UnitRoles.Elephants)
                f *= 1 - 0.6 * enemyLight;   // javelins and slings drive elephants mad, as at Zama
            if (r is UnitRoles.Cavalry or UnitRoles.HorseArchers)
                f *= 1 - 1.5 * enemyElephants;   // horses won't face elephants, as at Ipsus
            s += mine[r] * Math.Max(0.2, f);
        }
        int tactics = gen?.Skill(Skills.Tactics) ?? 3, enemyTactics = enemyGen?.Skill(Skills.Tactics) ?? 3;
        s *= Math.Max(0.4, 1 + TacticsStep * (tactics - enemyTactics));
        if (crossing)
            s *= 1 - 0.25 * g.River * (1 - (gen?.Effect("river") ?? 0));
        return s;
    }

    /// <summary>Chance the attacker wins, rising with the ratio of strength (a power of 1.5, as before).</summary>
    public static double WinChance(double attack, double defend) => Conquest.WinChance(attack, defend);

    /// <summary>
    /// Fights a battle. Losses fall on the committed share of each army;
    /// the beaten side loses more, most of all to a strong cavalry
    /// pursuit, and a good leader saves men. Returns the report.
    /// </summary>
    public static BattleReport Fight(BattleSide att, BattleSide def, Ground g, string place, int year, Random rng)
    {
        var am = RoleMight(att);
        var dm = RoleMight(def);
        double a = Strength(am, dm, g, att.General, def.General, 0, true);
        double d = Strength(dm, am, g, def.General, att.General, def.Extra, false);
        double chance = WinChance(a, d);
        bool won = rng.NextDouble() < chance;
        var report = new BattleReport
        {
            Year = year, Place = place, Ground = g.Describe(),
            AttackerRealm = att.Realm, DefenderRealm = def.Realm, Attacker = att.RealmName, Defender = def.RealmName,
            AttackerArmy = att.Label, DefenderArmy = def.Label,
            AttackerGeneral = att.General?.Name ?? "", DefenderGeneral = def.General?.Name ?? "",
            AttackerMen = Men(att), DefenderMen = Men(def),
            AttackerStrength = a, DefenderStrength = d, Chance = chance, AttackerWon = won,
        };
        Phases(report, att, def, am, dm, g, won);

        var winner = won ? att : def;
        var loser = won ? def : att;
        var wm = won ? am : dm;
        double horse = wm[UnitRoles.Cavalry] + wm[UnitRoles.HorseArchers];
        double pursuit = 1 + Math.Min(0.6, horse / Math.Max(wm.Sum(), 1e-9)) + (winner.General?.Effect("pursuit") ?? 0);
        double loserShare = LoserLosses * pursuit * LossFactor(loser.General) * (0.8 + 0.4 * rng.NextDouble());
        double winnerShare = WinnerLosses * LossFactor(winner.General) * (0.8 + 0.4 * rng.NextDouble());
        int wLost = TakeLosses(winner, Math.Clamp(winnerShare, 0.01, 0.5), rng, null, Military.WinnerDead);
        int lLost = TakeLosses(loser, Math.Clamp(loserShare, 0.03, 0.8), rng, null, Military.LoserDead);
        report.AttackerLost = won ? wLost : lLost;
        report.DefenderLost = won ? lLost : wLost;

        Aftermath(winner, loser);
        return report;
    }

    /// <summary>After any battle: experience, weariness, the generals' record.</summary>
    static void Aftermath(BattleSide winner, BattleSide loser)
    {
        foreach (var (army, share) in winner.Armies)
        {
            army.Harden(share * 0.15 * (1 + (winner.General?.Effect("veterans") ?? 0)));
            if (army.CompanyId != 0)
                army.VictoriesUnpaid++;
        }
        foreach (var (army, share) in loser.Armies)
            army.Harden(share * 0.05);
        foreach (var side in new[] { winner, loser })
            foreach (var (army, _) in side.Armies)
                army.Fatigue = Math.Min(1, army.Fatigue + Conquest.FatiguePerFight * (1 - 0.05 * ((side.General?.Skill(Skills.Logistics) ?? 5) - 5)));
        if (winner.General != null)
        {
            winner.General.Battles++;
            winner.General.Victories++;
        }
        if (loser.General != null)
            loser.General.Battles++;
    }

    /// <summary>
    /// A battle fought on the battle map is over: each side loses what it lost
    /// there (less under a good leader), and the day is told from its log.
    /// </summary>
    public static BattleReport FromTactics(TacticalBattle t, BattleSide att, BattleSide def, Ground g, string place, int year, Random rng)
    {
        bool won = t.Winner == 0;
        var (aShare, dShare) = t.Losses();
        var report = new BattleReport
        {
            Year = year, Place = place, Ground = t.Naval ? "open sea" : g.Describe(),
            AttackerRealm = att.Realm, DefenderRealm = def.Realm, Attacker = att.RealmName, Defender = def.RealmName,
            AttackerArmy = att.Label, DefenderArmy = def.Label,
            AttackerGeneral = att.General?.Name ?? "", DefenderGeneral = def.General?.Name ?? "",
            AttackerMen = Men(att), DefenderMen = Men(def),
            AttackerStrength = t.Blocks.Where(b => b.Side == 0).Sum(b => b.Start), DefenderStrength = t.Blocks.Where(b => b.Side == 1).Sum(b => b.Start),
            Chance = won ? 1 : 0, AttackerWon = won,
        };
        report.Phases.Add($"Fought on the battle map over {Math.Min(t.Round, TacticalBattle.MaxRounds)} rounds.");
        report.Phases.AddRange(t.Log.TakeLast(8));
        var domain = t.Naval ? Domain.Naval : Domain.Land;
        report.AttackerLost = TakeLosses(att, Math.Clamp(aShare * LossFactor(att.General), 0.01, 0.9), rng, domain, won ? Military.WinnerDead : Military.LoserDead);
        report.DefenderLost = TakeLosses(def, Math.Clamp(dShare * LossFactor(def.General), 0.01, 0.9), rng, domain, won ? Military.LoserDead : Military.WinnerDead);
        Aftermath(won ? att : def, won ? def : att);
        return report;
    }

    /// <summary>
    /// A sea fight decided by the admirals (decision "Next 5"): fleets' Might,
    /// seamanship and weariness. The beaten fleet loses about a fifth of its ships.
    /// </summary>
    public static BattleReport FightNaval(BattleSide att, BattleSide def, string place, int year, Random rng)
    {
        double Might(BattleSide s) => s.Armies.Sum(x => Military.Might(x.Army, Domain.Naval) * x.Share)
            * Math.Max(0.4, 1 + TacticsStep * ((s.General?.Skill(Skills.Seamanship) ?? 3) - 5));
        double a = Might(att), d = Might(def);
        double chance = WinChance(a, d);
        bool won = rng.NextDouble() < chance;
        var report = new BattleReport
        {
            Year = year, Place = place, Ground = "open sea",
            AttackerRealm = att.Realm, DefenderRealm = def.Realm, Attacker = att.RealmName, Defender = def.RealmName,
            AttackerArmy = att.Label, DefenderArmy = def.Label,
            AttackerGeneral = att.General?.Name ?? "", DefenderGeneral = def.General?.Name ?? "",
            AttackerMen = Men(att), DefenderMen = Men(def), AttackerStrength = a, DefenderStrength = d, Chance = chance, AttackerWon = won,
        };
        string A = att.RealmName, D = def.RealmName;
        report.Phases.Add($"The fleets of {A} and {D} meet at sea.");
        report.Phases.Add(won ? $"{A}'s rams and boarding parties break {D}'s line; the survivors flee to port."
            : $"{D}'s ships hold their line and drive {A}'s fleet off.");
        var (winner, loser) = won ? (att, def) : (def, att);
        int wl = TakeLosses(winner, 0.06 * LossFactor(winner.General), rng, Domain.Naval, Military.WinnerDead);
        int ll = TakeLosses(loser, 0.2 * LossFactor(loser.General), rng, Domain.Naval, Military.SeaDead);
        report.AttackerLost = won ? wl : ll;
        report.DefenderLost = won ? ll : wl;
        Aftermath(winner, loser);
        return report;
    }

    /// <summary>A good leader loses fewer men: 5% fewer per point of leadership above 5.</summary>
    static double LossFactor(General? g) => g == null ? 1.1
        : Math.Max(0.4, 1 - 0.05 * (g.Skill(Skills.Leadership) - 5) + g.Effect("losses"));

    static int Men(BattleSide s) => (int)s.Armies.Sum(x => Military.Soldiers(x.Army) * x.Share);

    static int TakeLosses(BattleSide s, double share, Random rng, Domain? only, double deadShare) =>
        (int)s.Armies.Sum(x => Military.TakeLosses(x.Army, share * x.Share, rng, only, deadShare));

    /// <summary>The day in three phases, told as the ancient historians told it.</summary>
    static void Phases(BattleReport r, BattleSide att, BattleSide def, double[] am, double[] dm, Ground g, bool won)
    {
        string A = att.RealmName, D = def.RealmName;
        static string Of(string name) => name.EndsWith('s') ? name + "'" : name + "'s";
        r.Phases.Add($"The armies meet on {g.Describe()}." +
            (g.River > 0.3 ? $" {A} must cross the river under the enemy's eyes." : "") +
            (att.General != null ? $" {att.General.Name} leads {A}" : $" {A} has no general of note") +
            (def.General != null ? $"; {def.General.Name} leads {D}." : $"; {D} has no general of note."));
        double aSkirm = am[UnitRoles.LightInfantry] + am[UnitRoles.Missile], dSkirm = dm[UnitRoles.LightInfantry] + dm[UnitRoles.Missile];
        if (aSkirm + dSkirm > 0)
            r.Phases.Add(aSkirm > dSkirm * 1.2 ? $"The skirmishers of {A} drive in the enemy's light troops."
                : dSkirm > aSkirm * 1.2 ? $"{Of(D)} slingers and javelinmen harry {Of(A)} advance."
                : "The light troops skirmish between the lines without result.");
        if (am[UnitRoles.Elephants] + dm[UnitRoles.Elephants] > 0)
        {
            bool aEl = am[UnitRoles.Elephants] >= dm[UnitRoles.Elephants];
            string side = aEl ? A : D;
            double enemyLight = aEl ? dSkirm : aSkirm;
            r.Phases.Add(enemyLight > (aEl ? am : dm)[UnitRoles.Elephants] * 2
                ? $"{Of(side)} elephants are met with javelins and turn on their own lines."
                : $"{Of(side)} elephants crash into the enemy, and the horses will not face them.");
        }
        double aHorse = am[UnitRoles.Cavalry] + am[UnitRoles.HorseArchers], dHorse = dm[UnitRoles.Cavalry] + dm[UnitRoles.HorseArchers];
        if (aHorse + dHorse > 0)
            r.Phases.Add(aHorse > dHorse * 1.3 ? $"On the wings {Of(A)} horse scatters the enemy cavalry."
                : dHorse > aHorse * 1.3 ? $"{Of(D)} cavalry wins the wings and threatens the flanks."
                : "The cavalry fights long on the wings without a decision.");
        r.Phases.Add(won
            ? $"The main lines close; {Of(D)} line breaks and {A} holds the field."
            : $"The main lines close; {A} is thrown back and {D} holds the field.");
    }
}
