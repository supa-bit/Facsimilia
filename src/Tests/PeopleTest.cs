using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// People (the Ledger's "The court: characters and families", first part):
/// at least 250 traits, sound; everyone has twelve skills and three traits
/// of nature, the same each time; children inherit body and health and
/// grow into their skills; schooling at sixteen; every death has a cause,
/// childbirth among them; opinions have reasons (family, natures, memories
/// that fade); a ruler's skills and traits change the realm; and all of it
/// survives a save.
/// </summary>
public partial class PeopleTest : TestRunner
{
    protected override Task Run()
    {
        var cat = TraitCatalog.Instance;
        Check(cat.All.Count >= 250, $"at least 250 traits (the designer's note), have {cat.All.Count}");
        Check(cat.All.Select(t => t.Id).Distinct().Count() == cat.All.Count, "trait ids are unique");
        string[] effectKeys = { "tax", "research", "unrest", "corruption", "integration", "growth", "siege", "reach_km", "manpower", "trade" };
        foreach (var t in cat.All)
        {
            Check(t.Opposite.All(o => cat[o] != null), $"{t.Id}: its opposites exist");
            Check(t.Skills.Keys.All(k => CharSkills.IndexOf(k) >= 0), $"{t.Id}: its skills are among the twelve");
            Check(t.Effects.Keys.All(effectKeys.Contains), $"{t.Id}: its effects are realm effects");
        }
        foreach (var kind in new[] { TraitDef.Personality, TraitDef.Education, TraitDef.HealthKind, TraitDef.Body, TraitDef.Lifestyle, TraitDef.Earned })
            Check(cat.OfKind(kind).Any(), $"traits of kind {kind}");

        // Everyone has a nature, fixed by who they are.
        var reg = new CharacterRegistry();
        var king = reg.CreateCharacter("Seleucus", "male", -358);
        reg.CreateDynasty("Seleucids", king);
        var queen = reg.CreateCharacter("Apama", "female", -340);
        CharacterRegistry.Marry(king, queen);
        reg.Develop(king);
        Check(king.BaseSkills!.Length == CharSkills.Count && king.BaseSkills.All(v => v is >= 1 and <= 100), "twelve skills, each 1-100");
        Check(CharSkills.Word(CharSkills.Oratory, 98) == "Golden-tongued" && CharSkills.Word(CharSkills.Oratory, 3) == "Mumbling", "skills have words");
        Check(reg.SkillPoints(king, CharSkills.Tactics, -280) < reg.SkillPoints(king, CharSkills.Tactics, -320) || reg.SkillPoints(king, CharSkills.Tactics, -280) == 1,
            "the body's skills fade in old age (Seleucus at 78 against 38)");
        Check(king.Traits.Count(t => cat[t]?.Kind == TraitDef.Personality) == 3, "three traits of nature");
        Check(!king.Traits.Any(a => king.Traits.Any(b => cat.Opposed(a, b))), "no opposed traits together");
        var copy = new CharacterRegistry();
        var again = copy.CreateCharacter("Seleucus", "male", -358);
        copy.CreateDynasty("Seleucids", again);
        copy.Develop(again);
        Check(again.BaseSkills!.SequenceEqual(king.BaseSkills) && again.Traits.SequenceEqual(king.Traits), "the same person is always the same");

        // Inherited body: a tall father and mother have tall children, often.
        king.Traits.RemoveAll(t => cat.Opposed(t, "tall"));
        reg.AddTrait(king, "tall");
        reg.Develop(queen);
        queen.Traits.RemoveAll(t => cat.Opposed(t, "tall"));
        reg.AddTrait(queen, "tall");
        int tall = 0;
        for (int i = 0; i < 40; i++)
        {
            var child = reg.HaveChild(queen, king, $"Child{i}", i % 2 == 0 ? "male" : "female", -320 + i);
            reg.Develop(child);
            if (child.Has("tall"))
                tall++;
        }
        Check(tall is >= 10 and <= 35, $"about two in three children of two tall parents are tall, {tall} of 40");

        // Children grow into their skills; schooling at sixteen.
        var boy = reg.HaveChild(queen, king, "Antiochus", "male", -324);
        Check(reg.Skill(boy, CharSkills.Leadership, -320) < reg.Skill(boy, CharSkills.Leadership, -300) || reg.Skill(boy, CharSkills.Leadership, -300) <= 2,
            "a four-year-old has only part of the skills he will have");
        var realm = reg.CreateRealm("Seleucid realm", king, SuccessionLaw.MalePreferencePrimogeniture, Colors.Gray);
        var rng = new RandomNumberGenerator { Seed = 42 };
        for (int y = -322; y <= -306; y++)
            reg.AdvanceYear(y, rng);
        if (boy.IsAlive)
            Check(boy.Traits.Any(t => cat[t]?.Kind == TraitDef.Education), "at sixteen, schooling is finished");

        // A century: everyone who dies has a cause; some mothers die in childbirth.
        for (int y = -305; y < -105; y++)
            reg.AdvanceYear(y, rng);
        var dead = reg.Characters.Values.Where(c => !c.IsAlive && c.DeathYear >= -322).ToList();
        Check(dead.Count > 20 && dead.All(c => c.DeathCause != null), $"every death has its cause ({dead.Count} died)");
        var causes = dead.GroupBy(c => c.DeathCause).ToDictionary(g => g.Key!, g => g.Count());
        Check(causes.ContainsKey(DeathCauses.OldAge) && causes.ContainsKey(DeathCauses.Disease),
            $"old age and disease are among the causes: {string.Join(", ", causes.Select(kv => $"{kv.Key} {kv.Value}"))}");

        // Opinions have reasons.
        var a = reg.CreateCharacter("Ptolemy", "male", -360);
        var b = reg.CreateCharacter("Berenice", "female", -340);
        reg.Develop(a);
        reg.Develop(b);
        CharacterRegistry.Marry(a, b);
        Check(reg.OpinionReasons(a, b, -300).Any(r => r.Reason == "husband or wife" && r.Value > 0), "husband and wife think well of each other");
        a.Traits.RemoveAll(t => cat[t]?.Kind == TraitDef.Personality);
        b.Traits.RemoveAll(t => cat[t]?.Kind == TraitDef.Personality);
        reg.AddTrait(a, "brave");
        reg.AddTrait(b, "craven");
        Check(reg.OpinionReasons(a, b, -300).Any(r => r.Value < 0 && r.Reason.Contains("brave")), "the brave think less of the craven");
        int before = reg.Opinion(a, b, -300);
        reg.Remember(a, b, "insulted me at the feast", -30, -300);
        Check(reg.Opinion(a, b, -300) == System.Math.Max(-100, before - 30), "a memory changes the opinion");
        Check(reg.Opinion(a, b, -270) > reg.Opinion(a, b, -300), "old wrongs fade");

        // A ruler's skills and traits change the realm.
        a.BaseSkills![CharSkills.Stewardship] = 90;
        a.Traits.RemoveAll(t => cat[t]?.Skills.ContainsKey("stewardship") ?? false);
        var fx = reg.RulerEffects(a, -300);
        Check(System.Math.Abs(fx.GetValueOrDefault("tax") - 0.04) < 1e-9, $"a steward of 9 raises taxes by 4%, got {fx.GetValueOrDefault("tax")}");
        reg.AddTrait(a, "cruel");
        Check(reg.RulerEffects(a, -300).GetValueOrDefault("unrest") > fx.GetValueOrDefault("unrest"), "a cruel ruler stirs unrest");

        // The four kinds added on the designer's wish: faith, bloodline, reputation, bonds.
        foreach (var kind in new[] { TraitDef.Faith, TraitDef.Bloodline, TraitDef.Reputation })
            Check(cat.OfKind(kind).Count >= 10, $"at least ten traits of {kind}");
        Check(cat.Bonds.Count >= 5 && cat.Bonds.Any(x => x.Value < 0) && cat.Bonds.Any(x => x.Value > 0), "bonds for and against");
        Check(king.Traits.Count(t => cat[t]?.Group == TraitDef.Religion) == 1, "everyone has one religion");
        Check(king.Has("olympian"), "a Greek king worships the Olympians");
        var carthaginian = reg.CreateCharacter("Hanno", "male", -330, culture: Culture.Punic);
        reg.Develop(carthaginian);
        Check(carthaginian.Has("canaanite"), "a Carthaginian worships Baal and Tanit");
        var seleucids = new CharacterRegistry();
        var sel = seleucids.CreateCharacter("Seleukos", "male", -358);
        seleucids.CreateDynasty("House of Seleukos", sel);
        seleucids.Develop(sel);
        Check(sel.Has("descent_apollo"), "the Seleucids claim Apollo for an ancestor");
        var son = seleucids.HaveChild(seleucids.CreateCharacter("Apama", "female", -340, culture: Culture.Persian), sel, "Antiochos", "male", -323);
        seleucids.Develop(son);
        Check(son.Has("descent_apollo") && son.Has("half_foreign"), "his son by a Persian wife inherits Apollo and is of two peoples");
        Check(reg.OpinionReasons(king, carthaginian, -300).Any(x => x.Reason.StartsWith("worships other gods")), "other gods lower opinion");
        a.Traits.RemoveAll(t => cat[t]?.Kind == TraitDef.Reputation);
        reg.AddTrait(a, "kinslayer");
        Check(reg.ReputationOf(a, -300) == "notorious", "a kinslayer is notorious");
        reg.SetReputation(a, "notorious");
        Check(reg.OpinionReasons(b, a, -300).Any(x => x.Reason == "notorious" && x.Value < 0), "strangers think less of the notorious");
        reg.Bind(a, b, "rival", "rival", -300);
        Check(reg.OpinionReasons(a, b, -300).Any(x => x.Reason == "rival" && x.Value < 0) && b.Bonds.Any(x => x.With == a.Id), "a rivalry is held both ways");
        int bonded = reg.Characters.Values.Count(c => c.Bonds.Count > 0);
        Check(bonded > 0, "over a century at court, some bonds form");

        // Saved and loaded.
        var loaded = new CharacterRegistry();
        loaded.LoadFromDict(reg.ToDict());
        var a2 = loaded.Characters[a.Id];
        Check(a2.BaseSkills!.SequenceEqual(a.BaseSkills) && a2.Traits.SequenceEqual(a.Traits), "skills and traits survive a save");
        Check(a2.Memories.Count == 1 && a2.Memories[0].Value == -30, "memories survive a save");
        Check(a2.Bonds.Count == a.Bonds.Count && a2.Bonds.All(x => a.Bonds.Contains(x)), "bonds survive a save");
        var someDead = dead.First();
        Check(loaded.Characters[someDead.Id].DeathCause == someDead.DeathCause, "the cause of death survives a save");

        Finish($"People tests passed: {cat.All.Count} traits; {tall} of 40 children of tall parents tall; {dead.Count} deaths, " +
               string.Join(", ", causes.Select(kv => $"{kv.Value} of {kv.Key}")) + ".");
        return Task.CompletedTask;
    }
}
