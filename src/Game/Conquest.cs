using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// One place an attacker means to take: a whole enemy province, or a
/// stretch of unorganized or unclaimed land (MECHANICS.md, "Sieges").
/// People are the ones living there: their militia resists.
/// </summary>
public sealed class ConquestTarget
{
    public int Attacker { get; init; }
    public int Owner { get; init; }             // 0 = unclaimed
    public int ProvinceId { get; init; }        // 0 = unorganized or unclaimed land
    public string Name { get; init; } = "";
    public double People { get; set; }
    public bool BySea { get; set; }             // only reachable across water
    public int Node { get; set; } = -1;         // where the besiegers camp
    public int ArmyId { get; set; }             // the army sent
    public List<int> Cells { get; } = new();    // ownership cells to flip, for land without a province

    // Estimates for the plan panel.
    public double AttackerMight { get; set; }
    public double DefenderMight { get; set; }
    public double Chance { get; set; }          // of beating the defenders in the field
    public double Years { get; set; }           // expected years of siege
    public string? Problem { get; set; }        // why it can't be attacked (no war, no fleet, no army)
}

/// <summary>
/// A siege under way (decision "Playable 12": sieges over years). Each year
/// the besieging army wears the place down, faster the stronger it is
/// against the garrison (the local militia and any defending army standing
/// there) and the stronger its realm against the defender's; the defender
/// may march a relief army against it.
/// </summary>
public sealed class Siege
{
    public int Attacker { get; init; }
    public int Owner { get; init; }
    public int ProvinceId { get; init; }
    public int ArmyId { get; set; }
    public string Name { get; init; } = "";
    public double People { get; set; }
    public int Node { get; init; }
    public int StartYear { get; init; }
    /// <summary>0 just begun .. 1 the place falls.</summary>
    public double Progress { get; set; }
    public List<int> Cells { get; } = new();

    public GDictionary ToDict() => new()
    {
        ["attacker"] = Attacker, ["owner"] = Owner, ["province"] = ProvinceId, ["army"] = ArmyId, ["name"] = Name,
        ["people"] = People, ["node"] = Node, ["start"] = StartYear, ["progress"] = Progress,
        ["cells"] = Cells.ToArray(),
    };

    public static Siege FromDict(GDictionary d)
    {
        var s = new Siege
        {
            Attacker = d["attacker"].AsInt32(), Owner = d["owner"].AsInt32(), ProvinceId = d["province"].AsInt32(),
            ArmyId = d["army"].AsInt32(), Name = d["name"].AsString(), People = d["people"].AsDouble(),
            Node = d["node"].AsInt32(), StartYear = d["start"].AsInt32(), Progress = d["progress"].AsDouble(),
        };
        s.Cells.AddRange(d["cells"].AsInt32Array());
        return s;
    }
}

public static class Conquest
{
    /// <summary>Militia: about 2% of people take up arms, at half a soldier's worth: 0.8 Might per 100,000 people.</summary>
    public const double MilitiaPerPerson = 0.8 / 100000;
    /// <summary>Share of a defending realm's free armies it sends to relieve a siege.</summary>
    public const double DefenderCommit = 0.6;
    /// <summary>A realm's own organized province has walls and a garrison: its militia counts this much more.</summary>
    public const double ProvinceGarrison = 1.5;
    /// <summary>Chance each year that a defender with an army marches to relieve a siege.</summary>
    public const double ReliefChance = 0.5;
    /// <summary>Crossing the sea: the fleet must be at least this share of the defender's.</summary>
    public const double NavalNeeded = 0.8;
    public const double LoserLosses = 0.15, WinnerLosses = 0.06;
    public const double FatiguePerFight = 0.12;
    /// <summary>A besieging army loses this share a year to disease and desertion, and tires.</summary>
    public const double SiegeAttrition = 0.02, SiegeFatigue = 0.1;
    /// <summary>A siege between equals takes about 1 / SiegeBase years (about three).</summary>
    public const double SiegeBase = 0.35;
    /// <summary>Reach (decision "Playable 13": reach and supply): km an army advances in a year over land from where it stands.</summary>
    public const double ReachKm = 400;
    /// <summary>With a fleet, travel by sea costs this share of travel over land.</summary>
    public const double SeaCostShare = 0.35;

    /// <summary>Walls and a standing garrison: an organized province holds out with at least this much Might.</summary>
    public const double ProvinceWalls = 4.0;

    public static double Militia(double people, bool province) =>
        people * MilitiaPerPerson * (province ? ProvinceGarrison : 1) + (province ? ProvinceWalls : 0);

    /// <summary>Chance the attacker wins a battle: rises with the ratio of strength, softened (a power of 1.5).</summary>
    public static double WinChance(double attack, double defend)
    {
        if (attack <= 0)
            return 0;
        if (defend <= 0)
            return 1;
        double a = Math.Pow(attack, 1.5), d = Math.Pow(defend, 1.5);
        return a / (a + d);
    }

    /// <summary>
    /// A year's progress of a siege: faster with more strength against the
    /// garrison, and with the stronger realm (decision "Playable 12": the
    /// troops in the province and each realm's overall Might).
    /// </summary>
    public static double SiegeRate(double attack, double garrison, double attackerRealm, double defenderRealm)
    {
        if (attack <= 0)
            return 0;
        double local = Math.Pow(attack / Math.Max(garrison, 0.1), 0.6);
        double realm = Math.Pow(Math.Clamp((attackerRealm + 1) / (defenderRealm + 1), 0.5, 2), 0.3);
        return Math.Clamp(SiegeBase * local * realm, 0.05, 1);
    }

    /// <summary>
    /// Why a target can't be attacked, or null: war with its owner is
    /// needed, an army to send, and a crossing needs a fleet a match for the defender's.
    /// </summary>
    public static string? CheckTarget(ConquestTarget t, GameState game, string ownerName)
    {
        if (t.Owner > 0 && !game.Wars.AtWar(t.Attacker, t.Owner))
            return $"Not at war with {ownerName}";
        var army = game.Realm(t.Attacker).ArmyById(t.ArmyId);
        if (army == null || Military.RawMight(army, Domain.Land) <= 0)
            return "No army with land troops chosen";
        if (t.BySea)
        {
            double fleet = Military.Might(army, Domain.Naval);
            double theirs = t.Owner > 0 ? Military.Might(game.Realm(t.Owner), Domain.Naval) : 0;
            if (fleet <= 0)
                return "Across the sea: the army needs warships";
            if (fleet < NavalNeeded * theirs)
                return $"Across the sea: their fleet ({theirs:0.0}) outmatches yours ({fleet:0.0})";
        }
        return null;
    }
}
