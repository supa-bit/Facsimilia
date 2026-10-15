using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One of history's state monopolies (data/monopolies.json).</summary>
public sealed record MonopolyDef(string Good, string[] Realms, int From, int Until, bool Farmed, string Source)
{
    public bool In(string civ, int year) => Realms.Contains(civ) && From <= year && year < Until;
}

/// <summary>A good a realm alone may sell, and who runs the sale.</summary>
public sealed class Monopoly
{
    public string Good { get; init; } = "";
    /// <summary>Contractors run it (decision "How a monopoly works": both, chosen for each good).</summary>
    public bool Farmed { get; set; }
    /// <summary>One of history's monopolies for this realm and age: it angers people less.</summary>
    public bool Historical { get; init; }
    public int Since { get; init; }
    /// <summary>A good people need every day (grain, bread, salt...): claiming it angers them far more.</summary>
    public bool Staple { get; init; }
}

/// <summary>
/// State monopolies (Treasury and money): your answers "Declare monopolies
/// on goods", "Both, chosen for each good" (run by your officials or farmed
/// out) and "History's list, plus any other at a higher cost". The state
/// takes a share of the value of the good its land makes; officials earn
/// more but corruption eats into it, contractors pay less but steadily.
/// History's monopolies stir a little unrest; any other good stirs more (three
/// times more on a good people need every day) and angers the merchants
/// (customs fall). Who sets the price, smuggling,
/// ending a monopoly and other realms' monopolies wait on their questions.
/// </summary>
public sealed class MonopolyCatalog
{
    public const string Path = "res://data/monopolies.json";
    /// <summary>
    /// The state's share of the good's value when its officials sell it: the
    /// Ptolemies bought oil at a fixed low price and sold it at about twice
    /// that (P. Rev. cols. 39-56), so about half the value.
    /// </summary>
    public const double OfficialsTake = 0.5;
    /// <summary>Contractors bid less than the whole profit: they keep their own margin.</summary>
    public const double FarmedTake = 0.35;
    /// <summary>Unrest across the realm for each monopoly: history's, and any other good.</summary>
    public const double HistoricalUnrest = 0.01, OtherUnrest = 0.03;
    /// <summary>A monopoly history didn't have on a good people need every day stirs this many times the unrest (bread riots).</summary>
    public const double StapleUnrest = 3;
    /// <summary>Each monopoly on a good history didn't monopolise costs this share of customs (merchants trade elsewhere).</summary>
    public const double OtherCustomsCut = 0.04;
    /// <summary>A monopoly needs the realm to make at least this much of the good, in loads a year.</summary>
    public const double MinLoads = 1;

    static MonopolyCatalog? _instance;
    public static MonopolyCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<MonopolyDef> All { get; } = new();

    public static MonopolyCatalog Parse(string json)
    {
        var c = new MonopolyCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var m in doc.RootElement.GetProperty("monopolies").EnumerateArray())
            c.All.Add(new MonopolyDef(m.GetProperty("good").GetString()!,
                m.GetProperty("realms").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                m.GetProperty("from").GetInt32(), m.GetProperty("until").GetInt32(),
                m.TryGetProperty("farmed", out var f) && f.GetBoolean(), m.GetProperty("source").GetString()!));
        return c;
    }

    /// <summary>History's monopoly on this good for this realm in this year, if any.</summary>
    public MonopolyDef? Historical(string civ, string good, int year) => All.FirstOrDefault(m => m.Good == good && m.In(civ, year));

    /// <summary>History's monopolies a realm held in a year (every good, whether or not it makes it).</summary>
    public IEnumerable<MonopolyDef> HistoryFor(string civ, int year) => All.Where(m => m.In(civ, year));

    /// <summary>What one monopoly brings in a year, in talents: a share of the value of the good the realm makes.</summary>
    public static double Income(Monopoly m, double loads, double price, double corruption) =>
        loads * price / 6000.0 * (m.Farmed ? FarmedTake : OfficialsTake * (1 - corruption));

    /// <summary>Unrest from a realm's monopolies (added to every province's).</summary>
    public static double Unrest(RealmState r) => r.Monopolies.Sum(m => m.Historical ? HistoricalUnrest : OtherUnrest * (m.Staple ? StapleUnrest : 1));

    /// <summary>Share of customs kept after the merchants' anger at monopolies history didn't have.</summary>
    public static double CustomsKept(RealmState r) => Math.Max(0.5, 1 - OtherCustomsCut * r.Monopolies.Count(m => !m.Historical));
}
