using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The data files agree with each other (decision "Next 51": documented
/// data files, checked): every realm's people exists, every key used in
/// another file names a realm, every tech a unit, building or tech needs
/// exists, and routes and disasters lie on the map.
/// </summary>
public partial class DataTest : TestRunner
{
    static JsonElement Load(string file) => JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://data/" + file)).RootElement;

    protected override Task Run()
    {
        var realms = MapView.RealCivs.Select(c => c.Key).ToHashSet();
        Check(realms.Count == MapView.RealCivs.Length, "realm keys should be unique");
        var cultures = Load("cultures.json").GetProperty("cultures").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var c in MapView.RealCivs)
            Check(cultures.Contains(c.CultureKey), $"realm {c.Key}: culture '{c.CultureKey}' is not in cultures.json");
        void Keys(string file, IEnumerable<string> keys)
        {
            foreach (var k in keys)
                Check(realms.Contains(k), $"{file}: '{k}' is not a realm key in realms_bc300.json");
        }
        var start = Load("start_realms.json");
        Keys("start_realms.json", start.GetProperty("realms").EnumerateObject().Select(p => p.Name));
        foreach (var k in realms)
            Check(start.GetProperty("realms").TryGetProperty(k, out _), $"start_realms.json has no entry for {k}");
        Keys("start_realms.json alliances", start.GetProperty("alliances").EnumerateArray().SelectMany(a => a.EnumerateArray().Select(x => x.GetString()!)));
        Keys("start_realms.json rivals", start.GetProperty("rivals").EnumerateArray().SelectMany(a => a.EnumerateArray().Select(x => x.GetString()!)));
        Keys("goals.json", Load("goals.json").GetProperty("realms").EnumerateObject().Select(p => p.Name));
        Keys("coins.json", Load("coins.json").GetProperty("realms").EnumerateObject().Select(p => p.Name));
        foreach (var g in Load("history_goals.json").GetProperty("goals").EnumerateArray())
            Keys("history_goals.json", new[] { g.GetProperty("realm").GetString()!, g.GetProperty("target").GetString()! });

        var techs = TechCatalog.Instance.All.Select(t => t.Id).ToHashSet();
        foreach (var u in UnitCatalog.Instance.Units.Where(u => u.Requires != null))
            Check(techs.Contains(u.Requires!), $"unit {u.Id} needs tech '{u.Requires}', which doesn't exist");
        foreach (var t in TechCatalog.Instance.All)
            foreach (var r in t.Requires)
                Check(techs.Contains(r), $"tech {t.Id} needs '{r}', which doesn't exist");
        foreach (var b in Load("buildings.json").GetProperty("buildings").EnumerateArray())
            if (b.TryGetProperty("requires", out var req))
                Check(techs.Contains(req.GetString()!), $"building {b.GetProperty("id").GetString()} needs tech '{req.GetString()}'");

        bool OnMap(double lon, double lat) => lon is >= MapView.LonMin and <= MapView.LonMax && lat is >= MapView.LatMin and <= MapView.LatMax;
        foreach (var r in TradeRouteCatalog.Instance.Routes)
            foreach (var p in r.Points)
                Check(OnMap(p.Lon, p.Lat), $"trade route {r.Id}: {p.Name} is off the map");
        foreach (var d in DisasterCatalog.Instance.Events)
            Check(OnMap(d.Lon, d.Lat) && d.Deaths is > 0 and < 1, $"disaster {d.Name} is off the map or has a strange toll");
        foreach (var c in MapView.RealCivs)
            Check(c.CapitalLonLat is { } ll && OnMap(ll.X, ll.Y), $"realm {c.Key}: capital off the map");

        Finish($"Data tests passed: {realms.Count} realms, {cultures.Count} cultures, {techs.Count} techs, " +
            $"{TradeRouteCatalog.Instance.Routes.Count} trade routes and {DisasterCatalog.Instance.Events.Count} disasters agree with each other.");
        return Task.CompletedTask;
    }
}
