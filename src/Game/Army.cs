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
    /// <summary>
    /// Each unit type's experience, 0 raw recruits .. 1 hardened veterans
    /// (decision "Next 9", your answer: each unit keeps its own). Battles
    /// harden the units that fought; new recruits dilute only their own kind.
    /// </summary>
    public double[] UnitXp { get; }
    /// <summary>The army's experience: its units' experience weighted by their numbers. Setting it sets every unit's.</summary>
    public double Experience
    {
        get
        {
            int n = Count;
            if (n == 0)
                return 0;
            double s = 0;
            for (int i = 0; i < Units.Length; i++)
                s += Units[i] * UnitXp[i];
            return s / n;
        }
        set
        {
            for (int i = 0; i < UnitXp.Length; i++)
                UnitXp[i] = Math.Clamp(value, 0, 1);
        }
    }

    /// <summary>Hardens every unit present by an amount (after a battle).</summary>
    public void Harden(double amount)
    {
        for (int i = 0; i < Units.Length; i++)
            if (Units[i] > 0)
                UnitXp[i] = Math.Min(1, UnitXp[i] + amount);
    }

    /// <summary>Adds raw units of a type, diluting that type's experience.</summary>
    public void AddRaw(int index, int count = 1)
    {
        Units[index] += count;
        UnitXp[index] *= (Units[index] - count) / (double)Math.Max(Units[index], 1);
    }

    /// <summary>Moves units (with their experience) into another army.</summary>
    public void MoveUnits(Army to, int index, int count)
    {
        count = Math.Min(count, Units[index]);
        if (count <= 0)
            return;
        int before = to.Units[index];
        to.UnitXp[index] = (to.UnitXp[index] * before + UnitXp[index] * count) / (before + count);
        to.Units[index] += count;
        Units[index] -= count;
    }
    /// <summary>The mercenary company this army is (GameState.Companies id), or 0 for the realm's own troops.</summary>
    public int CompanyId { get; set; }
    /// <summary>Victories not yet rewarded (a mercenary company's bonus is added at the month's pay).</summary>
    public int VictoriesUnpaid { get; set; }
    /// <summary>
    /// How attached the army is to its general, 0..1 (decision 'Armies loyal to
    /// their general'): it grows with the years he leads it.
    /// </summary>
    public double GeneralLoyalty { get; set; }
    /// <summary>The army's standard (an emblem id), chosen by you.</summary>
    public string Standard { get; set; } = "";
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

    public Army(int unitCount)
    {
        Units = new int[unitCount];
        UnitXp = new double[unitCount];
    }

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
        var xp = new GDictionary();
        for (int i = 0; i < Units.Length; i++)
            if (Units[i] > 0)
            {
                units[cat[i].Id] = Units[i];
                xp[cat[i].Id] = UnitXp[i];
            }
        return new GDictionary { ["id"] = Id, ["name"] = Name, ["node"] = Node, ["fatigue"] = Fatigue, ["units"] = units, ["unit_xp"] = xp, ["general"] = GeneralId,
            ["company"] = CompanyId, ["victories_unpaid"] = VictoriesUnpaid, ["gen_loyalty"] = GeneralLoyalty, ["standard"] = Standard,
            ["route"] = new GArray(Route.Select(n => (Variant)n).ToArray()), ["carry"] = MarchCarry, ["supply"] = Supply };
    }

    public static Army FromDict(GDictionary d, UnitCatalog cat)
    {
        var a = new Army(cat.Count)
        {
            Id = d["id"].AsInt32(), Name = d["name"].AsString(), Node = d["node"].AsInt32(),
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
            GeneralId = d.TryGetValue("general", out var g) ? g.AsInt32() : 0,
            MarchCarry = d.TryGetValue("carry", out var mc) ? mc.AsDouble() : 0,
            Supply = d.TryGetValue("supply", out var sp) ? sp.AsDouble() : 4,
            CompanyId = d.TryGetValue("company", out var co) ? co.AsInt32() : 0,
            VictoriesUnpaid = d.TryGetValue("victories_unpaid", out var vu) ? vu.AsInt32() : 0,
            GeneralLoyalty = d.TryGetValue("gen_loyalty", out var gl) ? gl.AsDouble() : 0,
            Standard = d.TryGetValue("standard", out var st) ? st.AsString() : "",
        };
        if (d.TryGetValue("xp", out var x))
            a.Experience = x.AsDouble();   // a save from before each unit kept its own
        if (d.TryGetValue("route", out var route))
            foreach (var n in route.AsGodotArray())
                a.Route.Add(n.AsInt32());
        foreach (var (k, v) in d["units"].AsGodotDictionary())
            if (cat.Has(k.AsString()))
                a.Units[cat[k.AsString()].Index] = v.AsInt32();
        if (d.TryGetValue("unit_xp", out var ux))
            foreach (var (k, v) in ux.AsGodotDictionary())
                if (cat.Has(k.AsString()))
                    a.UnitXp[cat[k.AsString()].Index] = v.AsDouble();
        return a;
    }
}
