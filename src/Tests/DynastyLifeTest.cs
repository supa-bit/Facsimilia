using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Living dynasties: who inherits when the obvious heir is gone, the 300 BC
/// families, and centuries of births, marriages, deaths and successions
/// across every realm without the families dying out or ballooning.
/// </summary>
public partial class DynastyLifeTest : TestRunner
{
    protected override Task Run()
    {
        CheckRepresentation();
        CheckCollateral();
        CheckMotherLine();
        CheckStartingFamilies();
        CheckCenturies();
        return Task.CompletedTask;
    }

    static (CharacterRegistry, Character King, Character Queen) Family(string house)
    {
        var reg = new CharacterRegistry();
        var king = reg.CreateCharacter("King", "male", 1000);
        reg.CreateDynasty(house, king);
        var queen = reg.CreateCharacter("Queen", "female", 1002);
        CharacterRegistry.Marry(king, queen);
        return (reg, king, queen);
    }

    static Character Married(CharacterRegistry reg, Character c, string name)
    {
        var spouse = reg.CreateCharacter(name, c.IsMale ? "female" : "male", c.BirthYear);
        CharacterRegistry.Marry(c, spouse);
        return spouse;
    }

    /// <summary>A dead eldest son's children come before his younger brother - even a daughter, under male preference.</summary>
    void CheckRepresentation()
    {
        var (reg, king, queen) = Family("House A");
        var eldest = reg.HaveChild(queen, king, "Eldest", "male", 1020);
        var younger = reg.HaveChild(queen, king, "Younger", "male", 1023);
        var wife = Married(reg, eldest, "Wife");
        var granddaughter = reg.HaveChild(wife, eldest, "Granddaughter", "female", 1042);
        var realm = reg.CreateRealm("A", king, SuccessionLaw.MalePreferencePrimogeniture, Colors.Gray);
        Check(reg.ResolveHeir(realm) == eldest, "the eldest son should be heir while alive");
        reg.Kill(eldest, 1045);
        Check(reg.ResolveHeir(realm) == granddaughter, "the dead eldest son's daughter should come before his younger brother");
        reg.Kill(granddaughter, 1046);
        Check(reg.ResolveHeir(realm) == younger, "with the eldest line gone, the younger son inherits");
        var heir = reg.HandleRulerDeath(realm, 1050);
        Check(heir == younger && realm.RulerId == younger.Id, "the younger son didn't take the throne");
    }

    /// <summary>A childless ruler is succeeded by a nephew, and with no nephews by a cousin.</summary>
    void CheckCollateral()
    {
        var (reg, king, queen) = Family("House B");
        var ruler = reg.HaveChild(queen, king, "Ruler", "male", 1020);
        var brother = reg.HaveChild(queen, king, "Brother", "male", 1022);
        var sister = reg.HaveChild(queen, king, "Sister", "female", 1024);
        var bWife = Married(reg, brother, "BrotherWife");
        var nephew = reg.HaveChild(bWife, brother, "Nephew", "male", 1045);
        reg.Kill(king, 1040);
        var realm = reg.CreateRealm("B", ruler, SuccessionLaw.MalePreferencePrimogeniture, Colors.Gray);
        reg.Kill(brother, 1050);
        Check(reg.ResolveHeir(realm) == nephew, "the dead brother's son should inherit before the sister");
        reg.Kill(nephew, 1051);
        Check(reg.ResolveHeir(realm) == sister, "with no male line left, the sister inherits");
        reg.Kill(sister, 1052);
        reg.Kill(queen, 1052);
        Check(reg.ResolveHeir(realm) == null, "nobody is left, the line has ended");
    }

    /// <summary>A princess who marries outside the great houses keeps her children in her dynasty and line.</summary>
    void CheckMotherLine()
    {
        var (reg, king, queen) = Family("House C");
        var princess = reg.HaveChild(queen, king, "Princess", "female", 1020);
        var brother = reg.HaveChild(queen, king, "Brother", "male", 1022);
        var noble = Married(reg, princess, "Noble");
        var grandson = reg.HaveChild(princess, noble, "Grandson", "male", 1040);
        Check(grandson.DynastyId == king.DynastyId, "the princess's son should be of her house");
        var realm = reg.CreateRealm("C", grandson, SuccessionLaw.Primogeniture, Colors.Gray);
        // The childless grandson's heir is found up his mother's side: his uncle.
        reg.Kill(princess, 1060);
        Check(reg.ResolveHeir(realm) == brother, "the search should go up the royal side, to the uncle");
    }

    static IEnumerable<CivSpec> AllCivs => MapView.RealCivs.Concat(MapView.FrontierZones);

    static CharacterRegistry StartingWorld()
    {
        var reg = new CharacterRegistry();
        foreach (var spec in AllCivs)
            MapView.CreateStartingRealm(reg, spec.RealmName ?? spec.Key, spec);
        return reg;
    }

