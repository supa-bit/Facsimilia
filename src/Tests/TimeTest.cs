using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.Tests;

/// <summary>The mortality curve and yearly advancement.</summary>
public partial class TimeTest : TestRunner
{
    protected override Task Run()
    {
        // The life table's fixed points.
        Check(CharacterRegistry.DeathChanceForAge(-5) == 0.0, "the unborn can't die");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(0) - 0.08) < 1e-9, "8% in the first year");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(30) - 0.008) < 1e-9, "0.8% at 30");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(40) - 0.01) < 1e-9, "1% at 40");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(200) - 0.35) < 1e-9, "capped at 35%");
        for (int age = 40; age < 120; age++)
            Check(CharacterRegistry.DeathChanceForAge(age + 1) >= CharacterRegistry.DeathChanceForAge(age),
                $"death chance falls from {age} to {age + 1}");

        // What the curve means: how many 40-year-olds reach 60 and 70, and how
        // many newborns reach 5 (the doc comment on DeathChanceForAge).
        double Survive(int from, int to)
        {
            double alive = 1;
            for (int age = from; age < to; age++)
                alive *= 1 - CharacterRegistry.DeathChanceForAge(age);
            return alive;
        }
        double to60 = Survive(40, 60), to70 = Survive(40, 70), to5 = Survive(0, 5);
        Check(to60 is > 0.58 and < 0.68, $"{to60:P0} of 40-year-olds reach 60, expected about 63%");
        Check(to70 is > 0.28 and < 0.40, $"{to70:P0} of 40-year-olds reach 70, expected about a third");
        Check(to5 is > 0.80 and < 0.86, $"{to5:P0} of newborns reach 5, expected about five in six");

        // An old, childless ruler dies within a few years; with nobody left a
        // new house takes the throne, of the realm's own culture.
        var reg = new CharacterRegistry();
        var old = reg.CreateCharacter("Ateas", "male", 900, culture: Culture.Scythian);
        reg.CreateDynasty("House Ateas", old);
        var realm = reg.CreateRealm("Old Realm", old, SuccessionLaw.Primogeniture, Colors.Gray, Culture.Scythian);
        var rng = new RandomNumberGenerator { Seed = 1 };
        ChronicleEvent? newHouse = null;
        int year = 990;
        for (; year < 1030 && old.IsAlive; year++)
            newHouse = reg.AdvanceYear(year, rng).FirstOrDefault(e => e.Kind == ChronicleKind.NewHouse);
        Check(!old.IsAlive, "a 90-year-old survived 40 more years");
        Check(newHouse != null && newHouse.RealmId == realm.Id, "no new-house event when the line ended");
        var founder = reg.Characters[realm.RulerId];
        Check(founder.IsAlive && founder.DynastyId != old.DynastyId, "the new ruler isn't alive in a new house");
        Check(founder.Culture == Culture.Scythian && Names.Pool(Culture.Scythian, true).Contains(founder.Name),
            $"the new ruler {founder.Name} isn't Scythian");
        Check(reg.ResolveHeir(realm) != null, "the new house has no heir");

        Finish($"Time/mortality tests passed: {to60:P0} of 40-year-olds reach 60, {to70:P0} reach 70, " +
            $"{to5:P0} of newborns reach 5; a line that died out in {year - 1} was replaced by {newHouse!.Text}");
        return Task.CompletedTask;
    }
}
