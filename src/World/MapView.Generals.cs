using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Generals (decision "Next 2"): the men of the ruling family lead first; when
/// the family runs short, the realm appoints commanders, and now and then meets
/// a man of great prowess and raises him, or one of the great captains of the
/// age appears. Generals age, die, and pass their armies on.
/// </summary>
public partial class MapView
{
    /// <summary>Each year, the chance a realm meets a man of prowess (doubled at war).</summary>
    public const double ProwessChance = 0.03;
    /// <summary>Of those, the share who are great captains.</summary>
    public const double GreatShare = 0.12;
    /// <summary>Chance a beaten general falls on the field.</summary>
    public const double FallChance = 0.08;

    /// <summary>The family members a realm could send to lead: men of the ruling house, 18 to 65, alive.</summary>
    public List<Character> FamilyCommanders(int realmId)
    {
        var list = new List<Character>();
        if (!Registry.Realms.TryGetValue(realmId, out var realm) || !Registry.Characters.TryGetValue(realm.RulerId, out var ruler))
            return list;
        if (!Registry.Dynasties.TryGetValue(ruler.DynastyId, out var dyn))
            return list;
        foreach (int id in dyn.MemberIds)
            if (Registry.Characters.TryGetValue(id, out var c) && c.IsAlive && c.IsMale && c.AgeIn(DemoYear) is >= 18 and <= 65)
                list.Add(c);
        return list;
    }

    General NewGeneral(RealmState s, string name, string origin, int born, int characterId, Random rng)
    {
        var g = General.Make(s.NextGeneralId++, name, origin, born, characterId, rng, DemoYear);
        s.Generals.Add(g);
        return g;
    }

    string CommanderName(int realmId, Random rng)
    {
        var grng = new RandomNumberGenerator { Seed = (ulong)rng.NextInt64() };
        return Names.Pick(CultureOf(realmId), true, grng);
    }

    /// <summary>Puts a general at the head of an army (the old one waits at court).</summary>
    public void Assign(RealmState s, General g, Army a)
    {
        foreach (var other in s.Generals.Where(x => x.ArmyId == a.Id))
            other.ArmyId = 0;
        foreach (var army in s.Armies.Where(x => x.GeneralId == g.Id))
            army.GeneralId = 0;
        g.ArmyId = a.Id;
        a.GeneralId = g.Id;
    }

