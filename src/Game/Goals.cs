using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One optional goal (data/goals.json).</summary>
public sealed record GoalDef(string Id, string Text, string Kind, int Points, string[] Regions, double Share,
    double Factor, double Target, int Year);

/// <summary>
/// Optional goals and the score (MECHANICS.md, "Score and goals"; decided:
/// a sandbox with optional goals). Every realm has history's ambitions for
/// it plus common ones; reaching one adds its points to the score, which
/// also counts people, provinces and silver.
/// </summary>
public sealed class GoalsCatalog
{
    public const string Path = "res://data/goals.json";

    readonly List<GoalDef> _common = new();
    readonly Dictionary<string, List<GoalDef>> _realms = new();
    public double PeoplePerPoint { get; private set; } = 100000;
    public double ProvincePoints { get; private set; } = 1;
    public double TalentsPerPoint { get; private set; } = 1000;
    public double RegionShare { get; private set; } = 0.6;

    public static GoalsCatalog Parse(string json)
    {
        var c = new GoalsCatalog();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var s = root.GetProperty("score");
        c.PeoplePerPoint = s.GetProperty("people_per_point").GetDouble();
        c.ProvincePoints = s.GetProperty("province_points").GetDouble();
        c.TalentsPerPoint = s.GetProperty("talents_per_point").GetDouble();
        c.RegionShare = s.GetProperty("region_share").GetDouble();
        foreach (var g in root.GetProperty("common").EnumerateArray())
            c._common.Add(Read(g, c.RegionShare));
        foreach (var r in root.GetProperty("realms").EnumerateObject())
            c._realms[r.Name] = r.Value.EnumerateArray().Select(g => Read(g, c.RegionShare)).ToList();
        return c;
    }

    static GoalDef Read(JsonElement g, double share) => new(
        g.GetProperty("id").GetString()!, g.GetProperty("text").GetString()!, g.GetProperty("kind").GetString()!,
        g.GetProperty("points").GetInt32(),
        g.TryGetProperty("regions", out var r) ? r.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>(),
        g.TryGetProperty("share", out var sh) ? sh.GetDouble() : share,
        g.TryGetProperty("factor", out var f) ? f.GetDouble() : 1,
        g.TryGetProperty("target", out var t) ? t.GetDouble() : 0,
        g.TryGetProperty("year", out var y) ? y.GetInt32() : 0);

    /// <summary>A realm's goals: history's for it first, then the common ones.</summary>
    public IReadOnlyList<GoalDef> For(string civKey) =>
        (_realms.TryGetValue(civKey, out var own) ? own : new List<GoalDef>()).Concat(_common).ToList();

    public double BaseScore(double people, int provinces, double treasury, double debt) =>
        people / PeoplePerPoint + provinces * ProvincePoints + Math.Max(0, treasury - debt) / TalentsPerPoint;
}
