using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Dynasties;

/// <summary>
/// Owns every Character/Dynasty/Realm and is the only place that creates,
/// links, or kills them - the data objects themselves stay plain and never
/// reference this registry back.
/// </summary>
public sealed class CharacterRegistry
{
    public Dictionary<int, Character> Characters { get; } = new();
    public Dictionary<int, Dynasty> Dynasties { get; } = new();
    public Dictionary<int, Realm> Realms { get; } = new();

    int _nextCharacterId = 1;
    int _nextDynastyId = 1;
    int _nextRealmId = 1;

    public Dynasty CreateDynasty(string name, Character founder)
    {
        var dynasty = new Dynasty(_nextDynastyId++, name, founder.Id);
        Dynasties[dynasty.Id] = dynasty;
        founder.DynastyId = dynasty.Id;
        return dynasty;
    }

    public Character CreateCharacter(string name, string sex, int birthYear,
        int dynastyId = -1, int fatherId = -1, int motherId = -1)
    {
        var c = new Character(_nextCharacterId++, name, sex, birthYear, dynastyId, fatherId, motherId);
        Characters[c.Id] = c;
        if (dynastyId != -1 && Dynasties.TryGetValue(dynastyId, out var dynasty))
            dynasty.MemberIds.Add(c.Id);
        if (fatherId != -1 && Characters.TryGetValue(fatherId, out var father))
            father.ChildrenIds.Add(c.Id);
        if (motherId != -1 && Characters.TryGetValue(motherId, out var mother))
            mother.ChildrenIds.Add(c.Id);
        return c;
    }

    public Realm CreateRealm(string name, Character ruler, SuccessionLaw law, Color color)
    {
        var r = new Realm(_nextRealmId++, name, ruler.Id, law, color);
        Realms[r.Id] = r;
        return r;
    }

    public static void Marry(Character a, Character b)
    {
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
    }

    public Character HaveChild(Character mother, Character father, string name, string sex, int birthYear) =>
        CreateCharacter(name, sex, birthYear, father.DynastyId, father.Id, mother.Id);

    public void Kill(Character character, int deathYear)
    {
        character.IsAlive = false;
        character.DeathYear = deathYear;
        if (character.SpouseId != -1 && Characters.TryGetValue(character.SpouseId, out var spouse))
            spouse.SpouseId = -1;
    }

    /// <summary>Living children, eldest first.</summary>
    public List<Character> LivingChildren(Character character) =>
        character.ChildrenIds.Select(id => Characters[id]).Where(c => c.IsAlive)
            .OrderBy(c => c.BirthYear).ToList();

    /// <summary>
    /// Who inherits realm when its ruler is dead, per the realm's succession
    /// law. Only considers the late ruler's direct living children for now -
    /// falling back to collateral lines (siblings, cousins) is a follow-up.
    /// Returns null (a succession crisis) when no eligible heir exists.
    /// </summary>
    public Character? ResolveHeir(Realm realm)
    {
        if (!Characters.TryGetValue(realm.RulerId, out var ruler))
            return null;
        var children = LivingChildren(ruler);
        return realm.SuccessionLaw switch
        {
            SuccessionLaw.MalePreferencePrimogeniture => children.FirstOrDefault(c => c.IsMale) ?? children.FirstOrDefault(),
            _ => children.FirstOrDefault(),
        };
    }

    /// <summary>
    /// The "jump into your heir" moment: kills the current ruler and, if an
    /// heir can be resolved, hands them the realm. Returns the new ruler, or
    /// null on a succession crisis (RulerId is left pointing at the now-dead
    /// ruler then, since there's no one to hand it to).
    /// </summary>
    public Character? HandleRulerDeath(Realm realm, int deathYear)
    {
        if (!Characters.TryGetValue(realm.RulerId, out var ruler) || !ruler.IsAlive)
            return null;
        Kill(ruler, deathYear);
        var heir = ResolveHeir(realm);
        if (heir != null)
            realm.RulerId = heir.Id;
        return heir;
    }

    /// <summary>
    /// Placeholder mortality curve: flat 0% below 40, rising 1%/year, capped
    /// at 35%. Deliberately simple and deterministic so it's unit-testable -
    /// a real health/trait system replaces this later.
    /// </summary>
    public static double DeathChanceForAge(int age) => Math.Clamp((age - 40) * 0.01, 0.0, 0.35);

    /// <summary>
    /// Advances the world by one year: every living ruler ages and can die per
    /// DeathChanceForAge, triggering real succession. Pass a seeded rng for
    /// deterministic tests. Returns human-readable events for the chronicle.
    /// </summary>
    public List<string> AdvanceYear(int newYear, RandomNumberGenerator? rng = null)
    {
        if (rng == null)
        {
            rng = new RandomNumberGenerator();
            rng.Randomize();
        }
        var events = new List<string>();
        foreach (var realm in Realms.Values.ToList())
        {
            if (!Characters.TryGetValue(realm.RulerId, out var ruler) || !ruler.IsAlive)
                continue;
            int age = ruler.AgeIn(newYear);
            if (age < 0)
                continue;
            if (rng.Randf() < DeathChanceForAge(age))
            {
                string oldName = ruler.Name;
                var heir = HandleRulerDeath(realm, newYear);
                events.Add(heir == null
                    ? $"{realm.Name}: {oldName} has died at {age} with no heir - succession crisis."
                    : $"{realm.Name}: {oldName} has died at {age}. {heir.Name} inherits.");
            }
        }
        return events;
    }

