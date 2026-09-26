using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>A plague, earthquake, eruption or famine: where it struck, how far it reached, and the share it killed at its centre.</summary>
public sealed record Disaster(int Year, string Kind, string Name, double Lon, double Lat, double Km, double Deaths);

/// <summary>An earthquake belt: chance quakes strike regions within it.</summary>
public sealed record SeismicZone(string Name, double Lon, double Lat, double Km, double Odds);

/// <summary>
/// Plagues, famines and disasters (decision "Playable 28": history's own
/// plus chance), loaded from data/disasters.json.
/// </summary>
public sealed class DisasterCatalog
{
    public const string Path = "res://data/disasters.json";
    static DisasterCatalog? _instance;
    public static DisasterCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public List<Disaster> Events { get; } = new();
    public List<SeismicZone> Seismic { get; } = new();

    /// <summary>Yearly odds of a chance epidemic in a region (more where people crowd together).</summary>
    public const double EpidemicOdds = 0.004;
    /// <summary>Medicine's greatest cut in plague deaths.</summary>
    public const double MaxMedicine = 0.9;

    public static DisasterCatalog Parse(string json)
    {
        var c = new DisasterCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.GetProperty("events").EnumerateArray())
            c.Events.Add(new Disaster(e.GetProperty("year").GetInt32(), e.GetProperty("kind").GetString()!,
                e.GetProperty("name").GetString()!, e.GetProperty("lon").GetDouble(), e.GetProperty("lat").GetDouble(),
                e.GetProperty("km").GetDouble(), e.GetProperty("deaths").GetDouble()));
        foreach (var z in doc.RootElement.GetProperty("seismic").EnumerateArray())
            c.Seismic.Add(new SeismicZone(z.GetProperty("name").GetString()!, z.GetProperty("lon").GetDouble(),
                z.GetProperty("lat").GetDouble(), z.GetProperty("km").GetDouble(), z.GetProperty("odds").GetDouble()));
        return c;
    }

    /// <summary>Kilometres between two points (equirectangular: close enough at this scale).</summary>
    public static double Km(double lon1, double lat1, double lon2, double lat2)
    {
        double x = (lon2 - lon1) * Math.Cos((lat1 + lat2) * Math.PI / 360);
        double y = lat2 - lat1;
        return Math.Sqrt(x * x + y * y) * 111.2;
    }

    /// <summary>Share killed at this distance: full at the centre, fading to nothing at the edge.</summary>
    public static double DeathsAt(Disaster d, double km) =>
        km >= d.Km ? 0 : d.Deaths * (d.Km > 1000 ? 1 - 0.5 * km / d.Km : 1 - km / d.Km);

    /// <summary>Yearly odds of a chance earthquake at a place.</summary>
    public double QuakeOdds(double lon, double lat)
    {
        double odds = 0;
        foreach (var z in Seismic)
            if (Km(lon, lat, z.Lon, z.Lat) < z.Km)
                odds = Math.Max(odds, z.Odds);
        return odds;
    }

    /// <summary>How much medicine cuts plague deaths, from the realm's health techs.</summary>
    public static double MedicineCut(double healthTech) => Math.Clamp(healthTech * 1.5, 0, MaxMedicine);
}
