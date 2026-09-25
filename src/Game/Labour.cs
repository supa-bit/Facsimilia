using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>
/// Who does the work, as history had it (data/labour.json; MECHANICS.md,
/// "Labour and people"): in each region, the shares of people who were
/// free, dependent (bound tenants, serfs, royal farmers, helots) and
/// enslaved, changing over the centuries, and when slavery was abolished.
/// </summary>
public sealed class LabourHistory
{
    public const string Path = "res://data/labour.json";

    readonly Dictionary<string, (int Year, double Dependent, double Enslaved)[]> _regions = new();
    readonly Dictionary<string, int> _abolition = new();
    public double CaptiveShare { get; private set; } = 0.05;
    public double CaptiveLoss { get; private set; } = 0.05;
    public double CaptivePrice { get; private set; } = 300;
    public double KeptShare { get; private set; } = 0.05;

    public static LabourHistory Parse(string json)
    {
        var h = new LabourHistory();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var c = root.GetProperty("captives");
        h.CaptiveShare = c.GetProperty("share_of_conquered").GetDouble();
        h.CaptiveLoss = c.GetProperty("yearly_loss").GetDouble();
        h.CaptivePrice = c.GetProperty("price_drachmae").GetDouble();
        h.KeptShare = c.GetProperty("kept_share_of_people").GetDouble();
        foreach (var r in root.GetProperty("regions").EnumerateObject())
            h._regions[r.Name] = r.Value.EnumerateArray()
                .Select(p => (p[0].GetInt32(), p[1].GetDouble(), p[2].GetDouble())).OrderBy(p => p.Item1).ToArray();
        foreach (var a in root.GetProperty("abolition").EnumerateObject())
            h._abolition[a.Name] = a.Value.GetInt32();
        return h;
    }

    public bool Abolished(string region, int year) => _abolition.TryGetValue(region, out int y) && year >= y;

    /// <summary>Shares of the region's people who are dependent and enslaved in a year (interpolated).</summary>
    public (double Dependent, double Enslaved) Shares(string region, int year)
    {
        if (!_regions.TryGetValue(region, out var points) || points.Length == 0)
            return (0.2, Abolished(region, year) ? 0 : 0.05);
        (double d, double e) = points[0].Year >= year ? (points[0].Dependent, points[0].Enslaved)
            : points[^1].Year <= year ? (points[^1].Dependent, points[^1].Enslaved) : Between(points, year);
        return (d, Abolished(region, year) ? 0 : e);
    }

    static (double, double) Between((int Year, double Dependent, double Enslaved)[] p, int year)
    {
        for (int i = 1; i < p.Length; i++)
            if (p[i].Year >= year)
            {
                double t = (double)(year - p[i - 1].Year) / (p[i].Year - p[i - 1].Year);
                return (p[i - 1].Dependent + t * (p[i].Dependent - p[i - 1].Dependent),
                    p[i - 1].Enslaved + t * (p[i].Enslaved - p[i - 1].Enslaved));
            }
        return (p[^1].Dependent, p[^1].Enslaved);
    }
}

/// <summary>What the kinds of labour do in the game.</summary>
public static class Labour
{
    /// <summary>Levies come from free men; dependents give half as many.</summary>
    public static double LevyShare(double free, double dependent, double people) =>
        people > 0 ? Math.Clamp((free + 0.5 * dependent) / people, 0, 1) : 1;

    /// <summary>Mines were worked by the enslaved (Laurion, the Spanish silver mines): more of them, more ore.</summary>
    public static double MineFactor(double enslavedShare) => 1 + 2 * enslavedShare;

    /// <summary>Unrest where many are enslaved: the risk of servile war.</summary>
    public static double ServileUnrest(double enslavedShare) => Math.Max(0, enslavedShare - 0.15) * 1.0;
}