    // --- Save / load -----------------------------------------------------------
    // Plain Dictionary serialization for SaveSystem (stored as JSON), in the
    // same layout the GDScript version wrote, so older saves still load. JSON
    // has no int/Color types, so LoadFromDict converts every field back.

    public GDictionary ToDict()
    {
        var characters = new GDictionary();
        foreach (var c in Characters.Values)
        {
            characters[c.Id.ToString()] = new GDictionary
            {
                ["id"] = c.Id, ["name"] = c.Name, ["sex"] = c.Sex, ["birth_year"] = c.BirthYear,
                ["death_year"] = c.DeathYear, ["is_alive"] = c.IsAlive, ["dynasty_id"] = c.DynastyId,
                ["father_id"] = c.FatherId, ["mother_id"] = c.MotherId, ["spouse_id"] = c.SpouseId,
                ["children_ids"] = new GArray(c.ChildrenIds.Select(i => Variant.From(i))),
                ["traits"] = new GArray(c.Traits.Select(t => Variant.From(t))),
            };
        }
        var dynasties = new GDictionary();
        foreach (var d in Dynasties.Values)
        {
            dynasties[d.Id.ToString()] = new GDictionary
            {
                ["id"] = d.Id, ["name"] = d.Name, ["founder_id"] = d.FounderId,
                ["member_ids"] = new GArray(d.MemberIds.Select(i => Variant.From(i))),
            };
        }
        var realms = new GDictionary();
        foreach (var r in Realms.Values)
        {
            realms[r.Id.ToString()] = new GDictionary
            {
                ["id"] = r.Id, ["name"] = r.Name, ["ruler_id"] = r.RulerId,
                ["succession_law"] = (int)r.SuccessionLaw,
                ["color"] = new GArray { r.Color.R, r.Color.G, r.Color.B, r.Color.A },
            };
        }
        return new GDictionary
        {
            ["characters"] = characters, ["dynasties"] = dynasties, ["realms"] = realms,
            ["next_character_id"] = _nextCharacterId, ["next_dynasty_id"] = _nextDynastyId,
            ["next_realm_id"] = _nextRealmId,
        };
    }

    public void LoadFromDict(GDictionary data)
    {
        Characters.Clear();
        Dynasties.Clear();
        Realms.Clear();
        foreach (var (_, value) in Section(data, "characters"))
        {
            var cd = value.AsGodotDictionary();
            var c = new Character(cd["id"].AsInt32(), cd["name"].AsString(), cd["sex"].AsString(),
                cd["birth_year"].AsInt32(), cd["dynasty_id"].AsInt32(), cd["father_id"].AsInt32(),
                cd["mother_id"].AsInt32())
            {
                DeathYear = cd["death_year"].AsInt32(),
                IsAlive = cd["is_alive"].AsBool(),
                SpouseId = cd["spouse_id"].AsInt32(),
                ChildrenIds = cd["children_ids"].AsGodotArray().Select(v => v.AsInt32()).ToList(),
                Traits = cd["traits"].AsGodotArray().Select(v => v.AsString()).ToList(),
            };
            Characters[c.Id] = c;
        }
        foreach (var (_, value) in Section(data, "dynasties"))
        {
            var dd = value.AsGodotDictionary();
            var d = new Dynasty(dd["id"].AsInt32(), dd["name"].AsString(), dd["founder_id"].AsInt32())
            {
                MemberIds = dd["member_ids"].AsGodotArray().Select(v => v.AsInt32()).ToList(),
            };
            Dynasties[d.Id] = d;
        }
        foreach (var (_, value) in Section(data, "realms"))
        {
            var rd = value.AsGodotDictionary();
            var col = rd["color"].AsGodotArray();
            var color = new Color(col[0].AsSingle(), col[1].AsSingle(), col[2].AsSingle(), col[3].AsSingle());
            var r = new Realm(rd["id"].AsInt32(), rd["name"].AsString(), rd["ruler_id"].AsInt32(),
                (SuccessionLaw)rd["succession_law"].AsInt32(), color);
            Realms[r.Id] = r;
        }
        _nextCharacterId = data.TryGetValue("next_character_id", out var nc) ? nc.AsInt32() : _nextCharacterId;
        _nextDynastyId = data.TryGetValue("next_dynasty_id", out var nd) ? nd.AsInt32() : _nextDynastyId;
        _nextRealmId = data.TryGetValue("next_realm_id", out var nr) ? nr.AsInt32() : _nextRealmId;
    }

    static GDictionary Section(GDictionary data, string key) =>
        data.TryGetValue(key, out var v) ? v.AsGodotDictionary() : new GDictionary();
}
