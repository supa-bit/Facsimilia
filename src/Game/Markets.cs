using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A great market (data/markets.json): it sets prices for the land nearest it while it is open.</summary>
public sealed record Market(string Id, string Name, double Lon, double Lat, int From, int Until)
{
    public bool OpenIn(int year) => From <= year && year < Until;
}

/// <summary>A pirate haven (data/pirates.json): it raids shipping and coasts within reach until suppressed.</summary>
public sealed record PirateHaven(string Id, string Name, double Lon, double Lat, int From, int Until, double ReachKm)
{
    public bool ActiveIn(int year) => From <= year && year < Until;
}

public sealed class MarketCatalog
{
    public const string Path = "res://data/markets.json", PiratesPath = "res://data/pirates.json";
    static MarketCatalog? _instance;
    public static MarketCatalog Instance => _instance ??= Load();

    public List<Market> Markets { get; } = new();
    public List<PirateHaven> Havens { get; } = new();

    public int IndexOf(string id) => Markets.FindIndex(m => m.Id == id);

    static MarketCatalog Load()
    {
        var c = new MarketCatalog();
        using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Path)))
            foreach (var m in doc.RootElement.GetProperty("markets").EnumerateArray())
                c.Markets.Add(new Market(m.GetProperty("id").GetString()!, m.GetProperty("name").GetString()!,
                    m.GetProperty("lon").GetDouble(), m.GetProperty("lat").GetDouble(), m.GetProperty("from").GetInt32(),
                    m.TryGetProperty("until", out var u) ? u.GetInt32() : int.MaxValue));
        using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(PiratesPath)))
            foreach (var h in doc.RootElement.GetProperty("havens").EnumerateArray())
                c.Havens.Add(new PirateHaven(h.GetProperty("id").GetString()!, h.GetProperty("name").GetString()!,
                    h.GetProperty("lon").GetDouble(), h.GetProperty("lat").GetDouble(), h.GetProperty("from").GetInt32(),
                    h.GetProperty("until").GetInt32(), h.GetProperty("reach_km").GetDouble()));
        return c;
    }
}

/// <summary>
/// What trade needs to know about markets this year: each realm's share of
/// people served by each market, and each realm's price limits.
/// </summary>
public sealed class MarketContext
{
    public int Count { get; init; }
    /// <summary>Realm id -> its people's share in each market (sums to 1 for a realm with people).</summary>
    public Dictionary<int, double[]> Share { get; init; } = new();
    /// <summary>Realms limiting prices by edict: their staple goods are bought at no more than the usual price.</summary>
    public HashSet<int> PriceLimits { get; init; } = new();
    /// <summary>Goods the price limit covers (grain and staples).</summary>
    public HashSet<int> LimitedGoods { get; init; } = new();
    /// <summary>Filled by Trade.Run: each market's price factor for each good [market, good].</summary>
    public double[,] Prices { get; set; } = new double[0, 0];
    /// <summary>Filled by Trade.Run: each market's trade this year, drachmae.</summary>
    public double[] Volume { get; set; } = System.Array.Empty<double>();
    /// <summary>Filled by Trade.Run: the share of a limited good's imports a realm went without (its shortage).</summary>
    public Dictionary<int, double> Shortage { get; } = new();
}
