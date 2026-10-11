using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>A kind of road, by its material (data/roads.json).</summary>
public sealed class RoadKind
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The share of the ground's marching cost left on this road (lower is faster).</summary>
    public double Pace { get; init; }
    public double Trade { get; init; }
    public double Reach { get; init; }
    /// <summary>Talents per km.</summary>
    public double Cost { get; init; }
    /// <summary>Men per km taken from manpower while it is built.</summary>
    public double Workers { get; init; }
    public string? Requires { get; init; }
    public string Description { get; init; } = "";
    /// <summary>Its place in the list: higher is better.</summary>
    public int Rank { get; init; }
    /// <summary>A canal: dug between two waters for boats, not a road.</summary>
    public bool Water { get; init; }
    /// <summary>A frontier wall: slows armies crossing it and needs a garrison.</summary>
    public bool Wall { get; init; }
    /// <summary>For walls: men a km held from manpower once it is finished.</summary>
    public double Garrison { get; init; }
    /// <summary>For walls: km of march an army loses crossing it.</summary>
    public double CrossKm { get; init; }
    /// <summary>For walls: garrison Might added to provinces it runs through.</summary>
    public double ProvinceGarrison { get; init; }
    /// <summary>For canals: the roughest ground it can be dug through.</summary>
    public double MaxRugged { get; init; } = 1000;
}

/// <summary>A road on the map: a chain of nodes, built or being built, by a realm.</summary>
public sealed class Road
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public int Realm { get; set; }
    public string Name { get; set; } = "";
    public List<int> Nodes { get; } = new();
    /// <summary>Years of work left (0: built).</summary>
    public int Left { get; set; }
    /// <summary>For walls: whether its garrison is at its post (taken from manpower when finished).</summary>
    public bool Manned { get; set; }
    /// <summary>Broken by its owner to slow an invader.</summary>
    public bool Broken { get; set; }
    /// <summary>Years in a row its upkeep went unpaid.</summary>
    public int Neglect { get; set; }
    public double Workers { get; set; }
    /// <summary>History's road it follows ("via_appia:2" for its third leg), or "".</summary>
    public string History { get; set; } = "";
    public double Km { get; set; }

    public bool Built => Left == 0;

    public GDictionary ToDict() => new()
    {
        ["id"] = Id, ["kind"] = Kind, ["realm"] = Realm, ["name"] = Name, ["nodes"] = Nodes.ToArray(), ["left"] = Left,
        ["broken"] = Broken, ["manned"] = Manned, ["neglect"] = Neglect, ["workers"] = Workers, ["history"] = History, ["km"] = Km,
    };

    public static Road FromDict(GDictionary d)
    {
        var r = new Road
        {
            Id = d["id"].AsInt32(), Kind = d["kind"].AsString(), Realm = d["realm"].AsInt32(), Name = d["name"].AsString(),
            Left = d["left"].AsInt32(), Broken = d["broken"].AsBool(), Neglect = d["neglect"].AsInt32(), Workers = d["workers"].AsDouble(),
            History = d["history"].AsString(), Km = d["km"].AsDouble(), Manned = d.TryGetValue("manned", out var m) && m.AsBool(),
        };
        r.Nodes.AddRange(d["nodes"].AsInt32Array());
        return r;
    }
}

/// <summary>One of history's roads: a chain of real places, its builder and year (data/roads_history.json).</summary>
public sealed record HistoryRoad(string Id, string Name, string Kind, string? Builder, int Year, (string Name, double Lon, double Lat)[] Points,
    string Material = "", int? Fall = null, string FallWhy = "");

/// <summary>The kinds of road and history's roads.</summary>
public sealed class RoadCatalog
{
    public const string Path = "res://data/roads.json", HistoryPath = "res://data/roads_history.json";
    static RoadCatalog? _instance;
    public static RoadCatalog Instance => _instance ??= Load();

    public List<RoadKind> Kinds { get; } = new();
    public List<RoadKind> Canals { get; } = new();
    public List<RoadKind> Walls { get; } = new();
    public List<HistoryRoad> History { get; } = new();
    public double KmPerYear { get; private set; } = 100;
    public double BridgeCost { get; private set; } = 6;
    public double UpkeepShare { get; private set; } = 0.01;

