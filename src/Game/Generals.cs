using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>A general's skills, each 1-10 (5 an ordinary commander).</summary>
public static class Skills
{
    public const int Tactics = 0, Leadership = 1, Siegecraft = 2, Logistics = 3, Seamanship = 4;
    public const int Count = 5;
    public static readonly string[] Keys = { "tactics", "leadership", "siegecraft", "logistics", "seamanship" };
    public static readonly string[] Names = { "Tactics", "Leadership", "Siegecraft", "Logistics", "Seamanship" };
    public static readonly string[] Help =
    {
        "winning the day: each point above the enemy's general is worth about 7% in battle",
        "holding the men together: fewer losses",
        "taking walls: faster sieges",
        "marching and supply: less sickness and weariness",
        "handling fleets",
    };
}

/// <summary>A perk (data/generals.json): what it adds to skills and to strength in battle.</summary>
public sealed record Perk(string Id, string Name, string Description, bool Great, IReadOnlyDictionary<string, double> Effects);

public sealed class PerkCatalog
{
    public const string DataPath = "res://data/generals.json";
    static PerkCatalog? _instance;
    public static PerkCatalog Instance => _instance ??= Load();

    public IReadOnlyList<Perk> All { get; }
    readonly Dictionary<string, Perk> _byId;

    PerkCatalog(List<Perk> perks)
    {
        All = perks;
        _byId = perks.ToDictionary(p => p.Id);
    }

    public Perk? this[string id] => _byId.GetValueOrDefault(id);

    static PerkCatalog Load()
    {
        var perks = new List<Perk>();
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(DataPath));
        foreach (var p in doc.RootElement.GetProperty("perks").EnumerateArray())
        {
            var effects = new Dictionary<string, double>();
            foreach (var e in p.GetProperty("effects").EnumerateObject())
                effects[e.Name] = e.Value.GetDouble();
            perks.Add(new Perk(p.GetProperty("id").GetString()!, p.GetProperty("name").GetString()!,
                p.GetProperty("description").GetString()!,
                p.TryGetProperty("great", out var g) && g.GetBoolean(), effects));
        }
        return new PerkCatalog(perks);
    }
}

/// <summary>
/// A commander (decision "Next 2"): a member of the ruling family, or a man
/// appointed from the court or the ranks, sometimes one of the great
/// captains of the age. Leads one army; wins, loses, ages and dies.
/// </summary>
public sealed class General
{
    public const string Family = "family", Appointed = "appointed", Great = "great";

    public int Id { get; init; }
    public string Name { get; set; } = "";
    /// <summary>The family member (CharacterRegistry id), or -1 for an appointed man.</summary>
    public int CharacterId { get; set; } = -1;
    public int BornYear { get; set; }
    public string Origin { get; set; } = Appointed;
    public int[] BaseSkills { get; } = new int[Skills.Count];
    public List<string> Perks { get; } = new();
    public int Battles { get; set; }
    public int Victories { get; set; }
    /// <summary>The army he leads, or 0.</summary>
    public int ArmyId { get; set; }

    /// <summary>A skill with its perks, 1-12.</summary>
    public int Skill(int s) => Math.Clamp(BaseSkills[s] + (int)Effect(Skills.Keys[s]), 1, 12);

    /// <summary>The sum of his perks' effect of one kind.</summary>
    public double Effect(string key)
    {
        double e = 0;
        foreach (var id in Perks)
            if (PerkCatalog.Instance[id] is { } p && p.Effects.TryGetValue(key, out var v))
                e += v;
        return e;
    }

    /// <summary>A one-line summary: skills and perks.</summary>
    public string Summary() =>
        $"tactics {Skill(Skills.Tactics)}, leadership {Skill(Skills.Leadership)}, siegecraft {Skill(Skills.Siegecraft)}, " +
        $"logistics {Skill(Skills.Logistics)}, seamanship {Skill(Skills.Seamanship)}" +
        (Perks.Count > 0 ? "; " + string.Join(", ", Perks.Select(p => PerkCatalog.Instance[p]?.Name ?? p)) : "");

    /// <summary>
    /// A new commander. A great captain has high skills and one great perk;
    /// others have ordinary skills and a perk or two, and the young start untried.
    /// </summary>
    public static General Make(int id, string name, string origin, int bornYear, int characterId, Random rng, int year)
    {
        var g = new General { Id = id, Name = name, Origin = origin, BornYear = bornYear, CharacterId = characterId };
        bool great = origin == Great;
        for (int s = 0; s < Skills.Count; s++)
            g.BaseSkills[s] = Math.Clamp((great ? 6 : 3) + rng.Next(0, great ? 4 : 5), 1, 10);
        var cat = PerkCatalog.Instance.All;
        if (great)
        {
            var greats = cat.Where(p => p.Great).ToList();
            g.Perks.Add(greats[rng.Next(greats.Count)].Id);
        }
        int age = year - bornYear;
        if (age < 25 && !great)
            g.Perks.Add("green");
        var ordinary = cat.Where(p => !p.Great && p.Id != "green").ToList();
        int n = great ? 1 : rng.NextDouble() < 0.5 ? 1 : 2;
        for (int k = 0; k < n; k++)
        {
            var p = ordinary[rng.Next(ordinary.Count)];
            if (!g.Perks.Contains(p.Id) && !(p.Id == "cautious" && g.Perks.Contains("reckless")) && !(p.Id == "reckless" && g.Perks.Contains("cautious")))
                g.Perks.Add(p.Id);
        }
        return g;
    }

    public GDictionary ToDict() => new()
    {
        ["id"] = Id, ["name"] = Name, ["character"] = CharacterId, ["born"] = BornYear, ["origin"] = Origin,
        ["skills"] = new GArray(BaseSkills.Select(s => (Variant)s).ToArray()),
        ["perks"] = new GArray(Perks.Select(p => (Variant)p).ToArray()),
        ["battles"] = Battles, ["victories"] = Victories, ["army"] = ArmyId,
    };

    public static General FromDict(GDictionary d)
    {
        var g = new General
        {
            Id = d["id"].AsInt32(), Name = d["name"].AsString(), CharacterId = d["character"].AsInt32(),
            BornYear = d["born"].AsInt32(), Origin = d["origin"].AsString(),
            Battles = d["battles"].AsInt32(), Victories = d["victories"].AsInt32(), ArmyId = d["army"].AsInt32(),
        };
        var skills = d["skills"].AsGodotArray();
        for (int s = 0; s < Math.Min(skills.Count, Skills.Count); s++)
            g.BaseSkills[s] = skills[s].AsInt32();
        foreach (var p in d["perks"].AsGodotArray())
            g.Perks.Add(p.AsString());
        return g;
    }
}
