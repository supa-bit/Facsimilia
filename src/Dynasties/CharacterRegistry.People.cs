using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;

namespace Facsimilia.Dynasties;

/// <summary>
/// People as people (the Ledger's "The court: characters and families"):
/// the twelve skills, traits (some inherited from parents), schooling at
/// sixteen, illnesses that come with age, how they die, what they think of
/// one another, and what a ruler's skills and traits do to the realm.
/// </summary>
public sealed partial class CharacterRegistry
{
    public const int OrdinarySkill = 5, MaxSkill = 12;
    const int PersonalityTraits = 3;

    /// <summary>
    /// A skill on the 1-100 scale shown to the player (decision "How skills
    /// are shown"): base plus traits (each trait point is ten), grown with
    /// age (a child has only part of it) and fading in old age (decision
    /// "Skills through life"): the body's skills from sixty, the rest from seventy,
    /// while learning, statecraft and piety hold.
    /// </summary>
    public int SkillPoints(Character c, int skill, int year)
    {
        Develop(c);
        var cat = TraitCatalog.Instance;
        int v = c.BaseSkills![skill];
        foreach (var id in c.Traits)
            if (cat[id] is { } t && t.Skills.TryGetValue(CharSkills.Keys[skill], out int add))
                v += 10 * add;
        int age = c.AgeIn(year);
        if (age < AdultAge)
            v = (int)Math.Round(v * Math.Max(0.15, age / (double)AdultAge));
        bool body = skill is CharSkills.Tactics or CharSkills.Leadership or CharSkills.Logistics or CharSkills.Seamanship;
        bool mind = skill is CharSkills.Learning or CharSkills.Statecraft or CharSkills.Piety;
        if (body && age > 60)
            v -= (int)((age - 60) * 1.5);
        else if (!mind && age > 70)
            v -= age - 70;
        return Math.Clamp(v, 1, 100);
    }

    /// <summary>A skill as the rules use it: 1-10 (5 ordinary), up to 12 with great gifts.</summary>
    public int Skill(Character c, int skill, int year)
    {
        Develop(c);
        var cat = TraitCatalog.Instance;
        int bonus = 0;
        foreach (var id in c.Traits)
            if (cat[id] is { } t && t.Skills.TryGetValue(CharSkills.Keys[skill], out int add))
                bonus += add;
        // Traits can lift a skill past 100 in the rules (to 12), though the shown points stop at 100.
        int points = SkillPoints(c, skill, year);
        int over = Math.Max(0, c.BaseSkills![skill] + 10 * bonus - 100);
        return Math.Clamp((int)Math.Round((points + over) / 10.0), 1, MaxSkill);
    }

    public int[] Skills(Character c, int year) => Enumerable.Range(0, CharSkills.Count).Select(s => Skill(c, s, year)).ToArray();

