using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Marching (section 11, part 2): armies follow routes month by month,
/// slower in winter; the land of realms not at war is closed; beyond its
/// land an army lives on supply lines, forage and baggage, and starves when
/// all run out; enemy armies that meet fight and the beaten one falls back;
/// the realm sees only what it knows; and routes and baggage survive a save.
/// </summary>
public partial class MarchTest : TestRunner
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
        var pop = map.Population!;
        int rome = map.CivRealmIds["rome"], etruscans = map.CivRealmIds["etruscans"], egypt = map.CivRealmIds["egypt"];
        var r = map.Game.Realm(rome);
        var legio = r.Armies[0];
        int Node(double lon, double lat) => pop.NodeAtLonLat(lon, lat, MapView.LonMin, MapView.LonMax, MapView.LatMin, MapView.LatMax);

        // A march across Rome's own land, month by month.
        int start = legio.Node;
        int far = pop.LandNodes.Where(i => pop.NodeOwner[i] == rome).OrderByDescending(i => Math.Abs(i % pop.Width - start % pop.Width) + Math.Abs(i / pop.Width - start / pop.Width)).First();
        Check(map.SendArmy(rome, legio, far) == null && legio.Marching, "Rome's army couldn't march across its own land");
        double months = map.RouteMonths(r, legio);
        Check(months > 0.3 && months < 12, $"a march across Roman Italy should take months, not {months:0.0}");
        int steps = legio.Route.Count, marched = 0;
        map.MarchMonth();
        marched++;
        Check(legio.Route.Count < steps, "a month should bring the army along its route");
        while (legio.Marching && marched < 24)
        {
            map.MarchMonth();
            marched++;
        }
        Check(legio.Node == far, $"the army didn't arrive in {marched} months");

        // Closed land and winter.
        var egyptNode = map.CapitalNode(egypt);
        Check(map.FindRoute(rome, legio, egyptNode) == null || Military.RawMight(legio, Domain.Naval) > 0, "the way to Egypt should be closed without war or a fleet");
        Check(MapView.Winter(0) && !MapView.Winter(6), "January is winter, July is not");

        // Hunger: an army far out in the empty desert, with a month of food.
        int desert = pop.LandNodes.Where(i => pop.NodeOwner[i] <= 0 && pop.Pop[i] < 1)
            .OrderBy(i => Math.Abs(i % pop.Width - Node(15, 27) % pop.Width) + Math.Abs(i / pop.Width - Node(15, 27) / pop.Width)).First();
        var lost = r.NewArmy("Lost legion", desert);
        lost.SetUnits(UnitCatalog.Instance["hastati_principes"].Index, 10);
        lost.Supply = 1;
        int men = Military.Soldiers(lost);
        for (int m = 0; m < 4; m++)
            map.MarchMonth();
        Check(lost.Supply == 0 && Military.Soldiers(lost) < men, $"an army in the desert should eat its baggage and starve ({lost.Supply:0.0} months left, {Military.Soldiers(lost)} of {men} men)");
        var home = r.Armies[0];
        Check(home.Supply >= MapView.BaggageMonths - 0.01, "an army at home should keep its baggage full");

        // Meeting: at war, two armies close to each other fight, and the loser falls back.
        Check(map.DeclareWar(etruscans) != null, "couldn't declare war on the Etruscans");
        var et = map.Game.Realm(etruscans);
        var etArmy = et.Armies.First(a => !a.IsEmpty);
        legio.Node = etArmy.Node;
        legio.Route.Clear();
        int battles = map.Game.Battles.Count;
        int homeProv = legio.Regiments.Select(x => x.Origin).FirstOrDefault(o => o != 0);
        double HomePeople() => pop.LandNodes.Where(i => map.ProvinceOfNodeForTest(i) == homeProv).Sum(i => (double)pop.Pop[i]);
        double homeBefore = HomePeople();
        var events = map.MarchMonth();
        Check(map.Game.Battles.Count == battles + 1, "armies at war standing together should fight");
        var report = map.Game.Battles[^1];
        Check(events.Any(e => e.Text.Contains("defeats")), "no chronicle line for the battle");
        Check(homeProv != 0 && HomePeople() < homeBefore, $"the dead should be lost to their home province's people ({homeBefore:0} -> {HomePeople():0})");
        Check(legio.Regiments.Sum(x => x.Wounded) > 0 || legio.IsEmpty, "a battle should leave wounded who heal");
        var loser = report.AttackerWon ? (report.AttackerRealm == rome ? etArmy : legio) : (report.AttackerRealm == rome ? legio : etArmy);
        Check(loser.IsEmpty || loser.Marching, "the beaten army should fall back toward home");

        // What Rome knows.
        var known = map.Known();
        Check(known[map.CapitalNode(rome)] && known[legio.Node], "Rome should know its own land and where its armies stand");
        Check(!known[egyptNode], "Rome shouldn't know what happens in Alexandria");
        Check(!map.KnowsNode(egyptNode), "enemy armies in unknown land should be hidden");

        // Save and load.
        map.SendArmy(rome, home, far);
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var home2 = map2.Game.Realm(rome).Armies[0];
        Check(home2.Route.SequenceEqual(home.Route) && Math.Abs(home2.Supply - home.Supply) < 1e-6, "routes and baggage didn't survive the save");
        Check(map2.Game.MonthsMarched == map.Game.MonthsMarched, "the month didn't survive the save");

        // A whole year with the turn button marches all twelve months.
        map.AdvanceYear();
        Check(map.Game.MonthsMarched == 0, "after a year the months should start again");

        Finish($"March tests passed: across Roman Italy in {marched} month(s) (estimated {months:0.0}); closed borders; " +
            $"starving in the desert ({men - Military.Soldiers(lost):N0} men lost); {report.Title}, {report.Winner} won; fog; saves keep it.");
        map.Free();
        map2.Free();
    }
}
