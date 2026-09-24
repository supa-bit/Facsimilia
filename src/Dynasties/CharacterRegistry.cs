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
/// reference this registry back. The yearly simulation of family life
/// (deaths, marriages, births, successions) is in CharacterRegistry.Life.cs.
/// </summary>
public sealed partial class CharacterRegistry
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
        int dynastyId = -1, int fatherId = -1, int motherId = -1, Culture culture = Culture.Greek)
    {
        var c = new Character(_nextCharacterId++, name, sex, birthYear, dynastyId, fatherId, motherId, culture);
        Characters[c.Id] = c;
        if (dynastyId != -1 && Dynasties.TryGetValue(dynastyId, out var dynasty))
            dynasty.MemberIds.Add(c.Id);
        if (fatherId != -1 && Characters.TryGetValue(fatherId, out var father))
            father.ChildrenIds.Add(c.Id);
        if (motherId != -1 && Characters.TryGetValue(motherId, out var mother))
            mother.ChildrenIds.Add(c.Id);
        return c;
    }

    public Realm CreateRealm(string name, Character ruler, SuccessionLaw law, Color color, Culture culture = Culture.Greek)
    {
        var r = new Realm(_nextRealmId++, name, ruler.Id, law, color, culture);
        Realms[r.Id] = r;
        return r;
    }

    public static void Marry(Character a, Character b)
    {
        a.SpouseId = b.Id;
        b.SpouseId = a.Id;
    }

    /// <summary>
    /// A child joins the father's dynasty and culture - or the mother's, when
    /// the father belongs to no ruling house (a princess or ruling queen who
    /// married outside the great houses keeps her children in her line).
    /// </summary>
    public Character HaveChild(Character mother, Character father, string name, string sex, int birthYear)
    {
        bool viaFather = father.DynastyId != -1 || mother.DynastyId == -1;
        var line = viaFather ? father : mother;
        return CreateCharacter(name, sex, birthYear, line.DynastyId, father.Id, mother.Id, line.Culture);
    }

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
            .OrderBy(c => c.BirthYear).ThenBy(c => c.Id).ToList();

    public Character? Father(Character c) => c.FatherId != -1 && Characters.TryGetValue(c.FatherId, out var f) ? f : null;
    public Character? Mother(Character c) => c.MotherId != -1 && Characters.TryGetValue(c.MotherId, out var m) ? m : null;

    // --- Succession ---------------------------------------------------------------

    const int SuccessionAncestorDepth = 4;  // how far up the family tree collateral lines are searched

    /// <summary>
    /// Who inherits the realm after its current ruler, per the realm's law:
    /// primogeniture with representation, then collateral lines. The ruler's
    /// own descendants come first, eldest line first (a dead eldest son's
    /// children come before his younger brother); under male-preference, sons'
    /// lines before daughters' at every level. With no living descendant, the
    /// same search moves up to the ruler's father's descendants (brothers,
    /// nephews), then the grandfather's (uncles, cousins), and so on. Null
    /// when nobody in the family is left - the dynasty's line has ended.
    /// Anyone already reigning elsewhere is skipped (RulesAnotherRealm).
    /// Works whether or not the ruler is still alive (the HUD's heir).
    /// </summary>
    public Character? ResolveHeir(Realm realm)
    {
        if (!Characters.TryGetValue(realm.RulerId, out var ruler))
            return null;
        Character? root = ruler;
        for (int depth = 0; root != null && depth <= SuccessionAncestorDepth; depth++)
        {
            foreach (var candidate in LineOf(root, realm.SuccessionLaw))
                if (candidate.IsAlive && candidate.Id != ruler.Id && !RulesAnotherRealm(candidate, realm))
                    return candidate;
            root = ParentInLine(root);
        }
        return null;
    }

    /// <summary>
    /// A reigning monarch is passed over for another realm's throne: realms
    /// don't merge by inheritance (no personal unions), so a claim goes to the
    /// next in line instead.
    /// </summary>
    bool RulesAnotherRealm(Character c, Realm realm) =>
        Realms.Values.Any(r => r.Id != realm.Id && r.RulerId == c.Id);

    /// <summary>
    /// The parent whose house this person belongs to: normally the father, but
    /// the mother when she passed on her dynasty (she married outside the
    /// great houses), so the search continues up the royal side.
    /// </summary>
    Character? ParentInLine(Character c)
    {
        var father = Father(c);
        var mother = Mother(c);
        if (father != null && father.DynastyId == c.DynastyId)
            return father;
        if (mother != null && mother.DynastyId == c.DynastyId)
            return mother;
        return father ?? mother;
    }

    /// <summary>A person's descendants in succession order (depth-first, eldest line first).</summary>
    IEnumerable<Character> LineOf(Character root, SuccessionLaw law)
    {
        var children = root.ChildrenIds.Select(id => Characters[id]).OrderBy(c => c.BirthYear).ThenBy(c => c.Id);
        var ordered = law == SuccessionLaw.MalePreferencePrimogeniture
            ? children.OrderBy(c => c.IsMale ? 0 : 1)  // stable: keeps birth order within each sex
            : children;
        foreach (var child in ordered)
        {
            yield return child;
            foreach (var descendant in LineOf(child, law))
                yield return descendant;
        }
    }

    /// <summary>
    /// Kills the current ruler and, if an heir can be resolved, hands them the
    /// realm. Returns the new ruler, or null when the line has ended (RulerId is
    /// left on the dead ruler; AdvanceYear then seats a new house).
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

    // --- Mortality ------------------------------------------------------------------

    /// <summary>
    /// Chance of dying within a year at a given age: a rough life table for a
    /// pre-modern elite. Infancy and early childhood are dangerous (together
    /// roughly one in six die before five); from 15 to 39 a steady 0.8% (war,
    /// disease, childbirth); from 40 a Gompertz rise, 1% at 40 doubling about
    /// every 9 years, capped at 35%. About 63% of 40-year-olds live to 60 and
    /// a third to 70.
    /// </summary>
    public static double DeathChanceForAge(int age) => age switch
    {
        < 0 => 0.0,
        0 => 0.08,
        < 5 => 0.025,
        < 15 => 0.007,
        < 40 => 0.008,
        _ => Math.Min(0.35, 0.01 * Math.Exp(0.075 * (age - 40))),
    };

    // --- Save / load -------------------------------------------------------------------
    // Plain Dictionary serialization for SaveSystem (stored as JSON), in the
    // same layout the GDScript version wrote plus optional newer fields
    // (culture), so older saves still load. JSON has no int/Color types, so
    // LoadFromDict converts every field back.

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
                ["culture"] = c.Culture.ToString(),
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
                ["culture"] = r.Culture.ToString(),
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
        foreach (var (_, value) in Section(data, "realms"))
        {
            var rd = value.AsGodotDictionary();
            var col = rd["color"].AsGodotArray();
            var color = new Color(col[0].AsSingle(), col[1].AsSingle(), col[2].AsSingle(), col[3].AsSingle());
            string name = rd["name"].AsString();
            var culture = rd.TryGetValue("culture", out var rc) ? Names.Parse(rc.AsString(), Names.ForRealmName(name))
                : Names.ForRealmName(name);
            var r = new Realm(rd["id"].AsInt32(), name, rd["ruler_id"].AsInt32(),
                (SuccessionLaw)rd["succession_law"].AsInt32(), color, culture);
            Realms[r.Id] = r;
        }
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
        // Cultures: saved ones, else (older saves) the culture of the realm the
        // character's dynasty rules, else Greek.
        var cultureByDynasty = new Dictionary<int, Culture>();
        foreach (var r in Realms.Values)
            if (Characters.TryGetValue(r.RulerId, out var ruler) && ruler.DynastyId != -1)
                cultureByDynasty.TryAdd(ruler.DynastyId, r.Culture);
        foreach (var (_, value) in Section(data, "characters"))
        {
            var cd = value.AsGodotDictionary();
            var c = Characters[cd["id"].AsInt32()];
            var inferred = cultureByDynasty.GetValueOrDefault(c.DynastyId, Culture.Greek);
            c.Culture = cd.TryGetValue("culture", out var cc) ? Names.Parse(cc.AsString(), inferred) : inferred;
        }
        _nextCharacterId = data.TryGetValue("next_character_id", out var nc) ? nc.AsInt32() : _nextCharacterId;
        _nextDynastyId = data.TryGetValue("next_dynasty_id", out var nd) ? nd.AsInt32() : _nextDynastyId;
        _nextRealmId = data.TryGetValue("next_realm_id", out var nr) ? nr.AsInt32() : _nextRealmId;
    }

    static GDictionary Section(GDictionary data, string key) =>
        data.TryGetValue(key, out var v) ? v.AsGodotDictionary() : new GDictionary();
}
