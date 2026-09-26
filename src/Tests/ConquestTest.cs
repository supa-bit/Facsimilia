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

        // Reach: Rome's army reaches its Italian neighbours, not Egypt.
        var r = map.Game.Realm(rome);
        var legio = r.Armies[0];
        var cat = UnitCatalog.Instance;
        var (reach, _) = map.ComputeReach(rome, legio);
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
        // A small army faces a long siege; a great one a short one.
        var hastati = cat["hastati_principes"];
        var saved = (int[])legio.Units.Clone();
        foreach (var u in cat.Units.Where(u => u.Domain == Domain.Land))
            legio.Units[u.Index] = 0;
        legio.Units[hastati.Index] = 2;
        var weak = map.PlayerConquestTargets().First(x => x.ProvinceId == target.p!.Id);
        legio.Units[hastati.Index] = 200;   // overwhelming, so the outcome is certain
        targets = map.PlayerConquestTargets();
        var t = targets.First(x => x.ProvinceId == target.p!.Id);
        Check(t.Problem == null && t.Chance > 0.95, $"with 200 legions Rome should be near-certain to win in battle, chance {t.Chance:P0} ({t.Problem}; {t.Name})");
        Check(weak.Years > t.Years && t.Years <= 2, $"a weak army should besiege for longer ({weak.Years} vs {t.Years} years)");
        foreach (var a in map.Game.Realm(greeks).Armies)
            Array.Clear(a.Units);   // the Greeks keep no field army here, so only the siege is tested
        int units = legio.Count;
        var events = map.ResolvePlayerPlan();
        Check(events.Any(e => e.Text.Contains("lays siege")) && map.Game.Sieges.Any(x => x.ProvinceId == target.p!.Id),
            "ending the turn should begin a siege");
        Check(map.Provinces.Provinces[target.p!.Id].RealmId == greeks, "a siege takes time: the province shouldn't fall at once");
        Check(map.ProvinceOfNodeForTest(legio.Node) == target.p!.Id, "the army should march to the place it besieges");
        int years = 0;
        while (map.Provinces.Provinces[target.p!.Id].RealmId != rome && years++ < 5)
            events.AddRange(map.AdvanceYear());
        Check(map.Provinces.Provinces[target.p!.Id].RealmId == rome, $"the siege didn't end in {years} years");
        Check(target.cells.All(i => map.Grid.Cells[i] == rome), "the province's cells weren't all handed over");
        Check(events.Any(e => e.Text.Contains("takes")), "no chronicle line for the conquest");
        var war = map.Game.Wars.Between(rome, greeks)!;
        Check(war.Score > 0, $"the war score should favour Rome after a victory ({war.Score}): " +
            string.Join(" | ", events.Where(e => e.Text.Contains("Greek") || e.Text.Contains("Rom")).Select(e => e.Text).Distinct()));
        Check(legio.Count < units, "a siege should cost losses");

        // Peace: a small victory isn't enough to demand tribute; an even peace is refused until they lose more or tire.
        Check(!Diplomacy.Accepts(war, rome, map.DemoYear, demandTribute: true) || war.Score >= Diplomacy.TributeScore,
            "tribute demanded too early was accepted");
        war.Score = 60;
        var (accepted, text) = map.OfferPeace(greeks, demandTribute: true);
        Check(accepted && !map.Game.Wars.AtWar(rome, greeks), $"a beaten enemy should pay tribute and make peace: {text}");
        Check(Diplomacy.CanDeclare(map.Game, rome, greeks, map.DemoYear) != null, "a truce should follow peace");

        // The sea: without warships Rome can't reach across to Egypt's coast even when close.
        var quinqueremes = cat["quinqueremes"];
        foreach (var u in cat.Units.Where(u => u.Domain == Domain.Naval))
            legio.Units[u.Index] = 0;
        var (reachNoFleet, _) = map.ComputeReach(rome, legio);
        legio.Units[quinqueremes.Index] = 50;
        var (reachFleet, bySea) = map.ComputeReach(rome, legio);
        Check(reachFleet.Count(b => b) > reachNoFleet.Count(b => b), "a fleet should extend Rome's reach");
        Check(bySea.Any(b => b), "some land should be reachable only by sea");

        Finish($"Conquest tests passed: reach, war before attack, Rome besieged and took {target.p!.Name} in {years} year(s) " +
            $"(a weak army would need {weak.Years}), war score {war.Score:+0}, losses and weariness, tribute and truce, fleets extend reach " +
            $"({reachNoFleet.Count(b => b):N0} -> {reachFleet.Count(b => b):N0} nodes).");
        map.Free();
    }
}
