using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>An ancient place from the Pleiades gazetteer: where it is, when it stood, how linked it was, and its names over time.</summary>
public sealed record Place(double Lon, double Lat, int Links, int PleiadesId, int From, int To, List<(int From, int To, string Name)> Names)
{
    /// <summary>The name in a year: the newest attested then, else the newest known before; null if the place didn't stand.</summary>
    public string? NameIn(int year)
    {
        if (year < From || year > To)
            return null;
        (int From, int To, string Name)? best = null;
        foreach (var n in Names)
            if (n.From <= year && n.To >= year && (best == null || n.From > best.Value.From))
                best = n;
        if (best == null)
            foreach (var n in Names)
                if (n.From <= year && (best == null || n.From > best.Value.From))
                    best = n;
        return best?.Name;
    }
}

/// <summary>
/// City names (decision "Playable 23"): settlements from the Pleiades
/// gazetteer (CC BY 3.0), loaded from data/places.json.
/// </summary>
public sealed class PlaceCatalog
{
    public const string Path = "res://data/places.json";
    static PlaceCatalog? _instance;
    public static PlaceCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<Place> Places { get; } = new();

    public static PlaceCatalog Parse(string json)
    {
        var c = new PlaceCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty("places").EnumerateArray())
            c.Places.Add(new Place(p[0].GetDouble(), p[1].GetDouble(), p[2].GetInt32(), p[3].GetInt32(), p[4].GetInt32(),
                p[5].GetInt32(), p[6].EnumerateArray().Select(n => (n[0].GetInt32(), n[1].GetInt32(), n[2].GetString()!)).ToList()));
        return c;
    }

    /// <summary>Places standing in a year, with their name then.</summary>
    public IEnumerable<(Place Place, string Name)> StandingIn(int year)
    {
        foreach (var p in Places)
            if (p.NameIn(year) is string name)
                yield return (p, name);
    }

    /// <summary>The nearest place standing in a year within maxKm, preferring well-linked ones a little.</summary>
    public string? Nearest(double lon, double lat, int year, double maxKm = 60)
    {
        Place? best = null;
        double bestScore = double.MaxValue;
        foreach (var p in Places)
        {
            if (p.From > year || year > p.To || System.Math.Abs(p.Lat - lat) > maxKm / 111.0 || p.NameIn(year) == null)
                continue;
            double km = DisasterCatalog.Km(lon, lat, p.Lon, p.Lat);
            if (km > maxKm)
                continue;
            double score = km / (1 + 0.1 * p.Links);
            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best?.NameIn(year);
    }
}