    /// <summary>
    /// Gives a person their nature the first time it's needed: base skills
    /// (half from the parents, half their own), traits of body and health
    /// that run in the family, and three of personality. Fixed by their id,
    /// so the same person is always the same.
    /// </summary>
    public void Develop(Character c)
    {
        if (c.BaseSkills != null)
            return;
        var rng = new Random(StableHash.Of(c.Id, c.BirthYear, 7717));
        var father = Father(c);
        var mother = Mother(c);
        if (father != null) Develop(father);
        if (mother != null) Develop(mother);
        c.BaseSkills = new int[CharSkills.Count];
        for (int s = 0; s < CharSkills.Count; s++)
        {
            int own = 20 + rng.Next(0, 31) + rng.Next(0, 31);   // 20-80, mostly 40-60
            int fromParents = father != null && mother != null ? (father.BaseSkills![s] + mother.BaseSkills![s] + 1) / 2
                : father?.BaseSkills![s] ?? mother?.BaseSkills![s] ?? own;
            c.BaseSkills[s] = Math.Clamp((own + fromParents) / 2 + rng.Next(-5, 6), 1, 100);
        }
        var cat = TraitCatalog.Instance;
        if (c.Traits.Any(id => cat[id]?.Kind == TraitDef.Personality))
            return;   // nature already given (a saved game)
        foreach (var parent in new[] { father, mother }.OfType<Character>())
            foreach (var id in parent.Traits)
                if (cat[id] is { Inherit: > 0 } t && rng.NextDouble() < t.Inherit)
                    AddTrait(c, id);
        // A rare inborn gift or flaw even without the parents'.
        if (rng.NextDouble() < 0.25)
        {
            var born = cat.Inborn;
            AddTrait(c, born[rng.Next(born.Count)].Id);
        }
        var personality = cat.OfKind(TraitDef.Personality);
        for (int n = 0, tries = 0; n < PersonalityTraits && tries < 30; tries++)
            if (AddTrait(c, personality[rng.Next(personality.Count)].Id))
                n++;
        // Faith: the family's (inherited above), else the gods of their own people.
        if (!c.Traits.Any(id => cat[id]?.Group == TraitDef.Religion))
            AddTrait(c, ReligionOf(c.Culture));
        // Bloodline: royal blood from a reigning parent, two peoples from mixed parents,
        // and the ancestral claims of the great houses for those at the head of their line.
        if (new[] { father, mother }.OfType<Character>().Any(p => Realms.Values.Any(r => r.RulerId == p.Id)))
            AddTrait(c, "royal_blood");
        if (father != null && mother != null && father.Culture != mother.Culture)
            AddTrait(c, "half_foreign");
        if (father == null && mother == null && Dynasties.TryGetValue(c.DynastyId, out var house)
            && HouseClaims.TryGetValue(house.Name, out var claims))
            foreach (var claim in claims)
                AddTrait(c, claim);
    }

    /// <summary>The gods each people worshipped.</summary>
    public static string ReligionOf(Culture culture) => culture switch
    {
        Culture.Latin or Culture.Italic => "roman_gods",
        Culture.Punic => "canaanite",
        Culture.Greek => "olympian",
        Culture.Meroitic => "meroitic_gods",
        Culture.Nabataean or Culture.Arabian => "arabian_gods",
        Culture.Iberian => "iberian_gods",
        Culture.Celtic => "druidic",
        Culture.Scythian => "steppe_gods",
        Culture.Etruscan => "etruscan_rites",
        Culture.Thracian => "thracian_gods",
        Culture.Illyrian => "illyrian_gods",
        Culture.Berber => "berber_gods",
        Culture.Persian => "zoroastrian",
        Culture.Caucasian => "caucasian_gods",
        _ => "olympian",
    };

    /// <summary>
    /// What the great houses of 300 BC claimed for their blood: the Seleucids
    /// Apollo, the Ptolemies Heracles and Dionysus, the Spartan kings Heracles,
    /// the kings of Epirus Achilles, Cassander's sons Philip's blood through
    /// their mother, and the Persian kings for the houses of Pontus, Armenia
    /// and Cappadocia.
    /// </summary>
    static readonly Dictionary<string, string[]> HouseClaims = new()
    {
        ["House of Seleukos"] = new[] { "descent_apollo", "royal_blood" },
        ["House of Ptolemaios"] = new[] { "descent_heracles", "descent_dionysus", "royal_blood" },
        ["House of the Agiads"] = new[] { "descent_heracles", "royal_blood", "ancient_house" },
        ["House of the Aiakidai"] = new[] { "descent_achilles", "royal_blood", "ancient_house" },
        ["House of Antipatros"] = new[] { "royal_blood" },
        ["House of the Mithridatids"] = new[] { "achaemenid_blood", "royal_blood" },
        ["House of the Orontids"] = new[] { "achaemenid_blood", "royal_blood", "ancient_house" },
        ["House of Ariarathes"] = new[] { "achaemenid_blood", "royal_blood" },
        ["House of Atropates"] = new[] { "persian_nobility", "royal_blood" },
        ["House of Arkamani"] = new[] { "napatan_line", "royal_blood", "ancient_house" },
        ["House Valerius"] = new[] { "patrician" },
    };

    /// <summary>Adds a trait unless it is already held or opposed to one that is.</summary>
    public bool AddTrait(Character c, string id)
    {
        var cat = TraitCatalog.Instance;
        if (cat[id] == null || c.Traits.Contains(id) || c.Traits.Any(t => cat.Opposed(t, id)))
            return false;
        c.Traits.Add(id);
        c.HealthCache = c.FertilityCache = double.NaN;
        return true;
    }

