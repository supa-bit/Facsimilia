using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One army tradition: history's (a people's from the start) or earned (by one arm's victories, or by cities taken).</summary>
public sealed record TraditionDef(string Id, string Name, string Kind, string[] Cultures, string[] Realms, string Role,
    Dictionary<string, double> Effects, string Text)
{
    public bool Earned => Kind == "earned";
}

/// <summary>
/// Army traditions (War: armies, units and generals): your answers "Each
/// realm builds traditions from its wars that shape its units and
/// generals", "From history, plus earned ones" and "Both" (a tradition
/// fades after 50 years without use, and is lost after a crushing defeat).
/// Loaded from data/traditions.json.
/// </summary>
public sealed class TraditionCatalog
{
    public const string Path = "res://data/traditions.json";
    /// <summary>Victories won chiefly by one arm to earn its tradition, and cities taken for siegecraft.</summary>
    public const int VictoriesToEarn = 6, CitiesToEarn = 6;
    /// <summary>A tradition unused this many years is forgotten.</summary>
    public const int FadeYears = 50;
    /// <summary>A crushing defeat: the loser lost at least this share of its men, and this many times the winner's dead.</summary>
    public const double CrushingShare = 0.4, CrushingRatio = 3;

    static TraditionCatalog? _instance;
    public static TraditionCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<TraditionDef> All { get; } = new();
    readonly Dictionary<string, TraditionDef> _byId = new();
    public TraditionDef? this[string id] => _byId.GetValueOrDefault(id);

    public static TraditionCatalog Parse(string json)
    {
        var c = new TraditionCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var t in doc.RootElement.GetProperty("traditions").EnumerateArray())
        {
            string[] List(string key) => t.TryGetProperty(key, out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
            var def = new TraditionDef(t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!, t.GetProperty("kind").GetString()!,
                List("cultures"), List("realms"), t.TryGetProperty("role", out var r) ? r.GetString()! : "",
                t.GetProperty("effects").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()),
                t.GetProperty("text").GetString()!);
            c.All.Add(def);
            c._byId[def.Id] = def;
        }
        return c;
    }

    /// <summary>History's traditions of a people (and of a particular realm, by its civ key).</summary>
    public IEnumerable<TraditionDef> HistoryFor(string culture, string civKey) =>
        All.Where(t => !t.Earned && t.Cultures.Contains(culture) && (t.Realms.Length == 0 || t.Realms.Contains(civKey)));

    /// <summary>The tradition earned by an arm (a UnitRoles key, or "siege").</summary>
    public TraditionDef? EarnedFor(string role) => All.FirstOrDefault(t => t.Earned && t.Role == role);

    /// <summary>The sum of a realm's traditions' effect of one kind.</summary>
    public double Effect(RealmState r, string key)
    {
        double e = 0;
        foreach (var id in r.Traditions.Keys)
            if (this[id] is { } t && t.Effects.TryGetValue(key, out var v))
                e += v;
        return e;
    }

    /// <summary>The traditions of a realm that strengthen one arm (a UnitRoles key).</summary>
    public IEnumerable<TraditionDef> OfArm(RealmState r, string role) =>
        r.Traditions.Keys.Select(id => this[id]).Where(t => t != null && t.Effects.ContainsKey("might_" + role))!;

    /// <summary>A defeat so heavy it breaks an arm's tradition.</summary>
    public static bool Crushing(int loserMen, int loserLost, int winnerLost) =>
        loserMen > 0 && loserLost >= CrushingShare * loserMen && loserLost >= CrushingRatio * Math.Max(1, winnerLost);
}

/// <summary>Elite guards (decisions "Elite guards", "Which guards", "How many guards").</summary>
public static class Guards
{
    /// <summary>Guard units a realm may keep.</summary>
    public const int Max = 3;
    /// <summary>A unit this hardened (famous veterans) may be made the guard.</summary>
    public const double FamousXp = 0.8;
    /// <summary>A loyal army with guards in it is half as likely to turn on its ruler: the guard stays the ruler's.</summary>
    public const double MutinyCut = 0.5;

    public static int Count(RealmState r) =>
        r.Armies.Sum(a => a.Regiments.Count(reg => UnitCatalog.Instance[reg.Type].Guard));

    public static bool Has(Army a) => a.Regiments.Any(reg => UnitCatalog.Instance[reg.Type].Guard);
}
