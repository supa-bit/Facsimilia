using System.Threading.Tasks;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>A full generated world survives save and load into a separate MapView.</summary>
public partial class SaveFullWorldTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        map.StartPopulation();
        map.AdvanceYear();  // a year of dynasty and population state, not just a fresh start
        map.DemoYear = -290;
        int rome = map.CivRealmIds["rome"];
        int romeBefore = WorldFixture.CountCells(map, rome);
        Check(romeBefore > 0, "Rome has no territory");
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        Check(SaveSystem.HasSave("slot1"), "no save written");

        // Load into a second, independent instance: not the same in-memory objects.
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.DemoYear == -290, $"year {map2.DemoYear}, expected -290");
        Check(map2.PlayerRealmId == map.PlayerRealmId, "player realm differs");
        Check(map2.Registry.Characters.Count == map.Registry.Characters.Count, "character count differs");
        Check(map2.Registry.Realms.Count == map.Registry.Realms.Count, "realm count differs");
        int romeAfter = WorldFixture.CountCells(map2, rome);
        Check(romeAfter == romeBefore, $"Rome had {romeBefore} cells, now {romeAfter}");
        if (map.Population != null)
        {
            Check(map2.Population != null, "population wasn't restored");
            Check(map2.Population != null && map2.Population.Year == map.Population.Year
                && map2.Population.TotalPopulation() == map.Population.TotalPopulation(), "population state differs");
        }

        Finish($"Full-world save/load round-trip passed: {romeBefore:N0} Rome cells preserved, " +
            $"{map2.Registry.Characters.Count} characters and {map2.Registry.Realms.Count} realms restored.");
        map.Free();
        map2.Free();
    }
}