    /// <summary>
    /// The schooling a person finished with at sixteen: in the skill they were
    /// best at, graded by how good they were (decision "Raising children"
    /// will let you choose the tutor). Most also take up a pastime.
    /// </summary>
    void Educate(Character c, Random rng)
    {
        Develop(c);
        var cat = TraitCatalog.Instance;
        if (c.Traits.Any(t => cat[t]?.Kind == TraitDef.Education))
            return;
        int best = 0, bestScore = -1;
        for (int s = 0; s < CharSkills.Count; s++)
        {
            int score = c.BaseSkills![s] * 10 + rng.Next(10);
            if (score > bestScore)
                (best, bestScore) = (s, score);
        }
        int v = c.BaseSkills![best];
        string grade = v >= 85 ? "master" : v >= 65 ? "learned" : v >= 45 ? "trained" : "novice";
        AddTrait(c, $"edu_{CharSkills.Keys[best]}_{grade}");
        // Often a kinsman or the court's master of that art taught them.
        var teacher = new[] { Father(c), Mother(c) }.OfType<Character>()
            .Concat(Dynasties.TryGetValue(c.DynastyId, out var dyn) ? dyn.MemberIds.TakeLast(40).Select(i => Characters[i]) : Enumerable.Empty<Character>())
            .Where(t => t.IsAlive && t.Id != c.Id && t.BaseSkills != null && t.BaseSkills[best] >= 70)
            .OrderByDescending(t => t.BaseSkills![best]).FirstOrDefault();
        if (teacher != null && rng.NextDouble() < 0.35 && teacher.Bonds.Count < MaxBonds)
            Bind(teacher, c, "mentor", "student", c.BirthYear + AdultAge);
        // Some take up a mystery cult; the learned may follow a school of philosophy.
        var faith = cat.OfKind(TraitDef.Faith);
        if (rng.NextDouble() < 0.12)
        {
            var cults = faith.Where(t => t.Group == TraitDef.Devotion).ToList();
            AddTrait(c, cults[rng.Next(cults.Count)].Id);
        }
        if (c.BaseSkills![CharSkills.Learning] >= 60 && rng.NextDouble() < 0.3)
        {
            var schools = faith.Where(t => t.Group == TraitDef.Philosophy).ToList();
            AddTrait(c, schools[rng.Next(schools.Count)].Id);
        }
        if (rng.NextDouble() < 0.5)
        {
            var pastimes = cat.OfKind(TraitDef.Lifestyle);
            AddTrait(c, pastimes[rng.Next(pastimes.Count)].Id);
        }
    }

    /// <summary>A year older: schooling ends at sixteen; illnesses of age and old age's infirmity come.</summary>
    static readonly string[] IllnessesOfAge = { "gout", "weak_heart", "migraines", "melancholia", "nearsighted", "drunk_liver", "lame" };

    void GrowOlder(int year, Random rng, List<Character> living)
    {
        string[] ofAge = IllnessesOfAge;
        foreach (var c in living)
        {
            int age = c.AgeIn(year);
            if (age >= AdultAge && !c.Schooled)
            {
                Educate(c, rng);
                c.Schooled = true;
            }
            if (age > 35 && rng.NextDouble() < 0.012)
                AddTrait(c, ofAge[rng.Next(ofAge.Length)]);
            if (age >= 65 && rng.NextDouble() < 0.08)
                AddTrait(c, "infirm");
        }
        foreach (var realm in Realms.Values)
            if (Characters.TryGetValue(realm.RulerId, out var ruler) && ruler.IsAlive)
                SetReputation(ruler, ReputationOf(ruler, year));
    }

    // --- Reputation ----------------------------------------------------------------------

