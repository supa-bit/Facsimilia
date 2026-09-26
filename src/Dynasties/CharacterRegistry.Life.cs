using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facsimilia.Dynasties;

public enum ChronicleKind
{
    Succession,  // a ruler died and an heir inherited
    NewHouse,    // a ruler died with no family left; a new house took the throne
    Death,       // someone else in a court died
    Marriage,
    Birth,
    Economy,     // treasury news: debt, deserters
    War,         // a war declared anywhere
    Peace,       // a war ended anywhere
    Conquest,    // land changed hands
    Revolt,      // a province rose up
    Disaster,    // plague, earthquake, eruption, famine
}

/// <summary>
/// One thing that happened this year. RealmId is the realm it concerns (for
/// births, marriages and deaths, the court it happened in), so the chronicle
/// can show every realm's successions but only the player's own family news.
/// </summary>
public sealed record ChronicleEvent(ChronicleKind Kind, int RealmId, string Text)
{
    /// <summary>Successions matter everywhere; family news only at home.</summary>
    public bool IsNewsFor(int playerRealmId) =>
        Kind is ChronicleKind.Succession or ChronicleKind.NewHouse or ChronicleKind.War or ChronicleKind.Peace or ChronicleKind.Disaster
        || RealmId == playerRealmId;
}

/// <summary>
/// The yearly life of the ruling families. Every living character can die
/// (DeathChanceForAge); rulers who die are succeeded (ResolveHeir), and a
/// realm whose family has died out gets a new house. Only each realm's
/// court - the ruler, spouse, children, grandchildren, siblings, nephews and
/// nieces, and the heir's household - marries and has children, which keeps
/// dynasties to a few dozen living members each over thousands of years.
/// </summary>
public sealed partial class CharacterRegistry
{
    // Marriage and fertility, per year. Rough pre-modern elite figures: most
    // princes married young, princesses younger; a married woman had a child
    // about every four years in her twenties, fewer later - four or five
    // births over a full marriage, of whom three or four reached adulthood.
    const int MarryMinMale = 17, MarryMaxMale = 55;
    const int MarryMinFemale = 15, MarryMaxFemale = 38;
    const double MarriageChance = 0.35;       // per eligible unmarried court member per year
    const double CrossRealmMarriageShare = 0.3;  // of marriages, how many match another realm's court
    const int FertileMin = 16, FertileMax = 42;
    const int MaxChildren = 9;

    // One generator for the whole game. (A fresh, clock-seeded one each year
    // repeats the same rolls when turns come quickly, so everyone of an age
    // dies in the same year.)
    readonly RandomNumberGenerator _rng = NewRandomized();

