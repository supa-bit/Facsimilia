using System;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.Tests;

/// <summary>The mortality curve and yearly advancement.</summary>
public partial class TimeTest : TestRunner
{
    protected override Task Run()
    {
        var reg = new CharacterRegistry();
        Check(CharacterRegistry.DeathChanceForAge(30) == 0.0, "no deaths under 40");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(50) - 0.10) < 1e-9, "10% at 50");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(90) - 0.35) < 1e-9, "35% at 90");
        Check(Math.Abs(CharacterRegistry.DeathChanceForAge(200) - 0.35) < 1e-9, "capped at 35%");

        // A ruler under 40 has exactly 0% death chance, whatever the RNG does.
        var ruler = reg.CreateCharacter("Young Ruler", "male", 1000);
        reg.CreateDynasty("House Young", ruler);
        reg.CreateRealm("Young Realm", ruler, SuccessionLaw.Primogeniture, Colors.Gray);
        for (int year = 1000; year < 1035; year++)
            Check(reg.AdvanceYear(year).Count == 0, $"something happened in {year}");
        Check(ruler.IsAlive, "the young ruler died");

        // An old, childless ruler with an RNG seeded to roll a death: a crisis.
        var old = reg.CreateCharacter("Old Ruler", "male", 900);
        reg.CreateDynasty("House Old", old);
        var oldRealm = reg.CreateRealm("Old Realm", old, SuccessionLaw.Primogeniture, Colors.Gray);
        var rng = new RandomNumberGenerator { Seed = 1 };
        Check(rng.Randf() < CharacterRegistry.DeathChanceForAge(999 - 900), "seed 1 doesn't force a death");
        rng.Seed = 1;
        var events = reg.AdvanceYear(999, rng);
        Check(events.Count == 1 && events[0].Contains("succession crisis", StringComparison.OrdinalIgnoreCase),
            "expected one succession-crisis event");
        Check(!old.IsAlive && oldRealm.RulerId == old.Id, "the old ruler should be dead with no successor");

        Finish("Time/mortality tests passed: age-40 threshold holds, curve caps at 35%, and a forced death " +
            "correctly produced a succession crisis.");
        return Task.CompletedTask;
    }
}
