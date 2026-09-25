using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Score and goals: every playable realm has history's goals plus the
/// common ones; progress toward a region goal follows the share of its
/// people the realm holds; taking all Italy reaches "Unite Italy", which
/// adds its points to the score and a line to the chronicle; and reached
/// goals survive a save.
/// </summary>
public partial class GoalsTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.CivRealmIds["rome"];
        foreach (var (key, id) in map.CivRealmIds)
            Check(map.GoalsOf(id).Count >= 6, $"{key} should have its own goals and the common ones");
        var italy = map.GoalsOf(rome).First(g => g.Id == "italia");
        var (before, detail) = map.GoalProgress(rome, italy);
        Check(before > 0 && before < 1, $"Rome holds part of Italy at the start: {detail}");
        double scoreBefore = map.Score(rome);

        // Hand Rome every province and cell in Italy.
        var pop = map.Population!;
        int italia = pop.Regions.First(r => r.Name == "Italia").Id;
        foreach (var p in map.Provinces!.Provinces.Values.ToList())
        {
            int nx = (int)(p.LabelCell.X * pop.Width / MapView.GridWidth), ny = (int)(p.LabelCell.Y * pop.Height / MapView.GridHeight);
            if (pop.RegionOf(ny * pop.Width + nx) == italia && p.RealmId != rome)
                map.Provinces.SetRealm(p.Id, rome, map.Grid);
        }
        for (int i = 0; i < map.Grid.Cells.Length; i++)
        {
            int x = i % MapView.GridWidth, y = i / MapView.GridWidth;
            int node = pop.NodeAtCell(x, y, MapView.GridWidth, MapView.GridHeight);
            if (map.Grid.Cells[i] > 0 && map.Grid.Cells[i] != rome && node >= 0 && pop.RegionOf(node) == italia)
                map.Grid.Cells[i] = rome;
        }
        map.TerritoryChanged = true;
        map.SyncPopulationOwnership();
        var events = map.AdvanceYear();
        Check(map.PlayerState.GoalsDone.ContainsKey("italia"), $"Rome should have united Italy: {map.GoalProgress(rome, italy).Detail}");
        Check(events.Any(e => e.Text.StartsWith("Goal reached")), "reaching a goal should make the chronicle");
        Check(map.Score(rome) >= scoreBefore + italy.Points, $"the score should rise by the goal's points ({scoreBefore:0} -> {map.Score(rome):0})");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Realm(rome).GoalsDone.ContainsKey("italia"), "reached goals didn't survive the save");

        Finish($"Goals tests passed: every realm has its goals; Rome united Italy ({before:P0} -> done), score " +
            $"{scoreBefore:0} -> {map.Score(rome):0}; saved.");
        map.Free();
        map2.Free();
    }
}