    /// <summary>What the world says of a ruler, from their deeds and nature (one reputation at a time), or null: little known.</summary>
    public string? ReputationOf(Character c, int year)
    {
        bool Any(params string[] ids) => ids.Any(c.Has);
        if (Any("kinslayer", "parricide", "oathbreaker", "sacrilegious", "tyrant"))
            return "notorious";
        if (Any("cruel", "sadistic", "arbitrary") && Any("greedy"))
            return "hated";
        if (Any("cruel", "ruthless", "wrathful", "sadistic") && Any("conqueror", "victor", "sacker"))
            return "feared";
        if (Any("conqueror", "famous_commander", "wonder_builder", "olympic_victor", "genius", "founder"))
            return "famed";
        if (Any("generous") && Any("just", "kind", "compassionate"))
            return "beloved";
        if ((Any("just") && Any("honourable")) || Any("peacemaker", "benefactor", "magnanimous"))
            return "honoured";
        if (Any("drunkard", "craven", "defeated", "simple", "lunatic", "timid"))
            return "mocked";
        if (c.AgeIn(year) >= 50 && Skill(c, CharSkills.Learning, year) >= 9)
            return "called_wise";
        if (Skill(c, CharSkills.Piety, year) >= 9)
            return "called_pious";
        if (c.AgeIn(year) >= 40 && Skills(c, year).Average() >= 5.5)
            return "respected";
        return null;
    }

    /// <summary>Replaces a person's reputation with another (or none).</summary>
    public void SetReputation(Character c, string? reputation)
    {
        var cat = TraitCatalog.Instance;
        if (reputation != null && c.Has(reputation))
            return;
        if (c.Traits.RemoveAll(id => cat[id]?.Kind == TraitDef.Reputation) > 0)
            c.HealthCache = c.FertilityCache = double.NaN;
        if (reputation != null)
            AddTrait(c, reputation);
    }

    // --- Bonds ---------------------------------------------------------------------------

    const int MaxBonds = 4;
    const double BondChance = 0.02;   // per adult at court per year

    /// <summary>
    /// People at the same court become friends, rivals or lovers over the
    /// years, by how well their natures fit; rivals who come to hate each
    /// other become each other's nemesis.
    /// </summary>
    void FormBonds(int year, Dictionary<int, int> courts, Random rng)
    {
        var byCourt = courts.GroupBy(kv => kv.Value, kv => Characters[kv.Key])
            .ToDictionary(g => g.Key, g => g.Where(c => c.IsAlive && c.AgeIn(year) >= AdultAge).ToList());
        foreach (var members in byCourt.Values)
        {
            if (members.Count < 2)
                continue;
            foreach (var a in members)
            {
                if (a.Bonds.Count >= MaxBonds || rng.NextDouble() >= BondChance)
                    continue;
                var b = members[rng.Next(members.Count)];
                if (b.Id == a.Id || b.Bonds.Count >= MaxBonds)
                    continue;
                var existing = a.Bonds.FirstOrDefault(x => x.With == b.Id);
                int fit = Fit(a, b);
                if (existing != null)
                {
                    if (existing.Kind == "rival" && fit <= -15)
                        Bind(a, b, "nemesis", "nemesis", year);
                    continue;
                }
                if (fit >= 10)
                {
                    bool lovers = a.IsMale != b.IsMale && a.SpouseId != b.Id && (a.Has("lustful") || b.Has("lustful"));
                    bool warriors = a.IsMale && b.IsMale && a.Traits.Concat(b.Traits).Count(t => t is "victor" or "wounded_veteran" or "brave") >= 2;
                    string kind = lovers ? "lover" : warriors && rng.NextDouble() < 0.3 ? "sworn_brother" : "friend";
                    Bind(a, b, kind, kind, year);
                }
                else if (fit <= -10)
                    Bind(a, b, "rival", "rival", year);
            }
        }
    }

    /// <summary>How well two natures fit, quickly: shared and opposed traits, kin, people and faith.</summary>
    int Fit(Character a, Character b)
    {
        var cat = TraitCatalog.Instance;
        int fit = a.Culture == b.Culture ? 3 : -3;
        foreach (var x in a.Traits)
        {
            if (b.Traits.Contains(x) && cat[x]?.Kind is TraitDef.Personality or TraitDef.Faith or TraitDef.Lifestyle)
                fit += 5;
            foreach (var y in b.Traits)
                if (cat.Opposed(x, y) && cat[x]?.Kind is TraitDef.Personality or TraitDef.Faith)
                    fit -= 6;
        }
        if (a.Has("ambitious") && b.Has("ambitious")) fit -= 6;   // two wolves
        return fit;
    }

