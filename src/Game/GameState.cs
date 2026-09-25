using System;
using System.Collections.Generic;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>How hard a realm taxes (decision "Playable 5": one rate the ruler sets).</summary>
public enum TaxRate { Low = 0, Normal = 1, Heavy = 2, Crushing = 3 }

/// <summary>
/// Everything a realm has beyond its land, people and ruling family: its
/// treasury, taxes, debt and army. Money is silver by weight, in talents
/// (decision "Playable 4"): 1 talent = 6,000 drachmae = about 26 kg.
/// </summary>
public sealed class RealmState
{
    public int RealmId { get; }
    public double Treasury { get; set; }   // talents
    public double Debt { get; set; }       // talents owed to temples and bankers
    public TaxRate Tax { get; set; } = TaxRate.Normal;
    public int[] Units { get; } = new int[UnitTypes.Count];   // how many of each (see UnitTypes)
    public double Manpower { get; set; }   // men available to recruit
    public double Fatigue { get; set; }    // 0 rested .. 1 exhausted: campaigning wears armies down
    public bool ElephantSource { get; set; }   // can raise elephants without elephant country (Seleucid India trade)

    // Last year's accounts, for the treasury panel.
    public double LastTax { get; set; }
    public double LastTribute { get; set; }
    public double LastAdmin { get; set; }
    public double LastUpkeep { get; set; }
    public double LastInterest { get; set; }
    public double LastNet => LastTax + LastTribute - LastAdmin - LastUpkeep - LastInterest;

    public RealmState(int realmId) => RealmId = realmId;

    public GDictionary ToDict()
    {
        var units = new GArray();
        foreach (int u in Units)
            units.Add(u);
        return new GDictionary
        {
            ["realm"] = RealmId, ["treasury"] = Treasury, ["debt"] = Debt, ["tax"] = (int)Tax,
            ["units"] = units, ["manpower"] = Manpower, ["fatigue"] = Fatigue, ["elephant_source"] = ElephantSource,
            ["last"] = new GArray { LastTax, LastTribute, LastAdmin, LastUpkeep, LastInterest },
        };
    }

    public static RealmState FromDict(GDictionary d)
    {
        var r = new RealmState(d["realm"].AsInt32())
        {
            Treasury = d["treasury"].AsDouble(),
            Debt = d["debt"].AsDouble(),
            Tax = (TaxRate)d["tax"].AsInt32(),
            Manpower = d.TryGetValue("manpower", out var m) ? m.AsDouble() : 0,
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
            ElephantSource = d.TryGetValue("elephant_source", out var e) && e.AsBool(),
        };
        if (d.TryGetValue("units", out var units))
        {
            var a = units.AsGodotArray();
            for (int i = 0; i < Math.Min(a.Count, r.Units.Length); i++)
                r.Units[i] = a[i].AsInt32();
        }
        if (d.TryGetValue("last", out var last))
        {
            var a = last.AsGodotArray();
            if (a.Count == 5)
            {
                r.LastTax = a[0].AsDouble(); r.LastTribute = a[1].AsDouble(); r.LastAdmin = a[2].AsDouble();
                r.LastUpkeep = a[3].AsDouble(); r.LastInterest = a[4].AsDouble();
            }
        }
        return r;
    }
}

/// <summary>
/// The game's own state on top of the map, people and families: every
/// realm's treasury and army, the turn length, and (later) wars and
/// treaties. Saved with the game.
/// </summary>
public sealed class GameState
{
    /// <summary>Turn lengths the player can pick (decision "Playable 2").</summary>
    public static readonly int[] TurnLengths = { 1, 5, 10, 25 };

    public Dictionary<int, RealmState> Realms { get; } = new();
    public int YearsPerTurn { get; set; } = 1;
    public Wars Wars { get; } = new();

    public RealmState Realm(int id)
    {
        if (!Realms.TryGetValue(id, out var r))
            Realms[id] = r = new RealmState(id);
        return r;
    }

    public GDictionary ToDict()
    {
        var realms = new GArray();
        foreach (var r in Realms.Values)
            realms.Add(r.ToDict());
        return new GDictionary { ["realms"] = realms, ["years_per_turn"] = YearsPerTurn, ["wars"] = Wars.ToArray() };
    }

    public static GameState FromDict(GDictionary d)
    {
        var g = new GameState();
        if (d.TryGetValue("realms", out var realms))
            foreach (Variant v in realms.AsGodotArray())
            {
                var r = RealmState.FromDict(v.AsGodotDictionary());
                g.Realms[r.RealmId] = r;
            }
        if (d.TryGetValue("years_per_turn", out var y) && Array.IndexOf(TurnLengths, y.AsInt32()) >= 0)
            g.YearsPerTurn = y.AsInt32();
        if (d.TryGetValue("wars", out var w))
            g.Wars.Load(w.AsGodotArray());
        return g;
    }
}
