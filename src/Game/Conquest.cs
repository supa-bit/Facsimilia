using System;
using System.Collections.Generic;

namespace Facsimilia.Game;

/// <summary>
/// One place an attacker tries to take this turn: a whole enemy province,
/// or a stretch of unorganized or unclaimed land (MECHANICS.md, "Annexation
/// resolution"). People are the ones living there: their militia resists.
/// </summary>
public sealed class ConquestTarget
{
    public int Attacker { get; init; }
    public int Owner { get; init; }             // 0 = unclaimed
    public int ProvinceId { get; init; }        // 0 = unorganized or unclaimed land
    public string Name { get; init; } = "";
    public double People { get; set; }
    public bool BySea { get; init; }            // only reachable across water
    public List<int> Cells { get; } = new();    // ownership cells to flip, for land without a province

    // Filled in by Resolve.
    public double AttackerMight { get; set; }
    public double DefenderMight { get; set; }
    public double Chance { get; set; }
    public bool Won { get; set; }
    public string? Problem { get; set; }        // why it can't be attacked (no war, no fleet)
}

/// <summary>
/// Resolving conquests: each target is one decisive fight when the turn
/// ends (decision "Playable 12", suggested option: at once). The attacker's
/// army is split across its targets; the defender commits part of its army,
/// split across every place it is attacked, plus the local militia.
/// </summary>
public static class Conquest
{
    /// <summary>Militia: about 2% of people take up arms, at half a soldier's worth: 0.8 Might per 100,000 people.</summary>
    public const double MilitiaPerPerson = 0.8 / 100000;
    /// <summary>Share of a defending realm's army it commits against invaders.</summary>
    public const double DefenderCommit = 0.6;
    /// <summary>A realm's own organized province is garrisoned: its militia counts this much more.</summary>
    public const double ProvinceGarrison = 1.5;
    /// <summary>Crossing the sea: the fleet must be at least this share of the defender's.</summary>
    public const double NavalNeeded = 0.8;
    public const double LoserLosses = 0.15, WinnerLosses = 0.06;
    public const double FatiguePerFight = 0.12;
    /// <summary>Reach (decision "Playable 13", suggested: reach and supply): km an army advances in a year over land.</summary>
    public const double ReachKm = 150;
    /// <summary>With a fleet, travel by sea costs this share of travel over land.</summary>
    public const double SeaCostShare = 0.35;

    public static double Militia(double people, bool province) =>
        people * MilitiaPerPerson * (province ? ProvinceGarrison : 1);

    /// <summary>Chance the attacker wins: rises with the ratio of strength, softened (a power of 1.5).</summary>
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
    /// Estimates every target's fight (Might on both sides and the chance),
    /// without deciding it: what the confirmation panel shows while
    /// painting. Targets with a Problem are left out of the split.
    /// </summary>
    public static void Estimate(IReadOnlyList<ConquestTarget> targets, GameState game)
    {
        var byAttacker = new Dictionary<int, int>();
        var againstDefender = new Dictionary<int, int>();
        foreach (var t in targets)
        {
            if (t.Problem != null)
                continue;
            byAttacker[t.Attacker] = byAttacker.GetValueOrDefault(t.Attacker) + 1;
            if (t.Owner > 0)
                againstDefender[t.Owner] = againstDefender.GetValueOrDefault(t.Owner) + 1;
        }
        foreach (var t in targets)
        {
            if (t.Problem != null)
            {
                t.Chance = 0;
                continue;
            }
            var att = game.Realm(t.Attacker);
            t.AttackerMight = Military.Might(att, Domain.Land) / byAttacker[t.Attacker];
            double defArmy = 0;
            if (t.Owner > 0)
                defArmy = DefenderCommit * Military.Might(game.Realm(t.Owner), Domain.Land) / againstDefender[t.Owner];
            t.DefenderMight = defArmy + Militia(t.People, t.ProvinceId != 0);
            t.Chance = WinChance(t.AttackerMight, t.DefenderMight);
        }
    }

    /// <summary>
    /// Why a target can't be attacked, or null: war with its owner is
    /// needed, and a crossing needs a fleet a match for the defender's.
    /// </summary>
    public static string? CheckTarget(ConquestTarget t, GameState game, string ownerName)
    {
        if (t.Owner > 0 && !game.Wars.AtWar(t.Attacker, t.Owner))
            return $"Not at war with {ownerName}";
        if (t.BySea)
        {
            double fleet = Military.Might(game.Realm(t.Attacker), Domain.Naval);
            double theirs = t.Owner > 0 ? Military.Might(game.Realm(t.Owner), Domain.Naval) : 0;
            if (fleet <= 0)
                return "Across the sea: you need warships";
            if (fleet < NavalNeeded * theirs)
                return $"Across the sea: their fleet ({theirs:0.0}) outmatches yours ({fleet:0.0})";
        }
        return null;
    }

    /// <summary>
    /// Decides every target: rolls the fights, applies losses, weariness and
    /// war score. The caller hands over won land. Returns the men lost per realm.
    /// </summary>
    public static Dictionary<int, double> Resolve(IReadOnlyList<ConquestTarget> targets, GameState game, Random rng,
        Func<int, double> realmPeople)
    {
        Estimate(targets, game);
        var losses = new Dictionary<int, double>();
        var engaged = new Dictionary<int, int>();
        foreach (var t in targets)
            if (t.Problem == null)
                engaged[t.Attacker] = engaged.GetValueOrDefault(t.Attacker) + 1;
        foreach (var t in targets)
        {
            if (t.Problem != null)
                continue;
            t.Won = rng.NextDouble() < t.Chance;
            var att = game.Realm(t.Attacker);
            double share = 1.0 / engaged[t.Attacker];
            double attLost = Military.TakeLosses(att, share * (t.Won ? WinnerLosses : LoserLosses), rng);
            losses[t.Attacker] = losses.GetValueOrDefault(t.Attacker) + attLost;
            att.Fatigue = Math.Min(1, att.Fatigue + FatiguePerFight);
            if (t.Owner <= 0)
                continue;
            var def = game.Realm(t.Owner);
            double committed = t.DefenderMight - Militia(t.People, t.ProvinceId != 0);
            double defShare = committed / Math.Max(Military.Might(def, Domain.Land), 1e-9);
            double defLost = Military.TakeLosses(def, Math.Clamp(defShare, 0, 1) * (t.Won ? LoserLosses : WinnerLosses), rng);
            losses[t.Owner] = losses.GetValueOrDefault(t.Owner) + defLost;
            var war = game.Wars.Between(t.Attacker, t.Owner);
            if (war == null)
                continue;
            double people = Math.Max(realmPeople(t.Owner), 1);
            double swing = t.Won ? Math.Clamp(100 * t.People / people * 2, 5, 40) : -8;
            bool attackerStarted = war.Attacker == t.Attacker;
            war.Score = Math.Clamp(war.Score + (attackerStarted ? swing : -swing), -100, 100);
            war.AttackerLosses += attackerStarted ? attLost : defLost;
            war.DefenderLosses += attackerStarted ? defLost : attLost;
        }
        return losses;
    }
}
