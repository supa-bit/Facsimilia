using System;

namespace Facsimilia.Game;

/// <summary>
/// The growth drivers' Security factor (MECHANICS.md, "War's toll on
/// growth"): war fought on a region's land leaves it ravaged (burned fields,
/// flight, slaughtered herds), and a ravaged region grows slowly for years
/// as the ravage fades; long peace is mildly good for growth. Your answer
/// "Devastated land grows slowly for years" (Hannibal's years in Italy left
/// the south poorer for generations).
/// </summary>
public static class Security
{
    /// <summary>A battle on the region's land, whatever its size.</summary>
    public const double BattleRavage = 0.15;
    /// <summary>More for a great slaughter: this many fallen add another BattleRavage.</summary>
    public const double FallenPerRavage = 20_000;
    /// <summary>The most a single battle adds (Cannae).</summary>
    public const double BattleRavageMax = 0.45;
    /// <summary>A whole year's siege: the besiegers live off the land around the town.</summary>
    public const double SiegeRavage = 0.3;
    /// <summary>Each year a region keeps this share of its ravage: half is gone in about 5.4 years, three quarters in 11.</summary>
    public const double Fade = 0.88;
    /// <summary>Below this a region counts as recovered (and is forgotten).</summary>
    public const double Recovered = 0.02;
    /// <summary>Years of peace for the full peace score.</summary>
    public const double PeaceYearsFull = 50;
    /// <summary>The Security score of a long peace (the Pax Romana): mild next to war's toll.</summary>
    public const double PeaceScore = 0.6;

    /// <summary>What one battle adds, by the men who fell in it.</summary>
    public static double Battle(int fallen) =>
        Math.Min(BattleRavageMax, BattleRavage * (1 + Math.Max(0, fallen) / FallenPerRavage));

    /// <summary>What a siege adds for the months the besiegers stood there this year.</summary>
    public static double Siege(int monthsPresent) => SiegeRavage * Math.Clamp(monthsPresent / 12.0, 0, 1);

    /// <summary>Ravage after a new blow, never above full.</summary>
    public static double Add(double ravaged, double blow) => Math.Min(1, ravaged + blow);

    /// <summary>
    /// The Security score (-1..+1): a ravaged region's toll; otherwise the
    /// peace of the realm holding most of its people.
    /// </summary>
    public static double Score(double ravaged, int yearsAtPeace) =>
        ravaged >= Recovered
            ? -ravaged
            : PeaceScore * Math.Clamp(yearsAtPeace / PeaceYearsFull, 0, 1);
}