    static RandomNumberGenerator NewRandomized()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        return rng;
    }

    static double BirthChance(int motherAge) => motherAge switch
    {
        < FertileMin or > FertileMax => 0.0,
        < 30 => 0.24,
        < 35 => 0.17,
        < 40 => 0.09,
        _ => 0.03,
    };

    /// <summary>
    /// Advances everyone by one year: deaths, successions (a new house where a
    /// line has ended), marriages, births. Pass a seeded rng for deterministic
    /// tests. Returns what happened, for the chronicle.
    /// </summary>
    public List<ChronicleEvent> AdvanceYear(int year, RandomNumberGenerator? rng = null)
    {
        rng ??= _rng;
        var events = new List<ChronicleEvent>();
        var courts = Courts();

        // Deaths. Rulers' deaths trigger succession, in every realm they rule
        // (looked up at the moment of death: an heir who inherited earlier in
        // the same year and then dies is succeeded too).
        foreach (var c in Living())
        {
            int age = c.AgeIn(year);
            if (age < 0 || rng.Randf() >= DeathChanceForAge(age))
                continue;
            var ruled = Realms.Values.Where(r => r.RulerId == c.Id).OrderBy(r => r.Id).ToList();
            if (ruled.Count > 0)
            {
                Kill(c, year);
                foreach (var realm in ruled)
                    events.Add(Succeed(realm, c, age, year, rng));
            }
            else
            {
                Kill(c, year);
                if (courts.TryGetValue(c.Id, out int realmId))
                    events.Add(new(ChronicleKind.Death, realmId, $"{c.Name} has died at {age}."));
            }
        }

        courts = Courts();  // successions change who's at court
        Marriages(year, courts, rng, events);
        Births(year, courts, rng, events);
        return events;
    }

    // A succession can fail even with an heir: a strongman takes the throne.
    // Most dangerous when the heir is a child under a regent, or a distant
    // relative rather than the late ruler's own son or daughter.
    const double UsurpationBase = 0.06;
    const double UsurpationMinorHeir = 0.25;      // heir under 16
    const double UsurpationCollateralHeir = 0.10; // heir isn't the late ruler's descendant
    const int AdultAge = 16;

    /// <summary>Chance that a new house seizes the throne instead of this heir.</summary>
    public double UsurpationChance(Character dead, Character heir, int year) =>
        UsurpationBase
        + (heir.AgeIn(year) < AdultAge ? UsurpationMinorHeir : 0)
        + (IsDescendantOf(heir, dead) ? 0 : UsurpationCollateralHeir);

    bool IsDescendantOf(Character c, Character ancestor)
    {
        var frontier = new List<Character> { c };
        for (int depth = 0; depth < 8 && frontier.Count > 0; depth++)
        {
            if (frontier.Any(p => p.Id == ancestor.Id))
                return true;
            frontier = frontier.SelectMany(p => new[] { Father(p), Mother(p) }).OfType<Character>().ToList();
        }
        return false;
    }

    ChronicleEvent Succeed(Realm realm, Character dead, int age, int year, RandomNumberGenerator rng)
    {
        var heir = ResolveHeir(realm);
        if (heir != null && rng.Randf() >= UsurpationChance(dead, heir, year))
        {
            realm.RulerId = heir.Id;
            return new(ChronicleKind.Succession, realm.Id, $"{realm.Name}: {dead.Name} has died at {age}. {heir.Name} inherits.");
        }
        var founder = FoundHouse(realm, year, rng);
        string text = heir == null
            ? $"{realm.Name}: {dead.Name} has died at {age}, the last of {(dead.IsMale ? "his" : "her")} line. " +
              $"{founder.Name} takes the throne and founds a new house."
            : $"{realm.Name}: {dead.Name} has died at {age}. {founder.Name} pushes aside the heir, {heir.Name}" +
              (heir.AgeIn(year) < AdultAge ? $" (aged {heir.AgeIn(year)})" : "") + ", seizes the throne and founds a new house.";
        return new(ChronicleKind.NewHouse, realm.Id, text);
    }

    /// <summary>
    /// When a ruling family has died out or been overthrown: a new ruler (a noble of the realm's
    /// culture, 30-50) takes the throne with a wife and young children, and
    /// founds a house named after himself.
    /// </summary>
    public Character FoundHouse(Realm realm, int year, RandomNumberGenerator rng)
    {
        var name = Names.Pick(realm.Culture, true, rng);
        var founder = CreateCharacter(name, "male", year - rng.RandiRange(30, 50), culture: realm.Culture);
        CreateDynasty("House " + name, founder);
        var wife = CreateCharacter(Names.Pick(realm.Culture, false, rng), "female",
            founder.BirthYear + rng.RandiRange(2, 12), culture: realm.Culture);
        Marry(founder, wife);
        int children = rng.RandiRange(1, 3);
        for (int i = 0; i < children; i++)
        {
            int born = wife.BirthYear + 18 + 3 * i;
            if (born > year)
                break;
            bool male = rng.Randf() < 0.5f || i == 0;  // at least one son
            HaveChild(wife, founder, Names.Pick(realm.Culture, male, rng, NamesOfChildren(founder)), male ? "male" : "female", born);
        }
        realm.RulerId = founder.Id;
        return founder;
    }

    /// <summary>
    /// Each living court member mapped to the realm whose court they're in: the
    /// ruler, spouse, children, grandchildren, siblings, nephews and nieces,
    /// and the heir with their spouse and children.
    /// </summary>
    Dictionary<int, int> Courts()
    {
        var court = new Dictionary<int, int>();
        void Add(Character? c, int realmId)
        {
            if (c != null && c.IsAlive)
                court.TryAdd(c.Id, realmId);
        }
        IEnumerable<Character> Kids(Character c) => c.ChildrenIds.Select(id => Characters[id]);

        foreach (var realm in Realms.Values)
        {
            if (!Characters.TryGetValue(realm.RulerId, out var ruler))
                continue;
            Add(ruler, realm.Id);
            if (ruler.SpouseId != -1)
                Add(Characters.GetValueOrDefault(ruler.SpouseId), realm.Id);
            foreach (var child in Kids(ruler))
            {
                Add(child, realm.Id);
                foreach (var grandchild in Kids(child))
                    Add(grandchild, realm.Id);
            }
            var parent = ParentInLine(ruler);
            if (parent != null)
            {
                foreach (var sibling in Kids(parent).Where(s => s.Id != ruler.Id))
                {
                    Add(sibling, realm.Id);
                    foreach (var nibling in Kids(sibling))
                        Add(nibling, realm.Id);
                }
            }
            var heir = ResolveHeir(realm);
            if (heir != null)
            {
                Add(heir, realm.Id);
                if (heir.SpouseId != -1)
                    Add(Characters.GetValueOrDefault(heir.SpouseId), realm.Id);
                foreach (var child in Kids(heir))
                    Add(child, realm.Id);
            }
        }
        return court;
    }

    static bool Marriageable(Character c, int year)
    {
        int age = c.AgeIn(year);
        return c.IsAlive && c.SpouseId == -1 && (c.IsMale
            ? age is >= MarryMinMale and <= MarryMaxMale
            : age is >= MarryMinFemale and <= MarryMaxFemale);
    }

    void Marriages(int year, Dictionary<int, int> courts, RandomNumberGenerator rng, List<ChronicleEvent> events)
    {
        // Who at court is free to marry this year, found once rather than for every suitor.
        var free = courts.Where(kv => Marriageable(Characters[kv.Key], year)).Select(kv => kv.Key).ToList();
        foreach (var (id, realmId) in courts.OrderBy(kv => kv.Key))
        {
            var c = Characters[id];
            if (!Marriageable(c, year) || rng.Randf() >= MarriageChance)
                continue;
            Character spouse;
            string where = "";
            var match = rng.Randf() < CrossRealmMarriageShare
                ? free.Where(o => courts[o] != realmId)
                    .Select(o => Characters[o])
                    .Where(o => o.IsMale != c.IsMale && Marriageable(o, year) && o.DynastyId != c.DynastyId
                        && Math.Abs(o.BirthYear - c.BirthYear) <= 15)
                    .OrderBy(o => Math.Abs(o.BirthYear - c.BirthYear)).ThenBy(o => o.Id)
                    .FirstOrDefault()
                : null;
            if (match != null)
            {
                spouse = match;
                where = $" of {Realms[courts[match.Id]].Name}";
            }
            else
            {
                // A noble of the realm's own culture, outside the great houses.
                var culture = Realms[realmId].Culture;
                int born = c.IsMale ? c.BirthYear + rng.RandiRange(1, 10) : c.BirthYear - rng.RandiRange(0, 10);
                born = c.IsMale ? Math.Min(born, year - MarryMinFemale) : Math.Min(born, year - MarryMinMale);
                spouse = CreateCharacter(Names.Pick(culture, !c.IsMale, rng), c.IsMale ? "female" : "male", born, culture: culture);
            }
            Marry(c, spouse);
            var text = $"{c.Name} marries {spouse.Name}{where}.";
            events.Add(new(ChronicleKind.Marriage, realmId, text));
            if (match != null && courts[match.Id] != realmId)
                events.Add(new(ChronicleKind.Marriage, courts[match.Id], $"{spouse.Name} marries {c.Name} of {Realms[realmId].Name}."));
        }
    }

    void Births(int year, Dictionary<int, int> courts, RandomNumberGenerator rng, List<ChronicleEvent> events)
    {
        foreach (var mother in Living().Where(c => !c.IsMale && c.SpouseId != -1).ToList())
        {
            var father = Characters[mother.SpouseId];
            if (!father.IsAlive)
                continue;
            int realmId = courts.TryGetValue(mother.Id, out int r1) ? r1 : courts.TryGetValue(father.Id, out int r2) ? r2 : -1;
            if (realmId == -1)
                continue;  // only the courts keep growing
            var siblings = mother.ChildrenIds.Select(id => Characters[id]).ToList();
            if (siblings.Count >= MaxChildren || siblings.Any(s => s.BirthYear == year)
                || rng.Randf() >= BirthChance(mother.AgeIn(year)))
                continue;
            bool male = rng.Randf() < 0.51f;
            var line = father.DynastyId != -1 || mother.DynastyId == -1 ? father : mother;
            var child = HaveChild(mother, father, ChildName(line.Culture, male, mother, father, siblings, rng),
                male ? "male" : "female", year);
            events.Add(new(ChronicleKind.Birth, realmId,
                $"A {(male ? "son" : "daughter")}, {child.Name}, is born to {mother.Name} and {father.Name}."));
        }
    }

    /// <summary>
    /// A name for a newborn, never a living sibling's. The first son is often
    /// named for his paternal grandfather and the first daughter for her
    /// maternal grandmother, as was common practice.
    /// </summary>
    string ChildName(Culture culture, bool male, Character mother, Character father, List<Character> siblings,
        RandomNumberGenerator rng)
    {
        var taken = siblings.Where(s => s.IsAlive).Select(s => s.Name).ToHashSet();
        bool firstOfSex = siblings.All(s => s.IsMale != male);
        var namesake = male ? Father(father) : Mother(mother);
        if (firstOfSex && namesake != null && !taken.Contains(namesake.Name) && rng.Randf() < 0.4f)
            return namesake.Name;
        return Names.Pick(culture, male, rng, taken);
    }

    HashSet<string> NamesOfChildren(Character parent) =>
        parent.ChildrenIds.Select(id => Characters[id].Name).ToHashSet();
}
