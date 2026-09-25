using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Conquest by painting on the real 300 BC map: the army's reach; war
/// needed before attacking another realm's land; a strong attacker takes a
/// whole province, which changes hands with its cells, raises the war score
/// and costs losses and weariness; the sea needs a fleet; and peace follows
/// the war score.
/// </summary>
public partial class ConquestTest : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.CivRealmIds["rome"], greeks = map.CivRealmIds["greek_world"], egypt = map.CivRealmIds["egypt"];
        Check(map.PlayerRealmId == rome, "the player should be Rome");

        // Reach: Rome reaches its Italian neighbours, not Egypt.
        var (reach, _) = map.ComputeReach(rome);
        var pop = map.Population!;
        Check(!reach[pop.NodeAtLonLat(31.2, 30.0, MapView.LonMin, MapView.LonMax, MapView.LatMin, MapView.LatMax)],
            "Rome shouldn't reach Egypt in a year");
        Check(reach[pop.NodeAtLonLat(13.0, 41.5, MapView.LonMin, MapView.LonMax, MapView.LatMin, MapView.LatMax)],
            "Rome should reach its own neighbourhood");

        // A Greek province in Italy within reach.
        var target = map.Provinces!.Provinces.Values
            .Where(p => p.RealmId == greeks)
            .Select(p => (p, cells: Enumerable.Range(0, map.Provinces.Cells.Length).Where(i => map.Provinces.Cells[i] == p.Id).ToList()))
            .Select(x => (x.p, x.cells, planned: x.cells.Count(i => reach[pop.NodeAtCell(i % MapView.GridWidth, i / MapView.GridWidth, MapView.GridWidth, MapView.GridHeight)])))
            .Where(x => x.planned > 0)
            .OrderByDescending(x => x.planned)
            .FirstOrDefault();
        if (!Check(target.p != null, "no Greek province within Rome's reach"))
        {
            Finish("");
            return;
        }
        Check(map.PlanCells(target.cells) > 0, "couldn't paint the Greek province");
        var targets = map.PlayerConquestTargets();
        Check(targets.Any(t => t.ProvinceId == target.p!.Id && t.Problem != null), "attacking without war should be refused");

        Check(map.DeclareWar(greeks) != null, "couldn't declare war on the Greeks");
        Check(map.Game.Wars.AtWar(rome, greeks), "no war after declaring");
        var r = map.Game.Realm(rome);
        r.Units[UnitTypes.HeavyInfantry] = 200;   // overwhelming, so the outcome is certain
        targets = map.PlayerConquestTargets();
        var t = targets.First(x => x.ProvinceId == target.p!.Id);
        Check(t.Problem == null && t.Chance > 0.95, $"with 200 legions Rome should be near-certain to win, chance {t.Chance:P0}");
        int units = r.Units.Sum();
        var events = map.ResolvePlayerPlan();
        Check(map.Provinces.Provinces[target.p!.Id].RealmId == rome, "the province didn't change hands");
        Check(target.cells.All(i => map.Grid.Cells[i] == rome), "the province's cells weren't all handed over");
        Check(events.Any(e => e.Text.Contains("takes")), "no chronicle line for the conquest");
        var war = map.Game.Wars.Between(rome, greeks)!;
        Check(war.Score > 0, "the war score should favour Rome after a victory");
        Check(r.Units.Sum() < units && r.Fatigue > 0, "fighting should cost losses and weariness");

        // Peace: a small victory isn't enough to demand tribute; an even peace is refused until they lose more or tire.
        Check(!Diplomacy.Accepts(war, rome, map.DemoYear, demandTribute: true) || war.Score >= Diplomacy.TributeScore,
            "tribute demanded too early was accepted");
        war.Score = 60;
        var (accepted, text) = map.OfferPeace(greeks, demandTribute: true);
        Check(accepted && !map.Game.Wars.AtWar(rome, greeks), $"a beaten enemy should pay tribute and make peace: {text}");
        Check(Diplomacy.CanDeclare(map.Game, rome, greeks, map.DemoYear) != null, "a truce should follow peace");

        // The sea: without warships Rome can't reach across to Egypt's coast even when close.
        r.Units[UnitTypes.Warships] = 0;
        var (reachNoFleet, _) = map.ComputeReach(rome);
        r.Units[UnitTypes.Warships] = 50;
        var (reachFleet, bySea) = map.ComputeReach(rome);
        Check(reachFleet.Count(b => b) > reachNoFleet.Count(b => b), "a fleet should extend Rome's reach");
        Check(bySea.Any(b => b), "some land should be reachable only by sea");

        Finish($"Conquest tests passed: reach, war before attack, Rome took {target.p!.Name} (chance {t.Chance:P0}), " +
            $"war score {war.Score:+0}, losses and weariness, tribute and truce, fleets extend reach " +
            $"({reachNoFleet.Count(b => b):N0} -> {reachFleet.Count(b => b):N0} nodes).");
        map.Free();
    }
}
