using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A historical trade route: a chain of places (lon, lat), open from a year.</summary>
public sealed record TradeRoute(string Id, string Name, string Kind, int From, int Until,
    List<(string Name, double Lon, double Lat)> Points);

/// <summary>Trade routes (decision "Playable 8"), loaded from data/trade_routes.json.</summary>
public sealed class TradeRouteCatalog
{
    public const string Path = "res://data/trade_routes.json";
    static TradeRouteCatalog? _instance;
    public static TradeRouteCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<TradeRoute> Routes { get; } = new();

    /// <summary>Toll each realm between buyer and seller takes on what passes, as a share of its value.</summary>
    public const double TransitToll = 0.02;

    public IEnumerable<TradeRoute> OpenIn(int year) => Routes.Where(r => r.From <= year && year < r.Until);

    public static TradeRouteCatalog Parse(string json)
    {
        var c = new TradeRouteCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var r in doc.RootElement.GetProperty("routes").EnumerateArray())
            c.Routes.Add(new TradeRoute(r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!,
                r.GetProperty("kind").GetString()!, r.GetProperty("from").GetInt32(),
                r.TryGetProperty("until", out var u) ? u.GetInt32() : int.MaxValue,
                r.GetProperty("points").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetDouble(), p[2].GetDouble())).ToList()));
        return c;
    }
}
