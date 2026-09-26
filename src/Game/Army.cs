using System;
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
        return new GDictionary { ["id"] = Id, ["name"] = Name, ["node"] = Node, ["fatigue"] = Fatigue, ["units"] = units };
    }

    public static Army FromDict(GDictionary d, UnitCatalog cat)
    {
        var a = new Army(cat.Count)
        {
            Id = d["id"].AsInt32(), Name = d["name"].AsString(), Node = d["node"].AsInt32(),
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
        };
        foreach (var (k, v) in d["units"].AsGodotDictionary())
            if (cat.Has(k.AsString()))
                a.Units[cat[k.AsString()].Index] = v.AsInt32();
        return a;
    }
}
