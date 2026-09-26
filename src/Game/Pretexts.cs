using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// A reason for war (decision "Playable 15": declare on anyone, but
/// pretexts matter; inspired by Stellaris' casus belli, not a copy). Each
/// has its own cost in aggression - how much the world fears you - on
/// declaring and for each province taken, and some allow a vassal as the
/// prize.
/// </summary>
public sealed record Pretext(string Id, string Name, string Description, double AggressionOnDeclare,
    double AggressionPerProvince, bool AllowsVassal);

public static class Pretexts
{
    public static readonly Pretext Border = new("border", "Border dispute",
        "You share a border: there is always a quarrel over fields, rivers and grazing.", 5, 4, false);
    public static readonly Pretext Reconquest = new("reconquest", "Reconquest",
        "They hold land that was yours within living memory.", 0, 1, false);
    public static readonly Pretext Kin = new("kin", "Free our people",
        "They rule a province of your own people.", 3, 2, false);
    public static readonly Pretext History = new("history", "Historical claim",
        "History's own war: your realm fought this one in these very years.", 2, 2, false);
    public static readonly Pretext Subjugation = new("subjugation", "Subjugation",
        "A far weaker neighbour, fit to be a client: the prize may be their submission as your vassal.", 8, 4, true);
    public static readonly Pretext Faith = new("faith", "War of faith",
        "Neighbours who worship other gods.", 6, 4, false);
    public static readonly Pretext None = new("none", "No just cause",
        "Naked aggression: the world will remember it, and their friends will come.", 25, 8, false);

    public static readonly Pretext[] All = { History, Reconquest, Kin, Border, Subjugation, Faith, None };

    public static Pretext ById(string id) => All.FirstOrDefault(p => p.Id == id) ?? None;

    /// <summary>Aggression fades this much a year.</summary>
    public const double AggressionDecay = 2;
    /// <summary>At or above this, the realms that fear a conqueror band together against it.</summary>
    public const double CoalitionAggression = 50;
}

/// <summary>
/// What a peace asks (decision "Playable 16": terms by war score). Each
/// term costs war score; the loser accepts terms it has lost enough - or is
/// tired enough - to pay for.
/// </summary>
public sealed class PeaceTerms
{
    public List<int> Provinces { get; } = new();       // the loser's provinces to hand over
    public bool Tribute { get; set; }
    public bool Vassal { get; set; }

    public const double TributeCost = 20, VassalCost = 60, ProvinceMin = 8, ProvinceMax = 40;

    public bool IsWhitePeace => Provinces.Count == 0 && !Tribute && !Vassal;
}

/// <summary>An offer another realm makes the player, answered in the Diplomacy panel.</summary>
public sealed class Offer
{
    public string Kind { get; init; } = "";          // "peace", "ransom", "alliance", "vassal"
    public int From { get; init; }
    public double Silver { get; init; }              // talents paid to the player on acceptance
    public List<int> Provinces { get; } = new();     // provinces ceded to the player
    public int SiegeProvince { get; init; }          // for a ransom: the siege to lift
    public string Text { get; init; } = "";
    public int Year { get; init; }

    public GDictionary ToDict() => new()
    {
        ["kind"] = Kind, ["from"] = From, ["silver"] = Silver, ["provinces"] = Provinces.ToArray(),
        ["siege"] = SiegeProvince, ["text"] = Text, ["year"] = Year,
    };

    public static Offer FromDict(GDictionary d)
    {
        var o = new Offer
        {
            Kind = d["kind"].AsString(), From = d["from"].AsInt32(), Silver = d["silver"].AsDouble(),
            SiegeProvince = d["siege"].AsInt32(), Text = d["text"].AsString(), Year = d["year"].AsInt32(),
        };
        o.Provinces.AddRange(d["provinces"].AsInt32Array());
        return o;
    }
}
