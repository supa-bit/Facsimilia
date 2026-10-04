using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;

namespace Facsimilia.Game;

/// <summary>A wonder of the world, from data/wonders.json.</summary>
public sealed class WonderDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Lon { get; init; }
    public double Lat { get; init; }
    /// <summary>The civilization that built it in history, or null (no realm of the game did).</summary>
    public string? Builder { get; init; }
    public int Start { get; init; }
    public int Done { get; init; }
    /// <summary>The year it fell in history, or null if it stands.</summary>
    public int? Fall { get; init; }
    public string FallWhy { get; init; } = "";
    public double Cost { get; init; }
    public double Workers { get; init; }
    public string[] Goods { get; init; } = Array.Empty<string>();
    public Dictionary<string, double> Effects { get; init; } = new();
    public double Culture { get; init; }
    public string? Needs { get; init; }
    public string Description { get; init; } = "";
    /// <summary>The years history took (at least one).</summary>
    public int Years => Math.Max(1, Done - Start);
}

/// <summary>A wonder under way: who builds it, where, and how many years are left.</summary>
public sealed class WonderWork
{
    public string Id { get; set; } = "";
    public int Realm { get; set; }
    public int Province { get; set; }
    public int Left { get; set; }
    /// <summary>The workers taken from the realm's manpower, given back when it is done or abandoned.</summary>
    public double Workers { get; set; }
    public double Paid { get; set; }

    public GArray ToArray() => new() { Id, Realm, Province, Left, Workers, Paid };
    public static WonderWork FromArray(GArray a) => new()
    { Id = a[0].AsString(), Realm = a[1].AsInt32(), Province = a[2].AsInt32(), Left = a[3].AsInt32(), Workers = a[4].AsDouble(), Paid = a[5].AsDouble() };
}

/// <summary>A wonder standing in the world: where, and whether its fall was averted.</summary>
public sealed class WonderSite
{
    public int Province { get; set; }
    public int Year { get; set; }
    public bool Saved { get; set; }

    public GArray ToArray() => new() { Province, Year, Saved };
    public static WonderSite FromArray(GArray a) => new() { Province = a[0].AsInt32(), Year = a[1].AsInt32(), Saved = a[2].AsBool() };
}

/// <summary>The wonders of the world (the Ledger topic "Buildings and works": wonders).</summary>
public sealed class WonderCatalog
{
    public const string Path = "res://data/wonders.json";
    static WonderCatalog? _instance;
    public static WonderCatalog Instance => _instance ??= Load();

    public List<WonderDef> All { get; } = new();
    public WonderDef? this[string id] => All.FirstOrDefault(w => w.Id == id);

    /// <summary>Saving a wonder from its fall costs this share of its price.</summary>
    public const double SaveShare = 0.5;
    /// <summary>Workers lost to toil and accident while a wonder is built.</summary>
    public const double WorkersLost = 0.05;
    /// <summary>A wonder abandoned (another finished it first) gives back this share of the silver paid.</summary>
    public const double Refund = 0.5;

    static WonderCatalog Load()
    {
        var c = new WonderCatalog();
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Path));
        foreach (var w in doc.RootElement.GetProperty("wonders").EnumerateArray())
            c.All.Add(new WonderDef
            {
                Id = w.GetProperty("id").GetString()!,
                Name = w.GetProperty("name").GetString()!,
                Lon = w.GetProperty("lon").GetDouble(),
                Lat = w.GetProperty("lat").GetDouble(),
                Builder = w.GetProperty("builder").ValueKind == JsonValueKind.Null ? null : w.GetProperty("builder").GetString(),
                Start = w.GetProperty("start").GetInt32(),
                Done = w.GetProperty("done").GetInt32(),
                Fall = w.TryGetProperty("fall", out var f) ? f.GetInt32() : null,
                FallWhy = w.TryGetProperty("fall_why", out var fw) ? fw.GetString()! : "",
                Cost = w.GetProperty("cost").GetDouble(),
                Workers = w.GetProperty("workers").GetDouble(),
                Goods = w.GetProperty("goods").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                Effects = w.GetProperty("effects").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()),
                Culture = w.GetProperty("culture").GetDouble(),
                Needs = w.TryGetProperty("needs", out var n) ? n.GetString() : null,
                Description = w.GetProperty("description").GetString()!,
            });
        return c;
    }
}
