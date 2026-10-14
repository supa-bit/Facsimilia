using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Dynasties;

/// <summary>A named way to die (data/deaths.json): "of the wasting sickness", "gored by a boar while hunting".</summary>
public sealed record DeathDef(string Id, string Kind, string Text, int From, int To, double Weight,
    IReadOnlyList<string> Traits, int MinAge, int MaxAge, bool Rich, string? Note);

/// <summary>
/// The designer's list of named diseases and accidents (14 Oct 2026), with
/// the names each age used (the wasting sickness, then consumption, then
/// tuberculosis), some only for people of a certain nature or station.
/// </summary>
public sealed class DeathCatalog
{
    public const string Path = "res://data/deaths.json";
    static DeathCatalog? _instance;
    public static DeathCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public IReadOnlyList<DeathDef> All { get; }
    readonly Dictionary<string, DeathDef> _byId;

    DeathCatalog(List<DeathDef> all)
    {
        All = all;
        _byId = all.ToDictionary(d => d.Id);
    }

    public DeathDef? this[string id] => _byId.GetValueOrDefault(id);

    /// <summary>The ways a person could die of this kind, this year: the age's names, their own nature and station.</summary>
    public IEnumerable<DeathDef> Possible(string kind, int year, int age, IReadOnlyCollection<string> traits, bool rich) =>
        All.Where(d => d.Kind == kind && year >= d.From && year <= d.To && age >= d.MinAge && age <= d.MaxAge
                       && (!d.Rich || rich) && (d.Traits.Count == 0 || d.Traits.Any(traits.Contains)));

    /// <summary>Picks one, by weight; the ways that fit their nature count double.</summary>
    public DeathDef? Pick(string kind, int year, int age, IReadOnlyCollection<string> traits, bool rich, Random rng)
    {
        var ways = Possible(kind, year, age, traits, rich).ToList();
        if (ways.Count == 0)
            return null;
        double W(DeathDef d) => d.Weight * (d.Traits.Count > 0 ? 2 : 1);
        double roll = rng.NextDouble() * ways.Sum(W);
        foreach (var d in ways)
            if ((roll -= W(d)) <= 0)
                return d;
        return ways[^1];
    }

    public static DeathCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var all = new List<DeathDef>();
        foreach (var d in doc.RootElement.GetProperty("deaths").EnumerateArray())
            all.Add(new DeathDef(d.GetProperty("id").GetString()!, d.GetProperty("kind").GetString()!, d.GetProperty("text").GetString()!,
                d.TryGetProperty("from", out var f) ? f.GetInt32() : int.MinValue, d.TryGetProperty("to", out var t) ? t.GetInt32() : int.MaxValue,
                d.TryGetProperty("weight", out var w) ? w.GetDouble() : 1,
                d.TryGetProperty("traits", out var tr) ? tr.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>(),
                d.TryGetProperty("min_age", out var mn) ? mn.GetInt32() : 0, d.TryGetProperty("max_age", out var mx) ? mx.GetInt32() : 200,
                d.TryGetProperty("rich", out var r) && r.GetBoolean(), d.TryGetProperty("note", out var n) ? n.GetString() : null));
        return new DeathCatalog(all);
    }
}
