using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The other realms act: over 30 years from 300 BC, with the player
/// (Nabatea) doing nothing, history's campaigns begin on time (Demetrius
/// against Macedon in 295 BC), realms raise armies and fight, wars end in
/// peace, the great powers survive a generation and most realms stand, it all survives a save,
/// and a year stays quick.
/// </summary>
public partial class AiTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("nabatea");
        map.StartGame();
        var events = new List<ChronicleEvent>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int y = 0; y < 30; y++)
            events.AddRange(map.AdvanceYear());
        double msPerYear = clock.ElapsedMilliseconds / 30.0;

        Check(events.Any(e => e.Kind == ChronicleKind.War && e.Text.Contains("Demetrius")),
            "Demetrius should go to war for Macedon in 295 BC");
        Check(events.Any(e => e.Cue != null && e.Cue.StartsWith("war_begin:")), "history's wars should be announced by the narrator");
        Check(events.Any(e => e.Kind == ChronicleKind.Peace), "some war should have ended in peace");
        Check(events.Count(e => e.Kind == ChronicleKind.Conquest) > 0, "someone should have taken land");
        // Alliances (your answer "They seek allies against rivals and threats"): some are sworn, never with an enemy of the threat's side.
        var sworn = map.Game.Treaties.All.Where(t => t.Kind == Game.TreatyKind.Alliance && t.Against != 0).ToList();
        Check(events.Any(e => e.Text.Contains("swear an alliance against")), "some realms should seek allies against a threat in 30 years");
        Check(sworn.All(t => !map.Game.Treaties.Allied(t.A, t.Against) && !map.Game.Treaties.Allied(t.B, t.Against)) || sworn.Count == 0,
            "no realm should ally against a realm it is allied with");
        var census = map.RealmCensus();
        // Small peoples may fall, as the Samnites and Etruscans did to Rome by 280 BC; the great powers stand.
        foreach (string key in new[] { "rome", "carthage", "egypt", "seleucid", "lysimachus", "kush" })
            Check(census.ContainsKey(map.CivRealmIds[key]), $"{key} was wiped out within 30 years");
        int standing = WorldFixture.CivKeys.Count(k => census.ContainsKey(map.CivRealmIds[k]));
        Check(standing >= WorldFixture.CivKeys.Length * 3 / 4, $"only {standing} of {WorldFixture.CivKeys.Length} realms stand after 30 years");
        // The designer's tripwire: 1 second a year for the year's calculations (nothing drawn). The designer
        // raises it when the simulation needs more; it is never a reason to cut simulation.
        Check(msPerYear < 1000, $"a year takes {msPerYear:0} ms, over the designer's 1-second tripwire");

        // Peace sense: a fresh attacker short of its aims fights on; worn out, it wants peace; a beaten side wants it above all.
        // (Realms 90001 and 90002 have no ruler, so no temper weighs in.)
        var probe = new Game.War(90001, 90002, map.DemoYear, true);
        Check(map.PeaceWish(probe, 90001, goalOver: false, years: 0) < 50, "a fresh attacker short of its aims shouldn't want peace");
        probe.AttackerExhaustion = 100;
        Check(map.PeaceWish(probe, 90001, goalOver: false, years: 0) >= 50, "a worn-out attacker should want peace");
        probe.Score = 60;
        Check(map.PeaceWish(probe, 90002, goalOver: false, years: 0) >= 100, "a beaten defender should want peace above all");

        // Rulers' character: a bold ruler wants peace less than a cautious one.
        int carthage = map.CivRealmIds["carthage"];
        var hannibal = map.Registry.Characters[map.Registry.Realms[carthage].RulerId];
        var kept = hannibal.Traits.ToList();
        hannibal.Traits = new() { "brave", "reckless" };
        var war = new Game.War(carthage, 2, map.DemoYear, true) { AttackerExhaustion = 60 };
        double boldWish = map.PeaceWish(war, carthage, goalOver: false, years: 0);
        Check(map.Boldness(carthage) == 2, "brave and reckless should make a ruler bold");
        hannibal.Traits = new() { "cautious", "timid", "patient", "calm" };
        Check(map.Boldness(carthage) == -3 && map.PeaceWish(war, carthage, goalOver: false, years: 0) > boldWish, "a cautious ruler should want peace sooner");
        hannibal.Traits = kept;

        // Mid-war save and load keeps the wars.
        int wars = map.Game.Wars.All.Count;
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Wars.All.Count == wars, "wars weren't saved");

        Finish($"AI tests passed: 30 years with {events.Count(e => e.Kind == ChronicleKind.War)} wars declared, " +
            $"{events.Count(e => e.Kind == ChronicleKind.Peace)} peaces, {events.Count(e => e.Kind == ChronicleKind.Conquest)} " +
            $"conquest reports; {events.Count(e => e.Kind == ChronicleKind.Revolt)} revolts; {sworn.Count} alliances sworn against threats; {standing} of {WorldFixture.CivKeys.Length} realms stand; {msPerYear:0} ms a year.");
        map.Free();
        map2.Free();
    }
}