    /// <summary>Makes a bond both ways, replacing any they had.</summary>
    public void Bind(Character a, Character b, string aHolds, string bHolds, int year)
    {
        a.Bonds.RemoveAll(x => x.With == b.Id);
        b.Bonds.RemoveAll(x => x.With == a.Id);
        a.Bonds.Add(new Bond(aHolds, b.Id, year));
        b.Bonds.Add(new Bond(bHolds, a.Id, year));
    }

    /// <summary>How much a person's health raises or lowers their yearly chance of death.</summary>
    public double HealthFactor(Character c)
    {
        if (!double.IsNaN(c.HealthCache))
            return c.HealthCache;
        var cat = TraitCatalog.Instance;
        double f = 1;
        foreach (var id in c.Traits)
            if (cat[id] is { } t)
                f *= t.Health;
        return c.HealthCache = f;
    }

    double OwnFertility(Character c)
    {
        if (!double.IsNaN(c.FertilityCache))
            return c.FertilityCache;
        var cat = TraitCatalog.Instance;
        double f = 1;
        foreach (var id in c.Traits)
            if (cat[id] is { } t)
                f *= t.Fertility;
        return c.FertilityCache = f;
    }

    /// <summary>How fertile a couple is together (barren is none).</summary>
    public double Fertility(Character a, Character b) => OwnFertility(a) * OwnFertility(b);

    /// <summary>What a natural death was: old age for the old, mostly disease, sometimes an accident (hunters and riders more).</summary>
    public string NaturalCause(Character c, int age, Random rng)
    {
        double accident = age < AdultAge ? 0.08 : c.IsMale ? 0.15 : 0.06;
        if (c.Has("hunter") || c.Has("horseman") || c.Has("charioteer") || c.Has("reckless"))
            accident *= 2;
        if (age >= 60 && rng.NextDouble() < Math.Min(0.85, (age - 50) / 30.0))
            return DeathCauses.OldAge;
        return rng.NextDouble() < accident ? DeathCauses.Accident : DeathCauses.Disease;
    }

    /// <summary>The chronicle's words for a death: "of a fever", "in childbirth".</summary>
    public static string HowDied(string? cause) => cause switch
    {
        DeathCauses.OldAge => "of old age",
        DeathCauses.Disease => "of a sickness",
        DeathCauses.Childbirth => "in childbirth",
        DeathCauses.Battle => "in battle",
        DeathCauses.Execution => "at the executioner's hand",
        DeathCauses.Murder => "murdered",
        DeathCauses.Accident => "in an accident",
        _ => "",
    };

    // --- Opinions ----------------------------------------------------------------------

