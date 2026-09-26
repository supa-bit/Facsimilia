using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A kind of building (data/buildings.json).</summary>
public sealed record BuildingDef(string Id, string Name, double Cost, int Years, double Upkeep, int Max, string? Needs,
    Dictionary<string, double> Effects, string Description, string? Requires = null)
{
    public double Effect(string key) => Effects.TryGetValue(key, out var v) ? v : 0;
}

/// <summary>The core set of buildings (decision "Playable 32").</summary>
public sealed class BuildingCatalog
{
    public const string Path = "res://data/buildings.json";
    static BuildingCatalog? _instance;
    public List<BuildingDef> All { get; } = new();

    public static BuildingCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public BuildingDef? this[string id] => All.FirstOrDefault(b => b.Id == id);

    public static BuildingCatalog Parse(string json)
    {
        var c = new BuildingCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var b in doc.RootElement.GetProperty("buildings").EnumerateArray())
        {
            var effects = new Dictionary<string, double>();
            foreach (var e in b.GetProperty("effects").EnumerateObject())
                effects[e.Name] = e.Value.GetDouble();
            c.All.Add(new BuildingDef(b.GetProperty("id").GetString()!, b.GetProperty("name").GetString()!,
                b.GetProperty("cost").GetDouble(), b.GetProperty("years").GetInt32(), b.GetProperty("upkeep").GetDouble(),
                b.GetProperty("max").GetInt32(), b.TryGetProperty("needs", out var n) ? n.GetString() : null, effects,
                b.GetProperty("description").GetString()!, b.TryGetProperty("requires", out var rq) ? rq.GetString() : null));
        }
        return c;
    }

    /// <summary>The sum of one effect over a province's finished buildings.</summary>
    public double Effect(ProvinceState p, string key)
    {
        double sum = 0;
        foreach (var (id, level) in p.Buildings)
            if (this[id] is { } b)
                sum += b.Effect(key) * level;
        return sum;
    }

    /// <summary>Upkeep of a province's buildings, talents a year.</summary>
    public double Upkeep(ProvinceState p) => p.Buildings.Sum(kv => (this[kv.Key]?.Upkeep ?? 0) * kv.Value);
}
