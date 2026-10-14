using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Dynasties;

/// <summary>
/// The twelve skills every person has (decision "Which skills"), shown 1-100
/// with words (50 an ordinary grown man or woman) and used by the rules as
/// 1-10 (CharacterRegistry.Skill). The last five are the war skills, the
/// same as a general's (Game.Skills), so a family member who takes command
/// brings his own.
/// </summary>
public static class CharSkills
{
    public const int Statecraft = 0, Stewardship = 1, Diplomacy = 2, Intrigue = 3, Learning = 4, Piety = 5, Oratory = 6,
        Tactics = 7, Leadership = 8, Siegecraft = 9, Logistics = 10, Seamanship = 11;
    public const int Count = 12;
    /// <summary>Where the war skills start; war skill k is Game.Skills index k.</summary>
    public const int FirstWar = Tactics;

    public static readonly string[] Keys =
    {
        "statecraft", "stewardship", "diplomacy", "intrigue", "learning", "piety", "oratory",
        "tactics", "leadership", "siegecraft", "logistics", "seamanship",
    };
    public static readonly string[] Names =
    {
        "Statecraft", "Stewardship", "Diplomacy", "Intrigue", "Learning", "Piety", "Oratory",
        "Tactics", "Leadership", "Siegecraft", "Logistics", "Seamanship",
    };
    /// <summary>What each skill does in a ruler (the realm effects in RulerEffects).</summary>
    public static readonly string[] Help =
    {
        "governing: less corruption, better integration of new lands",
        "managing money: more taxes",
        "dealing with other realms",
        "plots, spies and secrets",
        "knowledge: faster research",
        "the gods: calmer people",
        "speaking to crowds and assemblies: calmer people",
        "winning battles",
        "holding men together: more men come to the standards",
        "taking walls: faster sieges",
        "marching and supply: farther reach",
        "handling fleets",
    };

    public static int IndexOf(string key) => Array.IndexOf(Keys, key);

    /// <summary>
    /// Words for each skill's level (decision "How skills are shown": creative
    /// words and 1-100), from hopeless to masterly, seven steps.
    /// </summary>
    static readonly string[][] Words =
    {
        new[] { "Misrule itself", "Muddled", "Ordinary magistrate", "Capable governor", "Shrewd statesman", "Born ruler", "A second Solon" },
        new[] { "Spendthrift", "Careless with coin", "Keeps the accounts", "Careful steward", "Thrifty manager", "Master of the treasury", "Golden-handed" },
        new[] { "Gives offence", "Clumsy envoy", "Civil", "Persuasive envoy", "Smooth diplomat", "Peacemaker of nations", "Charmer of kings" },
        new[] { "An open book", "Naive", "Discreet", "Watchful", "Spinner of webs", "Spymaster's equal", "The hidden hand" },
        new[] { "Unlettered", "Barely lettered", "Lettered", "Well read", "Learned", "Sage", "Another Aristotle" },
        new[] { "Godless", "Lax in the rites", "Dutiful worshipper", "Devout", "Holy", "Beloved of the gods", "Living oracle" },
        new[] { "Mumbling", "Halting speaker", "Plain speaker", "Persuasive", "Eloquent", "Silver-tongued", "Golden-tongued" },
        new[] { "Lambs to the slaughter", "Blunderer", "Steady", "Cunning in the field", "Skilled tactician", "Master of the field", "A new Alexander" },
        new[] { "Followed by no one", "Weak hand", "Respected", "Trusted captain", "Inspiring", "Beloved of the soldiers", "Men would die for him" },
        new[] { "Walls defeat him", "Slow besieger", "Patient besieger", "Able engineer", "Taker of cities", "Breaker of walls", "Poliorcetes, the city-taker" },
        new[] { "Starves his men", "Careless of supply", "Keeps them fed", "Careful quartermaster", "Swift marcher", "Master of the march", "Moves like the wind" },
        new[] { "Seasick", "Landsman", "Can steer", "Seaworthy", "Able captain", "Master of the sea", "Lord of the waves" },
    };

    /// <summary>The words for a skill at a level of 1-100.</summary>
    public static string Word(int skill, int points) =>
        Words[skill][Math.Clamp((points - 1) * Words[skill].Length / 100, 0, Words[skill].Length - 1)];
}

