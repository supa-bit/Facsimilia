using System;
using Godot;
using GArray = Godot.Collections.Array;

namespace Facsimilia.Game;

/// <summary>What a conqueror does with a city that falls (your answers "Spare, plunder or raze" and "Choosing spare, plunder or raze").</summary>
public enum SackChoice
{
    Spare = 0,
    Plunder = 1,
    Raze = 2,
}

/// <summary>
/// Sacking (Cities and settlements): when a siege ends, the city is spared,
/// plundered or razed. Spare: the people come over more loyal. Plunder:
/// silver and captives, buildings damaged, some killed, a grudge. Raze:
/// everything burned, a quarter killed, the city a ruin that can be
/// resettled; other cities fear you for twenty years (Alexander at Thebes,
/// Rome at Carthage and Corinth in 146 BC).
/// </summary>
public static class Sacking
{
    public static readonly string[] Names = { "Spare", "Plunder", "Raze" };
    public static readonly string[] Help =
    {
        "Spare the city: no loot or captives, nothing burned. Its people come over to you more loyal.",
        "Plunder it: silver and captives, buildings damaged, some killed. Its people hold a grudge for years.",
        "Raze it: everything burned, a quarter of its people killed and many more enslaved; the city becomes a ruin. " +
            "Other cities will open their gates to you sooner for twenty years, but the world thinks you a conqueror.",
    };

    /// <summary>Loot from a plundered or razed province: talents per 1,000 people (about a year of its taxes).</summary>
    public const double LootPerThousand = 1.5;
    /// <summary>Share of a plundered province's people killed in the sack.</summary>
    public const double PlunderKilled = 0.03;
    /// <summary>Share of a razed province's people killed.</summary>
    public const double RazeKilled = 0.25;
    /// <summary>A razed province gives this many times the usual captives.</summary>
    public const double RazeCaptives = 3;
    /// <summary>A spared province starts this much more integrated.</summary>
    public const double SpareIntegration = 0.3;
    /// <summary>The grudge a plundered province holds (added unrest), and a razed one's.</summary>
    public const double PlunderGrudge = 0.25, RazeGrudge = 0.4;
    /// <summary>Each year a grudge keeps this share: half gone in about four years.</summary>
    public const double GrudgeFade = 0.85;
    /// <summary>Extra aggression for razing (others fear and band against you).</summary>
    public const double RazeAggression = 8;
    /// <summary>Years cities fear a realm that razed one, and how much faster its sieges go then.</summary>
    public const int TerrorYears = 20;
    public const double TerrorSiege = 1.25;
    /// <summary>Resettling a ruin: talents, and men sent out as settlers.</summary>
    public const double ResettleCost = 150, ResettleMen = 2000;

    /// <summary>
    /// What a realm the computer runs does with a city it takes: spares its
    /// own people and cities it lost before, razes rarely (3 in 100), and
    /// otherwise plunders, as most ancient armies did.
    /// </summary>
    public static SackChoice ForComputer(bool sameCulture, bool reconquest, Random rng) =>
        sameCulture ? SackChoice.Spare
        : !reconquest && rng.NextDouble() < 0.03 ? SackChoice.Raze
        : reconquest ? SackChoice.Spare
        : SackChoice.Plunder;
}

/// <summary>A city razed: where and when, by whom; resettled cities are forgotten.</summary>
public sealed record Ruin(int ProvinceId, string Name, int Year, int By)
{
    public GArray ToArray() => new() { ProvinceId, Name, Year, By };
    public static Ruin FromArray(GArray a) => new(a[0].AsInt32(), a[1].AsString(), a[2].AsInt32(), a[3].AsInt32());
}
