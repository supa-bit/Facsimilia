using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Generals and battles (section 11, first part): the great captains of 300 BC
/// lead their armies, every army gets a general, a better general wins more
/// often, the ground favours some arms, elephants fear javelins, winners
/// become veterans and recruits dilute them, battles leave reports, and all
/// of it survives a save.
/// </summary>
public partial class BattleTest : TestRunner
{
    static Army Make(params (string Unit, int N)[] units)
    {
        var a = new Army(UnitCatalog.Instance.Count) { Name = "Test army" };
        foreach (var (u, n) in units)
            a.Units[UnitCatalog.Instance[u].Index] = n;
        return a;
    }

    static General Gen(int tactics)
    {
        var g = new General { Id = tactics, Name = $"Tactics {tactics}" };
        for (int s = 0; s < Skills.Count; s++)
            g.BaseSkills[s] = 5;
        g.BaseSkills[Skills.Tactics] = tactics;
        return g;
    }

    /// <summary>Wins of the first side over many fights of fresh copies.</summary>
    static double WinShare(Func<Army> a, Func<Army> b, General? ga, General? gb, Ground g, int n = 400)
    {
        var rng = new Random(7);
        int wins = 0;
        for (int i = 0; i < n; i++)
        {
            var r = Battle.Fight(new BattleSide { Realm = 1, RealmName = "A", Armies = new() { (a(), 1.0) }, General = ga },
                new BattleSide { Realm = 2, RealmName = "B", Armies = new() { (b(), 1.0) }, General = gb }, g, "Testfield", -300, rng);
            if (r.AttackerWon)
                wins++;
        }
        return wins / (double)n;
    }

    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();

        // Battles in the abstract.
        Func<Army> legion = () => Make(("hastati_principes", 10));
        double even = WinShare(legion, legion, Gen(5), Gen(5), Ground.Open);
        double better = WinShare(legion, legion, Gen(9), Gen(5), Ground.Open);
        Check(even > 0.35 && even < 0.65, $"equal armies and generals should be a toss-up, not {even:P0}");
        Check(better > even + 0.1, $"a far better general should win more often ({better:P0} against {even:P0})");
        var hills = new Ground(0.9, 0, 0, 0, 0);
        double horseFlat = Battle.GroundFactor(UnitRoles.Cavalry, Ground.Open, null), horseHills = Battle.GroundFactor(UnitRoles.Cavalry, hills, null);
        Check(horseHills < horseFlat * 0.8, "cavalry should fight worse in the mountains");
        Check(Battle.GroundFactor(UnitRoles.LightInfantry, hills, null) > 1.1, "light troops should fight better in the mountains");
        var mountaineer = Gen(5);
        mountaineer.Perks.Add("hill_fighter");
        Check(Battle.GroundFactor(UnitRoles.HeavyInfantry, hills, mountaineer) > Battle.GroundFactor(UnitRoles.HeavyInfantry, hills, null),
            "a hill fighter should do better in the hills");
        var elephants = new double[UnitRoles.Count];
        elephants[UnitRoles.Elephants] = 10;
        var javelins = new double[UnitRoles.Count];
        javelins[UnitRoles.LightInfantry] = 10;
        var phalanx = new double[UnitRoles.Count];
        phalanx[UnitRoles.HeavyInfantry] = 10;
        Check(Battle.Strength(elephants, javelins, Ground.Open, null, null, 0, false) < Battle.Strength(elephants, phalanx, Ground.Open, null, null, 0, false),
            "elephants should be worth less against javelinmen, as at Zama");

        // Veterans.
        var vet = legion();
        var foe = Make(("levy_spearmen", 3));
        Battle.Fight(new BattleSide { Realm = 1, RealmName = "A", Armies = new() { (vet, 1.0) } },
            new BattleSide { Realm = 2, RealmName = "B", Armies = new() { (foe, 1.0) } }, Ground.Open, "X", -300, new Random(1));
        Check(vet.Experience > 0.04, "fighting a battle should harden an army");
        double xp = vet.Experience;
        Check(Military.Might(vet, Domain.Land) > Military.RawMight(vet, Domain.Land) * (1 - 0.5 * vet.Fatigue) + 1e-9, "veterans should add to Might");

        // The real world.
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.StartGame();
        var epirus = map.Game.Realm(map.CivRealmIds["epirus"]);
        var pyrrhos = epirus.GeneralOf(epirus.Armies[0]);
        Check(pyrrhos?.Name == "Pyrrhos" && pyrrhos.Perks.Contains("genius"), "Pyrrhos should lead Epirus's army, a genius of war");
        foreach (var s in map.Game.Realms.Values)
            foreach (var a in s.Armies.Where(a => !a.IsEmpty))
                Check(s.GeneralOf(a) != null, $"{map.RealmName(s.RealmId)}'s {a.Name} has no general");
        var rome = map.Game.Realm(map.CivRealmIds["rome"]);
        Check(rome.Generals.Any(g => g.Origin == General.Family), "Rome's first general should come from the ruling family");
        var cultures = map.CulturesOf(rome.RealmId);
        var legio = rome.Armies[0];
        legio.Experience = 0.5;
        rome.Treasury = 1000;
        int before = legio.Count;
        Military.Recruit(rome, map.CensusOf(rome.RealmId), cultures, UnitCatalog.Instance["hastati_principes"], legio);
        Check(legio.Experience < 0.5 && legio.Experience > 0.5 * (before - 1) / before, "raw recruits should dilute the veterans");

        int years = 0;
        while (map.Game.Battles.Count == 0 && years < 40)
        {
            map.AdvanceYear();
            years++;
        }
        Check(map.Game.Battles.Count > 0, "forty years without a single battle");
        var b = map.Game.Battles[0];
        Check(b.Phases.Count >= 2 && b.AttackerMen > 0 && b.Place != "", "a battle report should say where, who and how");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var ep2 = map2.Game.Realm(epirus.RealmId);
        Check(ep2.Generals.Count == epirus.Generals.Count && ep2.Generals.Zip(epirus.Generals).All(x => x.First.Name == x.Second.Name
            && x.First.Perks.SequenceEqual(x.Second.Perks) && x.First.ArmyId == x.Second.ArmyId), "generals didn't survive the save");
        Check(map2.Game.Battles.Count == map.Game.Battles.Count && map2.Game.Battles[0].Title == b.Title, "battle reports didn't survive the save");
        Check(Math.Abs(map2.Game.Realm(rome.RealmId).Armies[0].Experience - legio.Experience) < 1e-6, "veterans didn't survive the save");

        Finish($"Battle tests passed: a tactics-9 general wins {better:P0} against {even:P0} for equals; Pyrrhos leads Epirus; " +
            $"first battle after {years} year(s): {b.Title}, {b.Winner} won; {map.Game.Battles.Count} battle(s); saves keep it.");
        map.Free();
        map2.Free();
    }
}
