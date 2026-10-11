using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Roads: kinds by material; history's roads standing in 300 BC; a road
/// drawn between two of your towns follows your own land and costs silver,
/// workers and years; once built everyone marches faster on it, the
/// provinces it crosses trade more; history's builders lay theirs on their
/// dates; you can break and repair your own; unpaid roads decay; it all
/// survives a save.
/// </summary>
public partial class RoadsTest : TestRunner
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
        var cat = RoadCatalog.Instance;
        int rome = map.PlayerRealmId;
        var s = map.PlayerState;
        var pop = map.Population!;
        int Node(double lon, double lat) => pop.NodeAtLonLat(lon, lat, MapView.LonMin, MapView.LonMax, MapView.LatMin, MapView.LatMax);

        Check(cat.Kinds.Count >= 5 && cat.Kinds.Select(k => k.Pace).SequenceEqual(cat.Kinds.Select(k => k.Pace).OrderByDescending(x => x)), "kinds by material, each faster");
        Check(map.Game.Roads.Any(r => r.History.StartsWith("royal_road") && r.Built) && map.Game.Roads.Any(r => r.History.StartsWith("via_latina")),
            "the Royal Road and the Via Latina stand in 300 BC");
        Check(cat.Open(s).All(k => k.Requires == null || s.Techs.Contains(k.Requires)) && cat.Open(s).All(k => k.Id != "asphalt"), "no asphalt in 300 BC");

        // A gravel road from Rome toward Campania, through Roman land.
        int roma = Node(12.50, 41.89);
        s.Treasury = 100000;
        s.Manpower = 100000;
        Check(pop.NodeOwner[roma] == rome, "Roma is Roman");
        int Far(int n) => Math.Max(Math.Abs(n % pop.Width - roma % pop.Width), Math.Abs(n / pop.Width - roma / pop.Width));
        int dest = pop.LandNodes.Where(n => pop.NodeOwner[n] == rome && Far(n) is >= 12 and <= 20).Take(40)
            .FirstOrDefault(n => map.PlanRoad(rome, cat["gravel"]!, roma, n, "Roma", "the far town").Problem == null);
        s.Treasury = 100000;
        s.Manpower = 100000;
        var plan = map.PlanRoad(rome, cat["gravel"]!, roma, dest, "Roma", "the far town");
        Check(plan.Problem == null && plan.Km > 50 && plan.Silver > 0 && plan.Years >= 1 && plan.Nodes.All(n => pop.NodeOwner[n] == rome),
            $"a road through your own land with its price: {map.DescribePlan(plan)}");
        double trade = map.RealmBuildingEffects(rome, map.CensusOf(rome).People).Trade;
        double before = s.Treasury, men = s.Manpower;
        var road = map.BuildRoad(rome, plan)!;
        Check(s.Treasury < before && s.Manpower < men && !road.Built, "a road costs silver and workers and takes years");
        double paceBefore = map.RoadPaceForTest(plan.Nodes[5], plan.Nodes[6]);
        for (int y = 0; y < plan.Years; y++)
            map.AdvanceYear();
        Check(road.Built && road.Workers == 0, $"the road is finished after {plan.Years} years and its workers come home");
        Check(map.RoadPaceForTest(plan.Nodes[5], plan.Nodes[6]) < paceBefore, "armies march faster on the road");
        map.InvalidateGoods();
        Check(map.RealmBuildingEffects(rome, map.CensusOf(rome).People).Trade > trade, "provinces on the road trade more");

        // Break and repair.
        Check(map.BreakRoad(road) == null && map.RoadPaceForTest(plan.Nodes[5], plan.Nodes[6]) == paceBefore, "a broken road speeds no one");
        Check(map.RepairRoad(road) == null && !road.Broken, "a broken road can be repaired");
        map.AdvanceYear();

        // A canal between two waters: boats pass through it.
        var canal = cat.Canals.First(k => k.Id == "canal");
        Check(map.PlanRoad(rome, canal, roma, dest, "Roma", "inland").Problem != null || map.TouchesWaterForTest(dest), "a canal must join two waters");
        var shore = pop.LandNodes.Where(n => pop.NodeOwner[n] == rome && map.TouchesWaterForTest(n)).ToList();
        RoadPlan? cut = null;
        foreach (int a in shore.Take(60))
        {
            foreach (int b in shore.Where(b => Math.Max(Math.Abs(b % pop.Width - a % pop.Width), Math.Abs(b / pop.Width - a / pop.Width)) is >= 6 and <= 12).Take(20))
            {
                var p = map.PlanRoad(rome, canal, a, b, "here", "there");
                if (p.Problem == null) { cut = p; break; }
            }
            if (cut != null) break;
        }
        Check(cut != null, "a canal can be dug between two waters on level Roman ground");
        var dug = map.BuildRoad(rome, cut!)!;
        dug.Left = 0;
        map.RoadsYearForTest();
        Check(map.CanalStepForTest(cut!.Nodes[1], cut.Nodes[2]), "ships pass through a finished canal");

        // A frontier wall drawn along Roman land: built, then manned from manpower; strangers lose march crossing it.
        Check(map.Game.Roads.Any(r => r.History.StartsWith("long_walls_athens") && r.Manned), "the Long Walls of Athens stand, manned, in 300 BC");
        var stone = cat.Walls.First(k => k.Id == "stone_wall");
        var wallPlan = map.ExtendWall(rome, stone, null, roma, dest, "Roma", "the far town")!;
        Check(wallPlan != null && wallPlan.Problem == null && wallPlan.Km > 50, "a wall drawn point to point along your land");
        var wall = map.BuildRoad(rome, wallPlan)!;
        wall.Left = 1;
        s.Manpower = 100000;
        map.RoadsYearForTest();
        Check(wall.Built && wall.Manned && wall.Workers >= stone.Garrison * wall.Km * 0.99, "a finished wall takes its garrison from manpower");
        int w1 = wall.Nodes[10], w0 = pop.LandNodes.First(n => !wall.Nodes.Contains(n) && Math.Abs(n % pop.Width - w1 % pop.Width) <= 1 && Math.Abs(n / pop.Width - w1 / pop.Width) <= 1);
        int stranger = map.Game.Realms.Keys.First(r => r != rome && !map.Game.Treaties.Allied(r, rome));
        Check(map.WallCrossingForTest(w0, w1, stranger) >= stone.CrossKm && map.WallCrossingForTest(w0, w1, rome) == 0, "strangers lose march crossing your wall; you don't");
        map.Game.Roads.Remove(wall);

        // History's builders lay theirs on their dates.
        int year = map.DemoYear;
        map.DemoYear = -312;
        map.RoadsYearForTest();
        Check(map.Game.Roads.Any(r => r.History.StartsWith("via_appia:") && r.Realm == rome), "Rome lays the Via Appia in 312 BC");
        map.DemoYear = year;

        // Neglect: unpaid upkeep for three years drops a kind.
        s.UnpaidYears = 1;
        for (int y = 0; y < MapView.RoadNeglectYears; y++)
            map.RoadsYearForTest();
        Check(road.Kind == "earth", $"an unkept gravel road falls to an earth road ({road.Kind})");
        s.UnpaidYears = 0;

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Roads.Count == map.Game.Roads.Count && map2.Game.Roads.Any(r => r.Id == road.Id && r.Nodes.Count == road.Nodes.Count && r.Kind == "earth"),
            "roads didn't survive the save");

        Finish($"Roads tests passed: {cat.Kinds.Count} kinds; {map.Game.Roads.Count} roads; {map.DescribePlan(plan)}");
        map.Free();
        map2.Free();
    }
}
