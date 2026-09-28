using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// A named army (decision "Playable 9": named armies based in provinces):
/// where it stands (a population node, inside a province or on open land),
/// and how many of each unit it has. Fleets are armies too: warships sail
/// with the army they belong to and carry it across the sea.
/// </summary>
public sealed class Army
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    /// <summary>The population node it stands on.</summary>
    public int Node { get; set; }
    /// <summary>Count of each unit type (index = UnitCatalog index).</summary>
    public int[] Units { get; private set; }
    /// <summary>0 rested .. 1 exhausted: campaigning and sieges wear an army down.</summary>
    public double Fatigue { get; set; }
    /// <summary>0 raw recruits .. 1 hardened veterans (decision "Next 9"): battles harden an army, new recruits dilute it.</summary>
    public double Experience { get; set; }
    /// <summary>Its general (RealmState.Generals id), or 0.</summary>
    public int GeneralId { get; set; }
    /// <summary>The nodes still to march through, in order (decision "Next 3": marching by months on plotted routes).</summary>
    public List<int> Route { get; } = new();
    /// <summary>Kilometres of march already made toward the next node.</summary>
    public double MarchCarry { get; set; }
    /// <summary>Months of food in the baggage train (decision "Next 4": supply lines and baggage).</summary>
    public double Supply { get; set; } = 4;
    public bool Marching => Route.Count > 0;

    /// <summary>What the realm's technology adds to each role's Might (set each year; not saved).</summary>
    public double[] RoleBoost { get; } = new double[UnitRoles.Count];

    /// <summary>Not besieging this year: rests faster.</summary>
    public bool Resting { get; set; } = true;

    public Army(int unitCount) => Units = new int[unitCount];

    public int Count => Units.Sum();
    public bool IsEmpty => Units.All(u => u == 0);

    public int RoleCount(UnitCatalog cat, int role)
    {
        int n = 0;
        for (int i = 0; i < Units.Length; i++)
            if (cat[i].Role == role)
                n += Units[i];
        return n;
    }

    public GDictionary ToDict(UnitCatalog cat)
    {
        var units = new GDictionary();
        for (int i = 0; i < Units.Length; i++)
            if (Units[i] > 0)
                units[cat[i].Id] = Units[i];
        return new GDictionary { ["id"] = Id, ["name"] = Name, ["node"] = Node, ["fatigue"] = Fatigue, ["units"] = units, ["xp"] = Experience, ["general"] = GeneralId,
            ["route"] = new GArray(Route.Select(n => (Variant)n).ToArray()), ["carry"] = MarchCarry, ["supply"] = Supply };
    }

    public static Army FromDict(GDictionary d, UnitCatalog cat)
    {
        var a = new Army(cat.Count)
        {
            Id = d["id"].AsInt32(), Name = d["name"].AsString(), Node = d["node"].AsInt32(),
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
            Experience = d.TryGetValue("xp", out var x) ? x.AsDouble() : 0,
            GeneralId = d.TryGetValue("general", out var g) ? g.AsInt32() : 0,
            MarchCarry = d.TryGetValue("carry", out var mc) ? mc.AsDouble() : 0,
            Supply = d.TryGetValue("supply", out var sp) ? sp.AsDouble() : 4,
        };
        if (d.TryGetValue("route", out var route))
            foreach (var n in route.AsGodotArray())
                a.Route.Add(n.AsInt32());
        foreach (var (k, v) in d["units"].AsGodotDictionary())
            if (cat.Has(k.AsString()))
                a.Units[cat[k.AsString()].Index] = v.AsInt32();
        return a;
    }
}
