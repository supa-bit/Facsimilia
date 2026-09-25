using System;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// A region's renewable stocks, each 1 = as nature left it (MECHANICS.md,
/// "Harvests, weather and renewable stocks"): soil nutrients, standing
/// forest, pasture forage and fish. Heavy use wears them down; rest lets
/// them recover, each at its own speed.
/// </summary>
public sealed class RegionNature
{
    public double Soil { get; set; } = 1;
    public double Forest { get; set; } = 1;
    public double Pasture { get; set; } = 1;
    public double Fish { get; set; } = 1;
    /// <summary>This year's harvest: 1 an ordinary year, 0.6 a famine, 1.2 a bumper crop.</summary>
    public double Harvest { get; set; } = 1;

    public GDictionary ToDict() => new()
    {
        ["soil"] = Soil, ["forest"] = Forest, ["pasture"] = Pasture, ["fish"] = Fish, ["harvest"] = Harvest,
    };

    public static RegionNature FromDict(GDictionary d) => new()
    {
        Soil = d["soil"].AsDouble(), Forest = d["forest"].AsDouble(), Pasture = d["pasture"].AsDouble(),
        Fish = d["fish"].AsDouble(), Harvest = d["harvest"].AsDouble(),
    };
}

public static class Nature
{
    // Recovery a year, as a share of the gap to full: fish in a few years,
    // pasture in a decade or two, soil in decades, forests in a century.
    public const double SoilRegrow = 0.03, ForestRegrow = 0.01, PastureRegrow = 0.08, FishRegrow = 0.2;
    // Wear a year at full pressure (people at the land's capacity).
    public const double SoilWear = 0.035, ForestWear = 0.02, PastureWear = 0.06, FishWear = 0.12;
    /// <summary>Below this pressure (people against what the land can feed) nothing wears down.</summary>
    public const double SafePressure = 0.5;
    public const double FamineHarvest = 0.75, BumperHarvest = 1.2;

    /// <summary>
    /// The year's weather in a region, as a harvest multiplier: a draw
    /// shared by the whole map (a bad year for the Mediterranean) and one
    /// of the region's own, larger where rain is unreliable and smaller
    /// where rivers water the fields. The same year and region always give
    /// the same weather.
    /// </summary>
    public static double Harvest(int year, int region, double rainVariability, double irrigation)
    {
        double shared = Normal(year, 0);
        double own = Normal(year, region);
        double sigma = 0.05 + 0.4 * rainVariability * (1 - 0.6 * Math.Clamp(irrigation, 0, 1));
        return Math.Clamp(1 + sigma * (0.4 * shared + 0.9 * own), 0.4, 1.35);
    }

    /// <summary>A standard normal number fixed by (year, key).</summary>
    public static double Normal(int year, int key)
    {
        var rng = new Random(HashCode.Combine(year, key, 424243));
        double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>One year of wear and recovery; pressure = people / what the land can feed.</summary>
    public static void Tick(RegionNature n, double pressure, double dungKept)
    {
        double wear = Math.Max(0, pressure - SafePressure) / (1 - SafePressure);
        // Manure spread on the fields slows the soil's loss; dung burned for fuel doesn't.
        n.Soil = Step(n.Soil, SoilRegrow * (0.5 + 0.5 * dungKept), SoilWear * wear);
        n.Forest = Step(n.Forest, ForestRegrow, ForestWear * wear);
        n.Pasture = Step(n.Pasture, PastureRegrow, PastureWear * wear);
        n.Fish = Step(n.Fish, FishRegrow, FishWear * wear);
    }

    static double Step(double stock, double regrow, double wear) =>
        Math.Clamp(stock + regrow * (1 - stock) - wear * stock, 0.2, 1);

    // What each stock does to production: never below half.
    public static double FieldYield(RegionNature n) => n.Harvest * (0.5 + 0.5 * n.Soil);
    public static double OrchardYield(RegionNature n) => 1 + (n.Harvest - 1) * 0.6;
    public static double HerdYield(RegionNature n) => (0.5 + 0.5 * n.Pasture) * (1 + (n.Harvest - 1) * 0.5);
    public static double FishYield(RegionNature n) => 0.5 + 0.5 * n.Fish;
    public static double ForestYield(RegionNature n) => 0.5 + 0.5 * n.Forest;
    /// <summary>Carrying capacity as the soil and pasture now allow.</summary>
    public static double CapacityShare(RegionNature n) => 0.7 + 0.2 * n.Soil + 0.1 * n.Pasture;
}
