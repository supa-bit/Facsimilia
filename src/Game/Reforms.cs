using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One military reform from history (data/military_reforms.json).</summary>
public sealed record ReformDef(string Id, string Name, string Civ, int Year, string Cause, string CauseArg, int CauseYears,
    string? Tech, double Cost, int Years, double Rival, Dictionary<string, double> Effects, string Source, string Text);

/// <summary>
/// Military reforms (War: armies, units and generals): your answers
/// "Offered when conditions match; they happen for other realms on their
/// dates", "Every reform we can source to 1 AD", "History's causes plus a
/// technology", "Silver, years to take effect, and the estates' anger" (the
/// rival faction stands in for the estates), and "You can take it later from
/// a reforms list".
/// </summary>
public sealed class ReformCatalog
{
    public const string Path = "res://data/military_reforms.json";
    /// <summary>A defeat or battle counts as recent for this many years.</summary>
    public const int RecentYears = 10;
    /// <summary>A defeat this bloody (men lost) is a heavy one.</summary>
    public const int HeavyDefeatDead = 10_000;

    static ReformCatalog? _instance;
    public static ReformCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<ReformDef> All { get; } = new();
    readonly Dictionary<string, ReformDef> _byId = new();
    public ReformDef? this[string id] => _byId.GetValueOrDefault(id);

    public static ReformCatalog Parse(string json)
    {
        var c = new ReformCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.GetProperty("reforms").EnumerateArray())
        {
            var cause = r.GetProperty("cause");
            string arg = cause.TryGetProperty("civ", out var cv) ? cv.GetString()! : cause.TryGetProperty("culture", out var cu) ? cu.GetString()! : "";
            var def = new ReformDef(r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!, r.GetProperty("civ").GetString()!,
                r.GetProperty("year").GetInt32(), cause.GetProperty("kind").GetString()!, arg,
                cause.TryGetProperty("years", out var y) ? y.GetInt32() : 0,
                r.TryGetProperty("tech", out var t) ? t.GetString() : null,
                r.GetProperty("cost").GetDouble(), r.GetProperty("years").GetInt32(), r.GetProperty("rival").GetDouble(),
                r.GetProperty("effects").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()),
                r.GetProperty("source").GetString()!, r.GetProperty("text").GetString()!);
            c.All.Add(def);
            c._byId[def.Id] = def;
        }
        return c;
    }

    /// <summary>The sum of a realm's enacted reforms' effect of one kind.</summary>
    public double Effect(RealmState r, string key)
    {
        double e = 0;
        foreach (var id in r.Reforms.Keys)
            if (this[id] is { } d && d.Effects.TryGetValue(key, out var v))
                e += v;
        return e;
    }

    /// <summary>A reform takes effect: the realm's call-up and army pay change at once; the rest is read from its reforms.</summary>
    public static void Enact(RealmState r, ReformDef d, int year)
    {
        r.Reforms[d.Id] = year;
        r.ReformsPending.Remove(d.Id);
        r.ReformsOffered.Remove(d.Id);
        if (d.Effects.TryGetValue("manpower", out var m))
            r.ManpowerMultiplier *= 1 + m;
        if (d.Effects.TryGetValue("upkeep", out var u))
            r.UpkeepShare *= 1 + u;
    }
}