    void CheckStartingFamilies()
    {
        var reg = StartingWorld();
        string HeirOf(string key) => reg.ResolveHeir(reg.Realms.Values.First(r => r.Name == key))?.Name ?? "none";
        Check(HeirOf("egypt") == "Ptolemaios", $"Egypt's heir is {HeirOf("egypt")}, expected the son Ptolemaios");
        Check(HeirOf("kush") == "Shanakdakhete", $"Kush's heir is {HeirOf("kush")}, expected the elder daughter");
        Check(HeirOf("antigonus") == "Demetrios", $"Antigonus's heir is {HeirOf("antigonus")}");
        var antigonus = reg.Realms.Values.First(r => r.Name == "antigonus");
        var demetrios = reg.ResolveHeir(antigonus)!;
        Check(reg.LivingChildren(demetrios).Select(c => c.Name).SequenceEqual(new[] { "Antigonos", "Stratonike" }),
            "Demetrios's children are missing");
        foreach (var realm in reg.Realms.Values)
        {
            var ruler = reg.Characters[realm.RulerId];
            Check(ruler.AgeIn(MapView.StartYear) is >= 30 and <= 85, $"{realm.Name}'s ruler is {ruler.AgeIn(MapView.StartYear)}");
            Check(reg.ResolveHeir(realm) != null, $"{realm.Name} starts without an heir");
        }
        int[] ages = reg.Realms.Values.Select(r => reg.Characters[r.RulerId].AgeIn(MapView.StartYear)).Distinct().ToArray();
        Check(ages.Length >= 8, "starting rulers' ages should vary");
    }

    /// <summary>Every realm, 300 BC to AD 1700: always a living ruler, families that stay a sensible size.</summary>
    void CheckCenturies()
    {
        var reg = StartingWorld();
        var rng = new RandomNumberGenerator { Seed = 42 };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var counts = new Dictionary<ChronicleKind, int>();
        int maxLiving = 0, years = 0;
        for (int year = MapView.StartYear + 1; year <= 1700; year++)
        {
            if (year == 0)
                continue;
            years++;
            foreach (var e in reg.AdvanceYear(year, rng))
                counts[e.Kind] = counts.GetValueOrDefault(e.Kind) + 1;
            foreach (var realm in reg.Realms.Values)
            {
                var ruler = reg.Characters[realm.RulerId];
                if (!ruler.IsAlive)
                {
                    Check(false, $"{realm.Name} has a dead ruler in {year}");
                    return;
                }
            }
            maxLiving = Math.Max(maxLiving, reg.Characters.Values.Count(c => c.IsAlive));
        }
        double ms = clock.Elapsed.TotalMilliseconds / years;
        int realms = reg.Realms.Count;
        Check(maxLiving < realms * 120, $"{maxLiving} people alive at once: the families are ballooning");
        Check(counts.GetValueOrDefault(ChronicleKind.Birth) > realms * 100, "too few births to sustain the families");
        Check(counts.GetValueOrDefault(ChronicleKind.Succession) > realms * 40, "too few successions");
        int newHouses = counts.GetValueOrDefault(ChronicleKind.NewHouse);
        int successions = counts.GetValueOrDefault(ChronicleKind.Succession);
        Check(newHouses < successions / 4, $"{newHouses} new houses in {successions} successions: houses fall too often");
        Check(newHouses > realms * 2, $"only {newHouses} new houses in two thousand years: houses never fall");
        Check(ms < 5, $"a year took {ms:F1} ms");

        // Names come from the right tradition, or from a grandparent (a Scythian
        // princess's daughter may be named for her Scythian grandmother).
        bool NamedForGrandparent(Character c) =>
            new[] { reg.Father(c), reg.Mother(c) }.OfType<Character>()
                .SelectMany(p => new[] { reg.Father(p), reg.Mother(p) }).OfType<Character>().Any(g => g.Name == c.Name);
        foreach (var c in reg.Characters.Values.Where(c => c.BirthYear > MapView.StartYear))
            if (!Names.Pool(c.Culture, c.IsMale).Contains(c.Name) && !NamedForGrandparent(c))
            {
                Check(false, $"{c.Name} isn't a {c.Culture} {(c.IsMale ? "male" : "female")} name");
                break;
            }

        // A world with two thousand years of family history survives a save.
        var loaded = new CharacterRegistry();
        loaded.LoadFromDict((Godot.Collections.Dictionary)Json.ParseString(Json.Stringify(reg.ToDict())));
        Check(loaded.Characters.Count == reg.Characters.Count, "characters lost in the save");
        foreach (var realm in reg.Realms.Values)
        {
            var copy = loaded.Realms[realm.Id];
            Check(copy.RulerId == realm.RulerId && copy.Culture == realm.Culture, $"{realm.Name} changed in the save");
            Check(loaded.ResolveHeir(copy)?.Id == reg.ResolveHeir(realm)?.Id, $"{realm.Name}'s heir changed in the save");
        }
        Check(loaded.Characters.Values.All(c => c.Culture == reg.Characters[c.Id].Culture), "cultures changed in the save");

        double avgReign = (double)years * realms / (successions + newHouses + realms);
        Finish($"Dynasty life tests passed: representation, collateral and mother's-line succession; the 300 BC " +
            $"families; {years} years across {realms} realms with {counts.GetValueOrDefault(ChronicleKind.Birth):N0} births, " +
            $"{counts.GetValueOrDefault(ChronicleKind.Marriage):N0} marriages, {successions:N0} " +
            $"successions and {newHouses} new houses (average reign {avgReign:F0} years); at most {maxLiving} people alive " +
            $"at once; {ms:F2} ms per year; {reg.Characters.Count:N0} characters survived a save.");
    }
}
