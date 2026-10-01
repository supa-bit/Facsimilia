using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The battle map (section 11, part 3): troops deploy in blocks on ground
/// from the real map; moves, strikes, flanks, morale and routs; the stronger
/// army usually wins; a battle of the player's waits for them and the year
/// goes on after it; fleets fight at sea; and enemy fleets off a coast
/// blockade it, cutting its sea trade.
/// </summary>
public partial class TacticsTest : TestRunner
{
    static Army Make(params (string Unit, int N)[] units)
    {
        var a = new Army(UnitCatalog.Instance.Count) { Name = "Test army" };
        foreach (var (u, n) in units)
            a.SetUnits(UnitCatalog.Instance[u].Index, n);
        return a;
    }

    static TacticalBattle New(Army a, Army b, Ground g, bool naval, int seed) =>
        new(new BattleSide { Realm = 1, RealmName = "Rome", Armies = new() { (a, 1.0) } },
            new BattleSide { Realm = 2, RealmName = "Samnites", Armies = new() { (b, 1.0) } }, g, naval, seed);

    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        Func<Army> legion = () => Make(("hastati_principes", 12), ("velites", 4), ("equites", 4));
        Func<Army> levy = () => Make(("levy_spearmen", 6), ("slingers", 2));

        // Deployment.
        var t = New(legion(), levy(), new Ground(0.4, 0.3, 0, 0, 0.5), false, 1);
        Check(t.Blocks.Count(b => b.Side == 0) >= 3 && t.Blocks.Count(b => b.Side == 1) >= 2, "each side should deploy in several blocks");
        Check(t.Blocks.GroupBy(b => (b.X, b.Y)).All(g => g.Count() == 1), "two blocks shouldn't stand on one square");
        Check(t.Blocks.Where(b => b.Side == 0).All(b => b.X < TacticalBattle.Width / 2) && t.Blocks.Where(b => b.Side == 1).All(b => b.X >= TacticalBattle.Width / 2),
            "each side should start on its own half");
        Check(Enumerable.Range(0, TacticalBattle.Width).Any(x => Enumerable.Range(0, TacticalBattle.Height).Any(y => t.Map[x, y] == Tile.Hills)),
            "hilly ground should put hills on the battle map");

        // Moving.
        var horse = t.Blocks.First(b => b.Side == 0 && b.Role == UnitRoles.Cavalry);
        var reach = t.Reach(horse);
        Check(reach.Count > 0 && reach.All(p => Math.Max(Math.Abs(p.X - horse.X), Math.Abs(p.Y - horse.Y)) <= TacticalBattle.MoveOf[UnitRoles.Cavalry]), "cavalry should move up to 4 squares");
        var to = reach.First();
        Check(t.Move(horse, to.X, to.Y) && !t.Move(horse, horse.X + 1, horse.Y), "a block should move once a turn");

        // Matchups and ground.
        Check(TacticalBattle.Edge(UnitRoles.Cavalry, UnitRoles.Missile) > 1 && TacticalBattle.Edge(UnitRoles.Elephants, UnitRoles.Cavalry) > 1
            && TacticalBattle.Edge(UnitRoles.LightInfantry, UnitRoles.Elephants) > 1, "horse rides down archers, elephants scatter horse, javelins turn elephants");

        // The stronger army usually wins; every battle ends.
        int wins = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            var b = New(legion(), levy(), Ground.Open, false, seed);
            b.PlayOut();
            Check(b.Winner != null, "a battle played out should have a winner");
            if (b.Winner == 0)
                wins++;
        }
        Check(wins >= 15, $"a legion with horse should beat a small levy most days ({wins} of 20)");
        var lost = New(legion(), levy(), Ground.Open, false, 3);
        lost.PlayOut();
        var (al, dl) = lost.Losses();
        Check((lost.Winner == 0 ? dl > al : al > dl) && al > 0 && dl > 0, $"the loser should lose more ({al:P0} against {dl:P0})");

        // At sea.
        var sea = New(Make(("quinqueremes", 10)), Make(("triremes", 8)), Ground.Open, true, 5);
        Check(sea.Blocks.All(b => b.Role == UnitRoles.Warships) && sea.Map[0, 0] == Tile.Water, "a sea battle is fought by ships on water");
        sea.PlayOut();
        Check(sea.Winner != null, "a sea battle should end");

        // In the world: the player's battle waits for them.
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.CivRealmIds["rome"], etruscans = map.CivRealmIds["etruscans"];
        var r = map.Game.Realm(rome);
        var legio = r.Armies[0];
        Check(map.DeclareWar(etruscans) != null, "couldn't declare war on the Etruscans");
        var et = map.Game.Realm(etruscans);
        var etArmy = et.Armies.First(a => !a.IsEmpty);
        legio.Node = etArmy.Node;
        legio.Route.Clear();
        map.FightBattlesYourself = true;
        int battles = map.Game.Battles.Count, year = map.DemoYear;
        map.AdvanceYear();
        Check(map.Pending != null && map.Game.Battles.Count == battles && map.DemoYear == year,
            "the player's battle should wait on the battle map, and the year with it");
        var pending = map.Pending!;
        Check(pending.Side == (pending.Att.RealmId == rome ? 0 : 1), "the player should command Rome's side");
        map.FinishPendingBattle();
        Check(map.Pending == null && map.Game.Battles.Count == battles + 1 && map.Game.Battles[^1].Phases[0].Contains("battle map"),
            "the battle's result should go into the world");
        map.FightBattlesYourself = false;
        map.AdvanceYear();
        Check(map.DemoYear != year, "after the battle the year should go on");

        // Blockade: a Roman fleet off the Etruscan coast.
        var pop = map.Population!;
        var fleet = r.NewArmy("Classis", -1);
        fleet.SetUnits(UnitCatalog.Instance["quinqueremes"].Index, 30);
        int w = pop.Width;
        fleet.Node = pop.LandNodes.Where(i => pop.NodeOwner[i] == etruscans && map.Coastal(i))
            .SelectMany(i => Enumerable.Range(-3, 7).SelectMany(dy => Enumerable.Range(-3, 7).Select(dx => i + dy * w + dx)))
            .First(j => j >= 0 && j < pop.NodeRegion.Length && pop.NodeRegion[j] == 0);
        foreach (var a in et.Armies)
            for (int i = 0; i < a.Units.Length; i++)
                if (UnitCatalog.Instance[i].Domain == Domain.Naval)
                    a.SetUnits(i, 0);   // their own fleet is away
        map.MarchMonth();
        Check(et.Blockade > 0, "a Roman fleet off the Etruscan coast should blockade it");
        var c = map.CensusOf(etruscans);
        et.BlockadeMonths = 0;
        double open = Economy.Accounts(et, c).Customs;
        et.BlockadeMonths = 6;
        double shut = Economy.Accounts(et, c).Customs;
        Check(open <= 0 || shut < open, "a blockade should cut the customs");

        Finish($"Tactics tests passed: blocks deploy on real ground; a legion beats a levy on {wins} of 20 days; " +
            $"sea battles; the player's battle waits and the year goes on; blockades cut customs ({open:0} -> {shut:0} talents).");
        map.Free();
    }
}
