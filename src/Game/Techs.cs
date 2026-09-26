using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A technology (data/techs.json).</summary>
public sealed record TechDef(string Id, string Name, string Branch, double Cost, int AvailableFrom, string[] Requires,
    Dictionary<string, double> Effects, string[] Unlocks, string Description)
{
    public double Effect(string key) => Effects.TryGetValue(key, out var v) ? v : 0;
}

/// <summary>
/// The technology tree (decision "Playable 24": a web of techs that unlock
/// as time passes but must still be researched). Each realm gathers
/// research points a year from its towns and learning; the player chooses
/// what to study, other realms study what is cheapest.
/// </summary>
public sealed class TechCatalog
{
    public const string Path = "res://data/techs.json";
    static TechCatalog? _instance;
    public List<TechDef> All { get; } = new();
    readonly Dictionary<string, List<string>> _start = new();

    public static TechCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public TechDef? this[string id] => All.FirstOrDefault(t => t.Id == id);

    public static readonly string[] Branches =
        { "agriculture", "industry", "materials", "energy", "construction", "transport", "commerce", "communication",
          "land_war", "naval", "governance", "society", "science", "medicine", "belief" };
    public static readonly string[] BranchNames =
        { "Agriculture and food", "Crafts and industry", "Metals and chemistry", "Power and energy", "Building and cities",
          "Transport", "Trade and finance", "Writing and communication", "Land and air warfare", "Seafaring and navies",
          "Government and law", "Society and ideas", "Science", "Medicine and health", "Religion and the arts" };

    /// <summary>Short branch names for buttons.</summary>
    public static readonly string[] BranchShort =
        { "Farming", "Industry", "Metals", "Energy", "Cities", "Transport", "Trade", "Communication",
          "Land war", "Navies", "Government", "Society", "Science", "Medicine", "Religion, arts" };

    public static TechCatalog Parse(string json)
    {
        var c = new TechCatalog();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var s in root.GetProperty("start").EnumerateObject())
            c._start[s.Name] = s.Value.EnumerateArray().Select(x => x.GetString()!).ToList();
        foreach (var t in root.GetProperty("techs").EnumerateArray())
        {
            var effects = new Dictionary<string, double>();
            foreach (var e in t.GetProperty("effects").EnumerateObject())
                effects[e.Name] = e.Value.GetDouble();
            c.All.Add(new TechDef(t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!,
                t.GetProperty("branch").GetString()!, t.GetProperty("cost").GetDouble(), t.GetProperty("available_from").GetInt32(),
                t.GetProperty("requires").EnumerateArray().Select(x => x.GetString()!).ToArray(), effects,
                t.TryGetProperty("unlocks", out var u) ? u.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>(),
                t.GetProperty("description").GetString()!));
        }
        return c;
    }

    /// <summary>What a realm of this culture knows in 300 BC.</summary>
    public IEnumerable<string> StartFor(string culture) =>
        _start.GetValueOrDefault("*", new List<string>()).Concat(_start.GetValueOrDefault(culture, new List<string>()));

    /// <summary>Why a realm can't research a tech yet, or null.</summary>
    public string? CanResearch(RealmState r, TechDef t, int year)
    {
        if (r.Techs.Contains(t.Id))
            return "Already known.";
        if (year < t.AvailableFrom)
            return $"Not yet: known in the world from {Math.Abs(t.AvailableFrom)} {(t.AvailableFrom < 0 ? "BC" : "AD")}.";
        var missing = t.Requires.Where(x => !r.Techs.Contains(x)).ToList();
        if (missing.Count > 0)
            return "Needs " + string.Join(", ", missing.Select(m => this[m]?.Name ?? m)) + ".";
        return null;
    }

    /// <summary>The sum of one effect over what a realm knows.</summary>
    public double Effect(RealmState r, string key)
    {
        double sum = 0;
        foreach (var id in r.Techs)
            if (this[id] is { } t)
                sum += t.Effect(key);
        return sum;
    }

    /// <summary>Whether a building or unit a tech unlocks is open to the realm.</summary>
    public bool Unlocked(RealmState r, string? requires) => requires == null || r.Techs.Contains(requires);

    /// <summary>Research points a realm gathers in a year: some always, more from towns, schools and libraries.</summary>
    public double PointsPerYear(RealmState r, RealmCensus c) =>
        (6 + c.UrbanPeople / 15000 + c.OrganizedPeople / 300000) * (1 + Effect(r, "research"));

    /// <summary>
    /// A year of research: points gather, and the chosen tech is learnt when
    /// paid for. Other realms choose the cheapest open tech. Returns the tech learnt, if any.
    /// </summary>
    public TechDef? Tick(RealmState r, RealmCensus c, int year, bool chooseForThem)
    {
        r.ResearchPoints += PointsPerYear(r, c);
        if (chooseForThem && (r.Researching == "" || CanResearch(r, this[r.Researching]!, year) != null))
            r.Researching = All.Where(t => CanResearch(r, t, year) == null).OrderBy(t => t.Cost).FirstOrDefault()?.Id ?? "";
        if (r.Researching == "" || this[r.Researching] is not { } tech)
            return null;
        if (CanResearch(r, tech, year) != null)
        {
            r.Researching = "";
            return null;
        }
        if (r.ResearchPoints < tech.Cost)
            return null;
        r.ResearchPoints -= tech.Cost;
        r.Techs.Add(tech.Id);
        r.Researching = "";
        return tech;
    }
}
