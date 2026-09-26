using System;
using System.Collections.Generic;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>A province's people: who they are, and how settled they are under their ruler.</summary>
public sealed class ProvinceState
{
    public string Culture { get; set; } = "";
    public string Religion { get; set; } = "";
    /// <summary>0 newly conquered .. 1 fully part of the realm: pays full tax and gives full levies.</summary>
    public double Integration { get; set; } = 1;
    /// <summary>0 content .. 1 and above: ready to rise.</summary>
    public double Unrest { get; set; }
    /// <summary>The realm that held it last year: a change means it was conquered.</summary>
    public int Owner { get; set; }
    /// <summary>Finished buildings: id -> how many (walls can be built twice).</summary>
    public Dictionary<string, int> Buildings { get; } = new();
    /// <summary>The building under way, if any, and the years left.</summary>
    public string Building { get; set; } = "";
    public int BuildingYearsLeft { get; set; }
    /// <summary>A tax rate for this province only (decision "Playable 5": optional province rates), or null for the realm's.</summary>
    public TaxRate? Tax { get; set; }

    public int Level(string building) => Buildings.TryGetValue(building, out int n) ? n : 0;

    public GDictionary ToDict()
    {
        var b = new GDictionary();
        foreach (var (id, n) in Buildings)
            b[id] = n;
        return new GDictionary
        {
            ["culture"] = Culture, ["religion"] = Religion, ["integration"] = Integration, ["unrest"] = Unrest, ["owner"] = Owner,
            ["buildings"] = b, ["building"] = Building, ["building_left"] = BuildingYearsLeft, ["tax"] = Tax is { } t ? (int)t : -1,
        };
    }

    public static ProvinceState FromDict(GDictionary d)
    {
        var p = new ProvinceState
        {
            Culture = d["culture"].AsString(), Religion = d["religion"].AsString(),
            Integration = d["integration"].AsDouble(), Unrest = d["unrest"].AsDouble(), Owner = d["owner"].AsInt32(),
            Building = d.TryGetValue("building", out var bu) ? bu.AsString() : "",
            BuildingYearsLeft = d.TryGetValue("building_left", out var bl) ? bl.AsInt32() : 0,
            Tax = d.TryGetValue("tax", out var t) && t.AsInt32() >= 0 ? (TaxRate)t.AsInt32() : null,
        };
        if (d.TryGetValue("buildings", out var b))
            foreach (var (k, v) in b.AsGodotDictionary())
                p.Buildings[k.AsString()] = v.AsInt32();
        return p;
    }
}

/// <summary>
/// Integration, unrest and revolt (MECHANICS.md, "Cultures and loyalty").
/// A conquered province starts unintegrated and grows into the realm over
/// about 10 years if its people share the ruler's culture, 40 if kin, 100 if
/// foreign; war and heavy taxes slow it. Unintegrated provinces pay less and
/// give fewer men. Unrest comes from low integration, heavy taxes, a
/// different religion, want of bread, cloth and the like, and ruling more
/// provinces than the court can manage;
/// in the player's realm, high unrest can end in revolt.
/// </summary>
public static class Loyalty
{
    public const double SameCultureYears = 10, KinYears = 40, ForeignYears = 100;
    /// <summary>Integration a province of the ruler's culture keeps when conquered (it is already half at home).</summary>
    public const double ConqueredSameCulture = 0.5;
    /// <summary>Provinces a court can manage well; more add unrest everywhere.</summary>
    public const int AdminCapacity = 15;
    public static readonly double[] TaxUnrest = { -0.1, 0.0, 0.15, 0.35 };
    public const double ReligionUnrest = 0.1;
    public const double RevoltThreshold = 0.5;
    public const double RevoltChance = 0.3;   // per year, times unrest above the threshold

    public enum Kinship { Same, Kin, Foreign }

    public static Kinship Relation(string a, string b, IReadOnlyList<(string, string)> kin)
    {
        if (a == b)
            return Kinship.Same;
        foreach (var (x, y) in kin)
            if ((x == a && y == b) || (x == b && y == a))
                return Kinship.Kin;
        return Kinship.Foreign;
    }

    /// <summary>Tax and levy share from a province: half even when newly conquered.</summary>
    public static double Yield(double integration) => 0.5 + 0.5 * Math.Clamp(integration, 0, 1);

    public static double Overreach(int provinces) => Math.Max(0, (double)provinces / AdminCapacity - 1);

    /// <summary>One year for one province.</summary>
    /// <summary>Unrest when people lack what they need: this much at nothing, none from 80% of needs met.</summary>
    public const double WantUnrest = 0.4;

    public static void Tick(ProvinceState p, Kinship rel, bool sameReligion, bool atWar, TaxRate tax, int realmProvinces,
        double satisfaction = 1, double enslavedShare = 0, double integrationBoost = 0, double unrestAdded = 0)
    {
        double years = rel switch { Kinship.Same => SameCultureYears, Kinship.Kin => KinYears, _ => ForeignYears };
        double rate = 1 / years;
        if (atWar)
            rate *= 0.5;
        if (tax >= TaxRate.Heavy)
            rate *= 0.5;
        rate *= 1 + integrationBoost;
        p.Integration = Math.Min(1, p.Integration + rate);
        p.Unrest = Math.Max(0, 0.6 * (1 - p.Integration) + TaxUnrest[(int)tax]
            + (sameReligion ? 0 : ReligionUnrest) + 0.3 * Overreach(realmProvinces)
            + WantUnrest * Math.Max(0, 0.8 - satisfaction) / 0.8
            + Labour.ServileUnrest(enslavedShare) + unrestAdded);
    }

    public static double ChanceOfRevolt(double unrest) =>
        unrest <= RevoltThreshold ? 0 : Math.Min(0.9, (unrest - RevoltThreshold) * RevoltChance);
}
