using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Plagues, famines and disasters: history's own strike at their dates
/// (the Rhodes earthquake of 226 BC, Vesuvius in AD 79) and kill most at
/// their centre; chance quakes come only along the quake belts; medicine
/// cuts plague deaths; and a century of chance leaves the world on course.
/// </summary>
public partial class DisasterTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var cat = DisasterCatalog.Instance;
        Check(cat.Events.Count >= 40, $"history should bring many disasters, got {cat.Events.Count}");
        var black = cat.Events.First(e => e.Year == 1347);
        Check(DisasterCatalog.DeathsAt(black, 0) > DisasterCatalog.DeathsAt(black, 2000), "deaths should fade from the centre");
        Check(cat.QuakeOdds(28.2, 36.4) > 0 && cat.QuakeOdds(-5, 45) == 0, "quakes should strike the Aegean, not Aquitaine");
        Check(DisasterCatalog.MedicineCut(0) == 0 && DisasterCatalog.MedicineCut(5) == DisasterCatalog.MaxMedicine, "medicine cut");

        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var pop = map.Population!;
        double start = pop.LandNodes.Sum(i => (double)pop.Pop[i]);
        int chance = 0;
        bool rhodes = false;
        for (int y = 0; y < 100; y++)
        {
            var ev = map.AdvanceYear().Where(e => e.Kind == ChronicleKind.Disaster).ToList();
            rhodes |= ev.Any(e => e.Text.Contains("Rhodes"));
            chance += ev.Count(e => e.Text.StartsWith("An "));
        }
        double end = pop.LandNodes.Sum(i => (double)pop.Pop[i]);
        Check(rhodes, "the Rhodes earthquake should strike in 226 BC");
        Check(end > start * 0.85, $"a century of disasters shouldn't wreck the world: {start:N0} -> {end:N0}");

        // Vesuvius: kill most near Pompeii.
        map.DemoYear = 79;
        int node = pop.NodeAtLonLat(14.45, 40.78, MapView.LonMin, MapView.LonMax, MapView.LatMin, MapView.LatMax);
        pop.Pop[node] = 10000;
        var events = map.DisastersYear();
        Check(events.Any(e => e.Text.Contains("Vesuvius")), "Vesuvius should erupt in AD 79");
        Check(pop.Pop[node] < 9000, $"Pompeii's people should suffer, left {pop.Pop[node]:N0} of 10,000");

        Finish($"Disaster tests passed: {cat.Events.Count} historical disasters; Rhodes in 226 BC; {chance} chance disasters in Rome's " +
            $"lands in 100 years; people {start / 1e6:0.0}M -> {end / 1e6:0.0}M; Vesuvius left {pop.Pop[node]:N0} of 10,000.");
        map.Free();
    }
}
