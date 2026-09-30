using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A technology (data/techs.json).</summary>
public sealed record TechDef(string Id, string Name, string Branch, double Cost, int AvailableFrom, string[] Requires,
    Dictionary<string, double> Effects, string[] Unlocks, string Description)
{
    /// <summary>For an invention from beyond the map: the regions it enters by (empty for others).</summary>
    public string[] Beyond { get; init; } = Array.Empty<string>();

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
                t.GetProperty("description").GetString()!)
            {
                Beyond = t.TryGetProperty("beyond", out var by) ? by.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>(),
            });
        }
        return c;
    }

    /// <summary>What a realm of this culture knows in 300 BC.</summary>
    public IEnumerable<string> StartFor(string culture) =>
        _start.GetValueOrDefault("*", new List<string>()).Concat(_start.GetValueOrDefault(culture, new List<string>()));

    /// <summary>A tech opens this many years before history's date (decision "How far ahead of history").</summary>
    public const int EarlyYears = 50;
    /// <summary>At the earliest a tech costs this many times its price.</summary>
    public const double EarlyMaxFactor = 3;
    /// <summary>A tech every contact knows costs this much less (decision "Learning from neighbours").</summary>
    public const double DiffusionDiscount = 0.4;
    /// <summary>All research is scaled by this, set so leading realms keep near history's pace (a 100-year run, 30 Sep 2026).</summary>
    public const double PointsScale = 1.5;
    /// <summary>Old knowledge gets cheaper as it spreads: up to this much less, reached this many years after its date (decision "Pace of research").</summary>
    public const double AgeDiscount = 0.5;
    public const int AgeYears = 200;
    /// <summary>Each realm a realm trades or borders with adds this much research, up to ContactResearchMax.</summary>
    public const double ContactResearch = 0.04, ContactResearchMax = 0.4;

    public static int BranchIndex(string branch) => Array.IndexOf(Branches, branch);

    /// <summary>Why a realm can't research a tech yet, or null.</summary>
    public string? CanResearch(RealmState r, TechDef t, int year, TechContext? ctx = null)
    {
        if (r.Techs.Contains(t.Id))
            return "Already known.";
        if (year < t.AvailableFrom - EarlyYears)
            return $"Not yet: known in the world from {ThemeYear(t.AvailableFrom)}; it can be studied from {ThemeYear(t.AvailableFrom - EarlyYears)}.";
        if (t.Beyond.Length > 0)
        {
            if (year < t.AvailableFrom)
                return $"Not yet arrived: it comes from beyond the map around {ThemeYear(t.AvailableFrom)}.";
            if (ctx != null && !ctx.Arrived(t))
                return $"Not yet here: it comes from beyond the map through {string.Join(", ", t.Beyond)}; hold land there or trade with a realm that knows it.";
        }
        var missing = t.Requires.Where(x => !r.Techs.Contains(x)).ToList();
        if (missing.Count > 0)
            return "Needs " + string.Join(", ", missing.Select(m => this[m]?.Name ?? m)) + ".";
        return null;
    }

    static string ThemeYear(int y) => y < 0 ? $"{-y} BC" : $"AD {y}";

    /// <summary>
    /// What a tech costs this realm now: dearer the earlier before history's
    /// date, cheaper the longer the world has known it and the more of its
    /// neighbours and trade partners know it.
    /// </summary>
    public double Cost(TechDef t, int year, TechContext? ctx = null)
    {
        double early = Math.Clamp(t.AvailableFrom - year, 0, EarlyYears) / (double)EarlyYears;
        double age = Math.Clamp(year - t.AvailableFrom, 0, AgeYears) / (double)AgeYears;
        double cost = t.Cost * (1 + (EarlyMaxFactor - 1) * early) * (1 - AgeDiscount * age);
        if (ctx != null)
            cost *= 1 - DiffusionDiscount * ctx.KnownShare(t);
        return cost;
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

    /// <summary>Research points a realm gathers in a year: some always, more from towns, schools, libraries and trade contacts.</summary>
    public double PointsPerYear(RealmState r, RealmCensus c, TechContext? ctx = null) =>
        PointsScale * (6 + c.UrbanPeople / 15000 + c.OrganizedPeople / 300000)
        * (1 + Effect(r, "research") + Math.Min(ContactResearchMax, ContactResearch * (ctx?.Contacts ?? 0)));

    /// <summary>
    /// A year of research (decision "Researching several at once"): the
    /// points are shared among the branches by the realm's shares, and each
    /// branch learns its chosen tech when paid for. Other realms choose the
    /// cheapest open tech in each branch. Returns the techs learnt.
    /// </summary>
    public List<TechDef> Tick(RealmState r, RealmCensus c, int year, bool chooseForThem, TechContext? ctx = null)
    {
        var learnt = new List<TechDef>();
        double points = PointsPerYear(r, c, ctx);
        if (chooseForThem)
        {
            // Other realms put their effort into one branch at a time: the one with the cheapest open tech.
            var next = All.Where(t => CanResearch(r, t, year, ctx) == null).OrderBy(t => Cost(t, year, ctx)).FirstOrDefault();
            for (int b = 0; b < Branches.Length; b++)
                r.BranchShare[b] = next != null && Branches[b] == next.Branch ? 1 : 0;
            if (next != null)
                r.BranchResearching[BranchIndex(next.Branch)] = next.Id;
        }
        double total = r.BranchShare.Sum();
        for (int b = 0; b < Branches.Length; b++)
        {
            double share = total > 0 ? r.BranchShare[b] / total : 1.0 / Branches.Length;
            r.BranchPoints[b] += points * share;
            if (chooseForThem && (r.BranchResearching[b] == "" || this[r.BranchResearching[b]] is not { } cur || CanResearch(r, cur, year, ctx) != null))
                r.BranchResearching[b] = All.Where(t => t.Branch == Branches[b] && CanResearch(r, t, year, ctx) == null)
                    .OrderBy(t => Cost(t, year, ctx)).FirstOrDefault()?.Id ?? "";
            if (r.BranchResearching[b] == "" || this[r.BranchResearching[b]] is not { } tech)
                continue;
            if (CanResearch(r, tech, year, ctx) != null)
            {
                r.BranchResearching[b] = "";
                continue;
            }
            double cost = Cost(tech, year, ctx);
            if (r.BranchPoints[b] < cost)
                continue;
            r.BranchPoints[b] -= cost;
            r.Techs.Add(tech.Id);
            r.BranchResearching[b] = "";
            learnt.Add(tech);
        }
        return learnt;
    }
}

/// <summary>
/// What a realm's neighbours and trade partners know this year, and which
/// inventions from beyond the map have reached it.
/// </summary>
public sealed class TechContext
{
    /// <summary>How many realms it borders or trades with.</summary>
    public int Contacts { get; init; }
    /// <summary>The share of those contacts that know a tech, 0..1.</summary>
    public Func<TechDef, double> KnownShare { get; init; } = _ => 0;
    /// <summary>Whether an invention from beyond has reached the realm (it holds an entry region, or a contact knows it).</summary>
    public Func<TechDef, bool> Arrived { get; init; } = _ => true;
}
