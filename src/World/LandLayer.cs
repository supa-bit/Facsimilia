using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Godot;

namespace Facsimilia.World;

/// <summary>One number baked for every node: see data/land/land.json.</summary>
public sealed record LandField(string Name, string Part, string Unit, double Min, double Max,
    string Description, bool Derived, string[] Sources);

/// <summary>A historical mine, quarry, salt work or special habitat (tools/land_sites.json).</summary>
public sealed record LandSite(string Name, string Kind, string[] Goods, double Lon, double Lat, double Strength,
    int Node, int? From, int? To)
{
    /// <summary>Whether the site was worked or existed in the given year.</summary>
    public bool ActiveIn(int year) => (From == null || year >= From) && (To == null || year <= To);
}

/// <summary>
/// The land layer: what every place on the map is - terrain, climate, soil,
/// water, living habitat, and known deposits and special resources - one
/// value per population node (the same 780 x 456 grid). Baked from real
/// data by tools/build_land_layer.py into data/land/; see MECHANICS.md,
/// "Resources and the land". Read-only here: the parts of it that change
/// over time (soil nutrients, forests, forage, fish) will be simulated
/// state layered on top of these starting values.
///
/// Fields load on first use, so the game only pays for the ones it reads.
/// </summary>
public sealed class LandLayer
{
    public const string MetaPath = "res://data/land/land.json";
    const string Dir = "res://data/land/";

    public int Width { get; private set; }
    public int Height { get; private set; }
    public IReadOnlyList<LandField> Fields => _fields;
    public IReadOnlyList<LandSite> Sites => _sites;

    readonly List<LandField> _fields = new();
    readonly Dictionary<string, LandField> _byName = new();
    readonly Dictionary<string, byte[]> _bytes = new();
    readonly List<LandSite> _sites = new();

    public static bool Available() => Godot.FileAccess.FileExists(MetaPath);

    /// <summary>Reads data/land/land.json; null if it's missing or invalid.</summary>
    public static LandLayer? Load()
    {
        if (!Available())
            return null;
        Meta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<Meta>(Godot.FileAccess.GetFileAsString(MetaPath));
        }
        catch (JsonException)
        {
            meta = null;
        }
        if (meta?.fields == null)
        {
            GD.PushError($"LandLayer: {MetaPath} is not valid JSON");
            return null;
        }
        var layer = new LandLayer { Width = meta.width, Height = meta.height };
        foreach (var f in meta.fields)
        {
            var field = new LandField(f.name, f.part, f.unit, f.min, f.max, f.description, f.derived, f.sources);
            layer._fields.Add(field);
            layer._byName[f.name] = field;
        }
        foreach (var s in meta.sites ?? new List<SiteEntry>())
            layer._sites.Add(new LandSite(s.name, s.kind, s.goods, s.lon, s.lat, s.strength, s.node, s.from, s.to));
        return layer;
    }

    sealed record Meta(int width, int height, List<FieldEntry> fields, List<SiteEntry>? sites);
    sealed record FieldEntry(string name, string part, string unit, double min, double max, string description,
        bool derived, string[] sources);
    sealed record SiteEntry(string name, string kind, string[] goods, double lon, double lat, double strength, int node,
        int? from, int? to);

    public bool Has(string name) => _byName.ContainsKey(name);

    public LandField Field(string name) =>
        _byName.TryGetValue(name, out var f) ? f : throw new KeyNotFoundException($"no land field '{name}'");

    /// <summary>A field's raw bytes, one per node: 0 = the field's minimum, 255 = its maximum (sea is 0).</summary>
    public byte[] Bytes(string name)
    {
        if (_bytes.TryGetValue(name, out var cached))
            return cached;
        Field(name);  // throws for an unknown name
        byte[] gz = Godot.FileAccess.GetFileAsBytes(Dir + name + ".u8.gz");
        using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var output = new MemoryStream(Width * Height);
        input.CopyTo(output);
        byte[] bytes = output.ToArray();
        if (bytes.Length != Width * Height)
            throw new InvalidDataException($"land field {name} has {bytes.Length} bytes, expected {Width * Height}");
        _bytes[name] = bytes;
        return bytes;
    }

    /// <summary>A field's value at a node, in its own unit.</summary>
    public double Value(string name, int node)
    {
        var f = Field(name);
        return f.Min + Bytes(name)[node] / 255.0 * (f.Max - f.Min);
    }

    /// <summary>The node at a longitude/latitude (clamped to the map).</summary>
    public int NodeAt(double lon, double lat)
    {
        int x = Math.Clamp((int)((lon - MapView.LonMin) / (MapView.LonMax - MapView.LonMin) * Width), 0, Width - 1);
        int y = Math.Clamp((int)((MapView.LatMax - lat) / (MapView.LatMax - MapView.LatMin) * Height), 0, Height - 1);
        return y * Width + x;
    }

    /// <summary>Formats a value with its unit for display ("640 mm/year", "35%").</summary>
    public static string Format(LandField f, double v) => f.Unit switch
    {
        "share" or "share of woodland" or "chance/year" => $"{v * 100:0}%",
        "index" or "class" => $"{v:0.00}",
        "-1..1" => v >= 0.15 ? "faces south" : v <= -0.15 ? "faces north" : "mixed",
        "°C" => $"{v:0.0} °C",
        "pH" => $"pH {v:0.0}",
        _ => $"{v:#,0.#} {f.Unit}",
    };
}
