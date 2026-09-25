using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Harvests, weather and renewable stocks: the weather is fixed by the
/// year (the same year always has the same harvest), varies more where
/// rain is unreliable, and averages out to an ordinary year; famines come
/// now and then; overworked land wears down and rested land recovers,
/// fish fastest and forests slowest; a bad harvest shrinks what the
/// fields give; and 50 years on the real map leave history on course.
/// </summary>
public partial class NatureTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        // The rules on their own.
        Check(Nature.Harvest(-250, 5, 0.3, 0) == Nature.Harvest(-250, 5, 0.3, 0), "the weather should be fixed by year and region");
        var dry = Enumerable.Range(-300, 400).Select(y => Nature.Harvest(y, 3, 0.5, 0)).ToList();
        var watered = Enumerable.Range(-300, 400).Select(y => Nature.Harvest(y, 3, 0.5, 1)).ToList();
        double Sd(System.Collections.Generic.List<double> x) => Math.Sqrt(x.Average(v => (v - x.Average()) * (v - x.Average())));
        Check(Math.Abs(dry.Average() - 1) < 0.03, $"harvests should average an ordinary year, got {dry.Average():0.00}");
        Check(Sd(watered) < Sd(dry), "irrigated land should have steadier harvests");
        int famines = dry.Count(h => h < Nature.FamineHarvest);
        Check(famines > 5 && famines < 80, $"dry land should see a famine every decade or few, got {famines} in 400 years");

        var worked = new RegionNature();
        for (int y = 0; y < 50; y++)
            Nature.Tick(worked, 1.0, 0.2);
        Check(worked.Soil < 0.8 && worked.Fish < worked.Soil + 0.3, $"land at full pressure should wear down (soil {worked.Soil:0.00})");
        var soil = worked.Soil; var forest = worked.Forest; var fish = worked.Fish;
        for (int y = 0; y < 10; y++)
            Nature.Tick(worked, 0.1, 1);
        Check(worked.Fish - fish > worked.Forest - forest, "fish should recover faster than forests");
        Check(worked.Soil > soil, "rested soil should recover");
        var bad = new RegionNature { Harvest = 0.6 };
        Check(Nature.FieldYield(bad) < Nature.FieldYield(new RegionNature()), "a bad harvest should shrink the fields' yield");

        // On the real map.
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("egypt");
        map.StartGame();
        var pop = map.Population!;
        double start = pop.LandNodes.Sum(i => (double)pop.Pop[i]);
        int famineLines = 0;
        for (int y = 0; y < 50; y++)
            famineLines += map.AdvanceYear().Count(e => e.Text.Contains("famine"));
        double end = pop.LandNodes.Sum(i => (double)pop.Pop[i]);
        var harvests = pop.Regions.Where(r => r.Id > 0).Select(r => map.NatureOf(r.Id).Harvest).ToList();
        Check(harvests.Distinct().Count() > 10, "regions should have different harvests");
        Check(pop.Regions.Where(r => r.Id > 0).All(r => map.NatureOf(r.Id).Soil > 0.5), "300 BC land shouldn't be worn out in 50 years");
        Check(end > start * 0.85 && end < start * 1.3, $"the world's people went from {start:N0} to {end:N0} in 50 years");
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var r1 = pop.Regions.First(r => r.Name == "Aegyptus").Id;
        Check(Math.Abs(map2.NatureOf(r1).Soil - map.NatureOf(r1).Soil) < 1e-9, "nature didn't survive the save");

        Finish($"Nature tests passed: weather fixed by year, steadier on watered land, {famines} famines in 400 dry years; " +
            $"wear and recovery at their speeds; 50 years on the map ({famineLines} famines in Egypt's lands), " +
            $"people {start / 1e6:0.0}M -> {end / 1e6:0.0}M.");
        map.Free();
        map2.Free();
    }
}