    /// <summary>At the start: the great captains of 300 BC lead their first armies (data/generals.json "historical").</summary>
    void StartGenerals()
    {
        var rng = new Random(StableHash.Of(DemoYear, 4271));
        var historical = new Dictionary<string, JsonElement>();
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(PerkCatalog.DataPath));
        if (doc.RootElement.TryGetProperty("historical", out var h))
            foreach (var e in h.EnumerateArray())
                historical[e.GetProperty("realm").GetString()!] = e.Clone();
        foreach (var (key, id) in CivRealmIds)
        {
            var s = Game.Realm(id);
            if (!historical.TryGetValue(key, out var spec) || s.Armies.Count == 0)
                continue;
            string name = spec.GetProperty("name").GetString()!;
            var who = FamilyCommanders(id).FirstOrDefault(c => c.Name == name)
                ?? (Registry.Realms.TryGetValue(id, out var r) && Registry.Characters.TryGetValue(r.RulerId, out var ru) && ru.Name == name ? ru : null);
            var g = NewGeneral(s, name, General.Family, who?.BirthYear ?? DemoYear - 40, who?.Id ?? -1, rng);
            g.Perks.Clear();
            foreach (var p in spec.GetProperty("perks").EnumerateArray())
                g.Perks.Add(p.GetString()!);
            int k = 0;
            foreach (var v in spec.GetProperty("skills").EnumerateArray())
                if (k < Skills.Count)
                    g.BaseSkills[k++] = v.GetInt32();
            Assign(s, g, s.Armies[0]);
        }
        GeneralsYear(rng, false);
    }

    /// <summary>
    /// The year for every realm's generals: the dead are mourned, armies
    /// without a leader get one (the family first), and men of prowess are met.
    /// </summary>
    internal List<ChronicleEvent> GeneralsYear(Random rng, bool chance = true)
    {
        var events = new List<ChronicleEvent>();
        foreach (var s in Game.Realms.Values)
        {
            int id = s.RealmId;
            if (!Registry.Realms.ContainsKey(id))
                continue;
            // The dead: family men die with their character; appointed men of age.
            foreach (var g in s.Generals.ToList())
            {
                bool dead = g.CharacterId >= 0
                    ? !Registry.Characters.TryGetValue(g.CharacterId, out var c) || !c.IsAlive
                    : chance && rng.NextDouble() < CharacterRegistry.DeathChanceForAge(DemoYear - g.BornYear);
                bool old = DemoYear - g.BornYear > 70;
                if (!dead && !old)
                    continue;
                s.Generals.Remove(g);
                foreach (var a in s.Armies.Where(a => a.GeneralId == g.Id))
                    a.GeneralId = 0;
                if (id == PlayerRealmId && g.ArmyId != 0)
                    events.Add(new ChronicleEvent(ChronicleKind.Death, id, dead
                        ? $"{g.Name}, who led {s.ArmyById(g.ArmyId)?.Name ?? "an army"}, is dead."
                        : $"{g.Name} is too old to command, and retires."));
            }
            foreach (var a in s.Armies)
                if (a.GeneralId != 0 && s.GeneralOf(a) == null)
                    a.GeneralId = 0;

            // Men of prowess, met by chance.
            bool atWar = Game.Wars.Of(id).Any();
            if (chance && s.Armies.Count > 0 && rng.NextDouble() < ProwessChance * (atWar ? 2 : 1))
            {
                bool great = rng.NextDouble() < GreatShare;
                string name = CommanderName(id, rng);
                var g = NewGeneral(s, name, great ? General.Great : General.Appointed, DemoYear - rng.Next(22, 40), -1, rng);
                if (!great)
                    g.BaseSkills[Skills.Tactics] = Math.Min(10, g.BaseSkills[Skills.Tactics] + 2);
                string people = CultureName(RealmPeople(id).Culture);
                string text = great
                    ? $"{RealmName(id)}: {name} comes forward, one of the great captains of the age ({PerkCatalog.Instance[g.Perks[0]]?.Name})."
                    : $"{RealmName(id)} has met a man of great prowess among the {people}, {name}, and raised him to general.";
                events.Add(new ChronicleEvent(ChronicleKind.War, id, text));
                if (id != PlayerRealmId)
                {
                    var weakest = s.Armies.Where(a => !a.IsEmpty).OrderBy(a => s.GeneralOf(a)?.Skill(Skills.Tactics) ?? 0)
                        .ThenByDescending(a => Military.RawMight(a, Domain.Land)).FirstOrDefault();
                    if (weakest != null && (s.GeneralOf(weakest)?.Skill(Skills.Tactics) ?? 0) < g.Skill(Skills.Tactics))
                        Assign(s, g, weakest);
                }
            }

            // Armies without a leader: a family man, else an appointed one.
            var family = FamilyCommanders(id).Where(c => !s.Generals.Any(g => g.CharacterId == c.Id)).ToList();
            foreach (var a in s.Armies.Where(a => a.GeneralId == 0 && !a.IsEmpty))
            {
                var waiting = s.Generals.FirstOrDefault(g => g.ArmyId == 0 || s.ArmyById(g.ArmyId) == null);
                if (waiting == null && family.Count > 0)
                {
                    var c = family[0];
                    family.RemoveAt(0);
                    waiting = NewGeneral(s, c.Name, General.Family, c.BirthYear, c.Id, rng);
                }
                waiting ??= NewGeneral(s, CommanderName(id, rng), General.Appointed, DemoYear - rng.Next(25, 50), -1, rng);
                Assign(s, waiting, a);
            }
            // Generals of armies that are gone wait at court; an idle court is kept small.
            foreach (var g in s.Generals)
                if (g.ArmyId != 0 && s.ArmyById(g.ArmyId) == null)
                    g.ArmyId = 0;
            var idle = s.Generals.Where(g => g.ArmyId == 0 && g.Origin == General.Appointed).ToList();
            foreach (var g in idle.Skip(3))
                s.Generals.Remove(g);
        }
        return events;
    }

    /// <summary>The player appoints a new commander from the court.</summary>
    public General AppointGeneral(RealmState s)
    {
        var rng = new Random(StableHash.Of(DemoYear, s.NextGeneralId, 977));
        return NewGeneral(s, CommanderName(s.RealmId, rng), General.Appointed, DemoYear - rng.Next(25, 50), -1, rng);
    }

    /// <summary>A family member takes up command.</summary>
    public General? FamilyGeneral(RealmState s, int characterId)
    {
        if (!Registry.Characters.TryGetValue(characterId, out var c))
            return null;
        return NewGeneral(s, c.Name, General.Family, c.BirthYear, c.Id, new Random(StableHash.Of(characterId, 131)));
    }

    /// <summary>A beaten general may fall on the field.</summary>
    void MaybeFall(RealmState s, General? g, Random rng, List<ChronicleEvent> events)
    {
        if (g == null || rng.NextDouble() >= FallChance)
            return;
        s.Generals.Remove(g);
        foreach (var a in s.Armies.Where(a => a.GeneralId == g.Id))
            a.GeneralId = 0;
        if (g.CharacterId >= 0 && Registry.Characters.TryGetValue(g.CharacterId, out var c) && c.IsAlive)
        {
            bool ruler = Registry.Realms.TryGetValue(s.RealmId, out var r) && r.RulerId == c.Id;
            if (!ruler)
                Registry.Kill(c, DemoYear);   // a ruler's death in battle is left to the succession rules
        }
        events.Add(new ChronicleEvent(ChronicleKind.War, s.RealmId, $"{g.Name} of {RealmName(s.RealmId)} falls in battle."));
    }
}
