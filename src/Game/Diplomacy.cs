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
        return null;
    }

    public static War Declare(GameState game, int attacker, int defender, int year, bool pretext) =>
        game.Wars.Declare(attacker, defender, year, pretext);

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