    public RoadKind? this[string id] => Kinds.FirstOrDefault(k => k.Id == id) ?? Canals.FirstOrDefault(k => k.Id == id) ?? Walls.FirstOrDefault(k => k.Id == id);

    /// <summary>The kinds of road a realm can build now.</summary>
    public IEnumerable<RoadKind> Open(RealmState r) => Kinds.Where(k => k.Requires == null || r.Techs.Contains(k.Requires));

    /// <summary>The kinds of canal a realm can dig now.</summary>
    public IEnumerable<RoadKind> OpenCanals(RealmState r) => Canals.Where(k => k.Requires == null || r.Techs.Contains(k.Requires));

    /// <summary>The material history's kinds map to: Roman viae were paved, others gravel; walls by their own; railways wait for the railway network.</summary>
    public static string KindOfHistory(HistoryRoad h) => h.Kind switch { "via" => "paved", "road" => "gravel", "wall" => h.Material, _ => "" };

    static RoadCatalog Load()
    {
        var c = new RoadCatalog();
        using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Path)))
        {
            var root = doc.RootElement;
            c.KmPerYear = root.GetProperty("km_per_year").GetDouble();
            c.BridgeCost = root.GetProperty("bridge_cost").GetDouble();
            c.UpkeepShare = root.GetProperty("upkeep_share").GetDouble();
            int rank = 0;
            foreach (var list in new[] { "kinds", "canals", "walls" })
                if (root.TryGetProperty(list, out var arr))
                    foreach (var k in arr.EnumerateArray())
                        (list == "kinds" ? c.Kinds : list == "canals" ? c.Canals : c.Walls).Add(new RoadKind
                {
                    Water = list == "canals",
                    Wall = list == "walls",
                    Garrison = k.TryGetProperty("garrison", out var gr) ? gr.GetDouble() : 0,
                    CrossKm = k.TryGetProperty("cross_km", out var ck) ? ck.GetDouble() : 0,
                    ProvinceGarrison = k.TryGetProperty("province_garrison", out var pg) ? pg.GetDouble() : 0,
                    MaxRugged = k.TryGetProperty("max_rugged", out var mr) ? mr.GetDouble() : 1000,
                    Id = k.GetProperty("id").GetString()!,
                    Name = k.GetProperty("name").GetString()!,
                    Pace = k.GetProperty("pace").GetDouble(),
                    Trade = k.GetProperty("trade").GetDouble(),
                    Reach = k.GetProperty("reach").GetDouble(),
                    Cost = k.GetProperty("cost").GetDouble(),
                    Workers = k.GetProperty("workers").GetDouble(),
                    Requires = k.GetProperty("requires").ValueKind == JsonValueKind.Null ? null : k.GetProperty("requires").GetString(),
                    Description = k.GetProperty("description").GetString()!,
                    Rank = list == "kinds" ? rank++ : 0,
                });
        }
        if (Godot.FileAccess.FileExists(HistoryPath))
            using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(HistoryPath)))
                foreach (var r in doc.RootElement.GetProperty("roads").EnumerateArray())
                    c.History.Add(new HistoryRoad(r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!,
                        r.GetProperty("kind").GetString()!,
                        r.GetProperty("builder").ValueKind == JsonValueKind.Null ? null : r.GetProperty("builder").GetString(),
                        r.GetProperty("year").GetInt32(),
                        r.GetProperty("points").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetDouble(), p[2].GetDouble())).ToArray()));
        // History's frontier walls (data/works_history.json), laid as roads are.
        if (Godot.FileAccess.FileExists(WonderCatalog.WorksPath))
            using (var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(WonderCatalog.WorksPath)))
                foreach (var r in doc.RootElement.GetProperty("works").EnumerateArray())
                    if (r.GetProperty("kind").GetString() == "wall")
                        c.History.Add(new HistoryRoad(r.GetProperty("id").GetString()!, r.GetProperty("name").GetString()!, "wall",
                            r.GetProperty("builder").ValueKind == JsonValueKind.Null ? null : r.GetProperty("builder").GetString(),
                            r.GetProperty("start").GetInt32(),
                            r.GetProperty("points").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetDouble(), p[2].GetDouble())).ToArray(),
                            r.GetProperty("material").GetString()!,
                            r.TryGetProperty("fall", out var f) ? f.GetInt32() : null,
                            r.TryGetProperty("fall_why", out var fw) ? fw.GetString()! : ""));
        return c;
    }
}