/// <summary>A trait (data/traits.json).</summary>
public sealed record TraitDef(string Id, string Name, string Kind, string Description,
    IReadOnlyDictionary<string, int> Skills, IReadOnlyDictionary<string, double> Effects,
    IReadOnlyList<string> Opposite, double Inherit, double Health, double Fertility, string? Group = null, int Opinion = 0)
{
    public const string Personality = "personality", Education = "education", HealthKind = "health",
        Body = "body", Lifestyle = "lifestyle", Earned = "earned",
        Faith = "faith", Bloodline = "bloodline", Reputation = "reputation";
    /// <summary>The groups of faith: one religion, at most one mystery cult and one school of philosophy.</summary>
    public const string Religion = "religion", Devotion = "devotion", Philosophy = "philosophy";
}

/// <summary>A bond between two named people (data/traits.json "bonds"): friend, rival, lover, mentor...</summary>
public sealed record BondDef(string Id, string Name, int Value, string Description);

/// <summary>A bond one person holds with another (both hold one, of the matching kind).</summary>
public sealed record Bond(string Kind, int With, int Since);

/// <summary>
/// Every trait a person can have (decision "Traits": at least 250 of six
/// kinds, partly inherited). Pure data, so the tests can load it without
/// the engine (Parse).
/// </summary>
public sealed class TraitCatalog
{
    public const string Path = "res://data/traits.json";
    static TraitCatalog? _instance;
    public static TraitCatalog Instance => _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public IReadOnlyList<TraitDef> All { get; }
    readonly Dictionary<string, TraitDef> _byId;

    TraitCatalog(List<TraitDef> all)
    {
        All = all;
        _byId = all.ToDictionary(t => t.Id);
    }

    public TraitDef? this[string id] => _byId.GetValueOrDefault(id);

    public IReadOnlyList<BondDef> Bonds { get; private init; } = new List<BondDef>();

    public BondDef? Bond(string id) => Bonds.FirstOrDefault(b => b.Id == id);

    readonly Dictionary<string, List<TraitDef>> _ofKind = new();

    public IReadOnlyList<TraitDef> OfKind(string kind)
    {
        if (!_ofKind.TryGetValue(kind, out var list))
            _ofKind[kind] = list = All.Where(t => t.Kind == kind).ToList();
        return list;
    }

    /// <summary>Traits of body and health a child can be born with.</summary>
    public IReadOnlyList<TraitDef> Inborn => _inborn ??= All.Where(t => t.Kind is TraitDef.Body or TraitDef.HealthKind && t.Inherit > 0).ToList();
    List<TraitDef>? _inborn;

    /// <summary>Whether two traits cannot be held together (brave and craven).</summary>
    public bool Opposed(string a, string b) =>
        (this[a]?.Opposite.Contains(b) ?? false) || (this[b]?.Opposite.Contains(a) ?? false);

    public static TraitCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var all = new List<TraitDef>();
        foreach (var t in doc.RootElement.GetProperty("traits").EnumerateArray())
        {
            var skills = new Dictionary<string, int>();
            if (t.TryGetProperty("skills", out var s))
                foreach (var p in s.EnumerateObject())
                    skills[p.Name] = p.Value.GetInt32();
            var effects = new Dictionary<string, double>();
            if (t.TryGetProperty("effects", out var e))
                foreach (var p in e.EnumerateObject())
                    effects[p.Name] = p.Value.GetDouble();
            var opposite = t.TryGetProperty("opposite", out var o) ? o.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>();
            all.Add(new TraitDef(t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!,
                t.GetProperty("kind").GetString()!, t.GetProperty("description").GetString()!, skills, effects, opposite,
                t.TryGetProperty("inherit", out var i) ? i.GetDouble() : 0,
                t.TryGetProperty("health", out var h) ? h.GetDouble() : 1,
                t.TryGetProperty("fertility", out var f) ? f.GetDouble() : 1,
                t.TryGetProperty("group", out var g) ? g.GetString() : null,
                t.TryGetProperty("opinion", out var op) ? op.GetInt32() : 0));
        }
        var bonds = new List<BondDef>();
        if (doc.RootElement.TryGetProperty("bonds", out var bs))
            foreach (var b in bs.EnumerateArray())
                bonds.Add(new BondDef(b.GetProperty("id").GetString()!, b.GetProperty("name").GetString()!,
                    b.GetProperty("value").GetInt32(), b.GetProperty("description").GetString()!));
        return new TraitCatalog(all) { Bonds = bonds };
    }
}

/// <summary>Something one person remembers about another, for or against (opinions, decision "What characters think of each other").</summary>
public sealed record Memory(int Of, string Reason, int Value, int Year);

/// <summary>How people die (decision "How characters die"), shown in the chronicle and their life.</summary>
public static class DeathCauses
{
    public const string OldAge = "old age", Disease = "disease", Childbirth = "childbirth", Battle = "battle",
        Execution = "execution", Murder = "murder", Accident = "accident";
}
