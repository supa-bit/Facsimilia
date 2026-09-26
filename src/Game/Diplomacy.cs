using System;
using System.Collections.Generic;

namespace Facsimilia.Game;

/// <summary>
/// Declaring war and making peace (decisions "Playable 15" and "16",
/// suggested options): any realm may declare war on another, but a war
/// without a pretext - a shared border to dispute, or an old wrong - angers
/// everyone; wars end in a peace the losing side accepts depending on the
/// war score, with tribute for a clear victory, and a ten-year truce.
/// </summary>
public static class Diplomacy
{
    /// <summary>Score below which a side accepts peace, keeping what each holds.</summary>
    public const double AcceptLosing = -20;
    /// <summary>After this many years, a war-weary side accepts an even peace.</summary>
    public const int WearyYears = 8;
    /// <summary>Score needed to demand tribute.</summary>
    public const double TributeScore = 40;
    /// <summary>Tribute: this share of the loser's treasury, but never more than half a year's income.</summary>
    public const double TributeShare = 0.3;
    public const double TributeMaxYears = 0.5;
    /// <summary>A side this far ahead after two years is content with what it has won.</summary>
    public const double VictorContent = 20;

    /// <summary>Why a war can't be declared now, or null.</summary>
    public static string? CanDeclare(GameState game, int attacker, int defender, int year)
    {
        if (attacker == defender)
            return "That is your own realm.";
        if (game.Wars.AtWar(attacker, defender))
            return "You are already at war.";
        if (game.Wars.TruceUntil(attacker, defender, year) is int until)
            return $"A truce holds until {Math.Abs(until)} {(until < 0 ? "BC" : "AD")}.";
        if (game.Treaties.OverlordOf(attacker) != 0)
            return "A vassal makes no wars of its own.";
        if (game.Treaties.Allied(attacker, defender))
            return "You are allies: break the alliance first.";
        if (game.Treaties.OverlordOf(defender) == attacker)
            return "They are your vassal.";
        return null;
    }

    public static War Declare(GameState game, int attacker, int defender, int year, bool pretext, string casusBelli = "") =>
        game.Wars.Declare(attacker, defender, year, pretext, casusBelli);

    // --- War exhaustion (decision "Playable 16": peace can be forced) -------------

    /// <summary>Exhaustion a side gains each year at war, for each province it loses, and at 100 it must make peace.</summary>
    public const double ExhaustionPerYear = 3, ExhaustionPerProvince = 8, ForcedPeace = 100;
    /// <summary>Losing this share of the realm's sustainable manpower in men adds 30 exhaustion.</summary>
    public const double ExhaustionPerManpowerLost = 30;

    // --- Peace terms ---------------------------------------------------------------

    /// <summary>War score a province costs: more the bigger a share of the loser's people it holds; half if already besieged.</summary>
    public static double ProvinceCost(double provincePeople, double loserPeople, bool besieged) =>
        Math.Clamp(PeaceTerms.ProvinceMin + 100 * provincePeople / Math.Max(loserPeople, 1), PeaceTerms.ProvinceMin,
            PeaceTerms.ProvinceMax) * (besieged ? 0.5 : 1);

    /// <summary>
    /// How much war score the other side will pay for peace: what it has lost,
    /// plus half its exhaustion (tired realms give more), plus a little for a
    /// war that drags on.
    /// </summary>
    public static double WillingToPay(War war, int offering, int year)
    {
        int other = war.Enemy(offering);
        int years = year - war.Since;
        return Math.Max(0, -war.ScoreFor(other)) + 0.5 * war.ExhaustionFor(other) + (years >= 3 * WearyYears ? 20 : 0);
    }

    /// <summary>Whether the other side accepts terms costing this much war score (0 = white peace).</summary>
    public static bool AcceptsTerms(War war, int offering, int year, double cost)
    {
        if (cost <= 0)
            return Accepts(war, offering, year, demandTribute: false) || war.ExhaustionFor(war.Enemy(offering)) >= 50;
        return cost <= WillingToPay(war, offering, year);
    }

    /// <summary>
    /// Whether the other side accepts peace. keeping what each holds, or with
    /// tribute to the offering side if demanded.
    /// </summary>
    public static bool Accepts(War war, int offering, int year, bool demandTribute)
    {
        int other = war.Enemy(offering);
        double theirScore = war.ScoreFor(other);
        int years = year - war.Since;
        if (demandTribute)
            return theirScore <= -TributeScore;
        return theirScore <= AcceptLosing || (theirScore >= VictorContent && years >= 2)
            || (years >= WearyYears && theirScore <= 10) || years >= 3 * WearyYears;
    }

    /// <summary>Makes peace; with tribute, silver passes from the loser. Returns the tribute paid.</summary>
    public static double MakePeace(GameState game, War war, int winner, int year, bool tribute, double loserIncome)
    {
        double paid = 0;
        if (tribute)
        {
            var loser = game.Realm(war.Enemy(winner));
            paid = Math.Min(Math.Max(loser.Treasury * TributeShare, loserIncome * TributeShare), loserIncome * TributeMaxYears);
            loser.Treasury -= paid;
            if (loser.Treasury < 0)
            {
                loser.Debt += -loser.Treasury;
                loser.Treasury = 0;
            }
            game.Realm(winner).Treasury += paid;
        }
        game.Wars.MakePeace(war, year);
        return paid;
    }
}