    /// <summary>
    /// What one person thinks of another, with the reasons (decision "What
    /// characters think of each other"): family ties, shared and opposed
    /// natures, culture, how the other rules, looks and health, and what
    /// they remember.
    /// </summary>
    public List<(string Reason, int Value)> OpinionReasons(Character a, Character b, int year)
    {
        Develop(a);
        Develop(b);
        var r = new List<(string, int)>();
        if (a.SpouseId == b.Id) r.Add(("husband or wife", 25));
        if (b.FatherId == a.Id || b.MotherId == a.Id) r.Add(("their child", 30));
        if (a.FatherId == b.Id || a.MotherId == b.Id) r.Add(("their parent", 20));
        if (a.Id != b.Id && ((a.FatherId != -1 && a.FatherId == b.FatherId) || (a.MotherId != -1 && a.MotherId == b.MotherId)))
            r.Add(("brother or sister", 10));
        var cat = TraitCatalog.Instance;
        foreach (var id in a.Traits.Where(b.Traits.Contains))
            if (cat[id]?.Kind == TraitDef.Personality)
                r.Add(($"both {cat[id]!.Name.ToLowerInvariant()}", 5));
        foreach (var x in a.Traits)
            foreach (var y in b.Traits)
                if (cat.Opposed(x, y) && cat[x]?.Kind == TraitDef.Personality)
                    r.Add(($"{cat[x]!.Name.ToLowerInvariant()}, and the other {cat[y]!.Name.ToLowerInvariant()}", -10));
        r.Add(a.Culture == b.Culture ? ("of the same people", 5) : ($"a foreigner ({b.Culture})", -5));
        if (Realms.Values.Any(realm => realm.RulerId == b.Id))
            foreach (var (trait, v) in new[] { ("just", 10), ("arbitrary", -10), ("cruel", -15), ("sadistic", -20), ("generous", 10),
                         ("greedy", -10), ("beloved", 10), ("hated", -15), ("tyrant", -20), ("honourable", 5), ("charismatic", 10) })
                if (b.Has(trait))
                    r.Add(($"{cat[trait]?.Name.ToLowerInvariant() ?? trait} as a ruler", v));
        var faithA = a.Traits.FirstOrDefault(t => cat[t]?.Group == TraitDef.Religion);
        var faithB = b.Traits.FirstOrDefault(t => cat[t]?.Group == TraitDef.Religion);
        if (faithA != null && faithB != null)
        {
            if (faithA == faithB) r.Add(("of the same faith", 10));
            else
            {
                r.Add(($"worships other gods ({cat[faithB]!.Name.ToLowerInvariant()})", a.Has("tolerant") ? 0 : -10));
                if (a.Has("zealous")) r.Add(("a zealot's scorn for other gods", -15));
            }
        }
        foreach (var id in a.Traits.Where(b.Traits.Contains))
            if (cat[id]?.Group == TraitDef.Philosophy)
                r.Add(($"both {cat[id]!.Name.ToLowerInvariant()}", 5));
        foreach (var id in b.Traits)
            if (cat[id] is { Kind: TraitDef.Reputation, Opinion: not 0 } rep)
                r.Add((rep.Name.ToLowerInvariant(), rep.Opinion));
        foreach (var bond in a.Bonds.Where(x => x.With == b.Id))
            if (cat.Bond(bond.Kind) is { } bd)
                r.Add((bd.Name.ToLowerInvariant(), bd.Value));
        if (b.Has("handsome") || b.Has("beautiful")) r.Add(("good-looking", 5));
        if (b.Has("ugly")) r.Add(("ugly", -5));
        if (b.Has("leper")) r.Add(("a leper", -20));
        foreach (var m in a.Memories.Where(m => m.Of == b.Id))
        {
            int years = year - m.Year;
            int v = years > 20 ? m.Value / 2 : m.Value;   // old wrongs and favours fade
            if (v != 0)
                r.Add((m.Reason, v));
        }
        return r;
    }

    public int Opinion(Character a, Character b, int year) => Math.Clamp(OpinionReasons(a, b, year).Sum(x => x.Value), -100, 100);

    public void Remember(Character who, Character of, string reason, int value, int year) =>
        who.Memories.Add(new Memory(of.Id, reason, value, year));

    // --- What a ruler does to the realm -------------------------------------------------

    /// <summary>
    /// The realm effects of its ruler (the same keys as technologies): each
    /// skill point above or below an ordinary 5, and the traits they hold.
    /// </summary>
    public Dictionary<string, double> RulerEffects(Character ruler, int year)
    {
        var fx = new Dictionary<string, double>();
        void Add(string key, double v)
        {
            if (v != 0)
                fx[key] = fx.GetValueOrDefault(key) + v;
        }
        int D(int s) => Skill(ruler, s, year) - OrdinarySkill;
        Add("tax", 0.01 * D(CharSkills.Stewardship));
        Add("research", 0.02 * D(CharSkills.Learning));
        Add("corruption", -0.005 * D(CharSkills.Statecraft));
        Add("integration", 0.01 * D(CharSkills.Statecraft));
        Add("unrest", -0.005 * D(CharSkills.Oratory) - 0.004 * D(CharSkills.Piety));
        Add("siege", 0.02 * D(CharSkills.Siegecraft));
        Add("reach_km", 5.0 * D(CharSkills.Logistics));
        Add("manpower", 0.01 * D(CharSkills.Leadership));
        var cat = TraitCatalog.Instance;
        foreach (var id in ruler.Traits)
            if (cat[id] is { } t)
                foreach (var (k, v) in t.Effects)
                    Add(k, v);
        return fx;
    }
}
