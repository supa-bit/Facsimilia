using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A coin: its name and the silver it is worth in grams.</summary>
public sealed record Coin(int From, string Name, string One, double Grams);

/// <summary>
/// Each realm's own coin (decision "Playable 4"): the treasury is kept in
/// talents of silver and shown in the realm's coin of the day.
/// </summary>
public sealed class CoinCatalog
{
    public const string Path = "res://data/coins.json";
    static CoinCatalog? _instance;
    public static CoinCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public double TalentGrams { get; private set; } = 26200;
    readonly Dictionary<string, List<Coin>> _realms = new();
    List<Coin> _default = new();

    public static CoinCatalog Parse(string json)
    {
        var c = new CoinCatalog();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        c.TalentGrams = root.GetProperty("talent_grams").GetDouble();
        static List<Coin> Read(JsonElement a) => a.EnumerateArray().Select(e => new Coin(e.GetProperty("from").GetInt32(),
            e.GetProperty("name").GetString()!, e.GetProperty("one").GetString()!, e.GetProperty("grams").GetDouble()))
            .OrderBy(x => x.From).ToList();
        foreach (var r in root.GetProperty("realms").EnumerateObject())
            c._realms[r.Name] = Read(r.Value);
        c._default = Read(root.GetProperty("default"));
        return c;
    }

    /// <summary>The realm's coin in a year.</summary>
    public Coin For(string? realmKey, int year)
    {
        var list = realmKey != null && _realms.TryGetValue(realmKey, out var l) ? l : _default;
        return list.LastOrDefault(x => x.From <= year) ?? list[0];
    }

    /// <summary>How many of a coin a sum in talents of silver makes.</summary>
    public double Coins(double talents, Coin coin) => talents * TalentGrams / coin.Grams;

    /// <summary>A short count: 4.7M, 47k, 470.</summary>
    public static string Short(double n)
    {
        double a = Math.Abs(n);
        string s = a >= 1e9 ? $"{a / 1e9:0.0}B" : a >= 1e6 ? $"{a / 1e6:0.0}M" : a >= 1e4 ? $"{a / 1e3:0}k" : $"{a:0}";
        return (n < 0 ? "−" : "") + s;
    }
}
