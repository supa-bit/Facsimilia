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
        Check(events.Any(e => e.Kind == ChronicleKind.Peace), "some war should have ended in peace");
        Check(events.Count(e => e.Kind == ChronicleKind.Conquest) > 0, "someone should have taken land");
        var census = map.RealmCensus();
        // Small peoples may fall, as the Samnites and Etruscans did to Rome by 280 BC; the great powers stand.
        foreach (string key in new[] { "rome", "carthage", "egypt", "seleucid", "lysimachus", "kush" })
            Check(census.ContainsKey(map.CivRealmIds[key]), $"{key} was wiped out within 30 years");
        int standing = WorldFixture.CivKeys.Count(k => census.ContainsKey(map.CivRealmIds[k]));
        Check(standing >= WorldFixture.CivKeys.Length * 3 / 4, $"only {standing} of {WorldFixture.CivKeys.Length} realms stand after 30 years");
        Check(msPerYear < 600, $"a year takes {msPerYear:0} ms");

        // Mid-war save and load keeps the wars.
        int wars = map.Game.Wars.All.Count;
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Wars.All.Count == wars, "wars weren't saved");

        Finish($"AI tests passed: 30 years with {events.Count(e => e.Kind == ChronicleKind.War)} wars declared, " +
            $"{events.Count(e => e.Kind == ChronicleKind.Peace)} peaces, {events.Count(e => e.Kind == ChronicleKind.Conquest)} " +
            $"conquest reports; {standing} of {WorldFixture.CivKeys.Length} realms stand; {msPerYear:0} ms a year.");
        map.Free();
        map2.Free();
    }
}
