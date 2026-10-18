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

        // Terra incognita: Rome starts knowing Italy and its neighbours, not the far edges of the world.
        Check(map.Game.Explored.Length > 0 && map.ExploredNode(map.CapitalNode(rome)), "Rome should know its own land from the start");
        var pop2 = map.Population!;
        int explored = pop2.LandNodes.Count(i => map.Game.IsExplored(i));
        Check(explored > 0 && explored < pop2.LandNodes.Length / 2, $"Rome should start knowing only part of the world ({explored} of {pop2.LandNodes.Length} land nodes)");
        int farNode = pop2.LandNodes.OrderByDescending(i => i % pop2.Width).First();
        Check(!map.ExploredNode(farNode), "lands far to the east should be blank at the start");
        map.Game.MapFog = FogLevel.Light;
        Check(map.ExploredNode(farNode) && map.FogOn, "light fog shows all land but still hides armies");
        map.Game.MapFog = FogLevel.Off;
        Check(map.KnowsNode(egyptNode), "with the fog off everything is seen");
        map.Game.MapFog = FogLevel.Full;

        // The diplomacy fog: what Rome knows of other realms' figures.
        Check(map.InsightInto(rome) == MapView.Insight.Exact, "Rome knows its own figures");
        int stranger = map.Game.Realms.Keys.FirstOrDefault(id => id != rome && map.InsightInto(id) == MapView.Insight.Unknown);
        Check(stranger != 0, "with full diplomacy fog some far realm should be unknown");
        map.Game.DiplomacyFog = FogLevel.Light;
        Check(map.InsightInto(stranger) == MapView.Insight.Rough, "with light fog far realms are known roughly");
        map.Game.DiplomacyFog = FogLevel.Off;
        Check(map.InsightInto(stranger) == MapView.Insight.Exact, "with the fog off every realm is known");
        map.Game.DiplomacyFog = FogLevel.Light;
        Check(Math.Abs(MapView.Rough(123456) - 120000) < 1e-6, "rough figures keep two digits");
        // Other realms see through the same fog: Egypt knows Rome only by report, and far realms not at all.
        Check(map.InsightBetween(egypt, egypt) == MapView.Insight.Exact, "a realm knows its own strength");
        double romeMight = Military.Might(map.PlayerState, Domain.Land);
        double egyptSees = map.SeenMight(egypt, rome);
        Check(map.InsightBetween(egypt, rome) == MapView.Insight.Exact
            || (egyptSees >= 0.7 * romeMight && egyptSees <= 1.3 * romeMight), $"Egypt's estimate of Rome should be near the truth ({egyptSees:0} vs {romeMight:0})");
        map.Game.DiplomacyFog = FogLevel.Full;
        Check(map.SeenMight(rome, stranger) == 0, "under full fog a realm knows nothing of a stranger's strength");
        // Enemies watch each other: at war, even a far realm is known at least by report.
        var strangerWar = Diplomacy.Declare(map.Game, rome, stranger, map.DemoYear, false);
        Check(map.InsightInto(stranger) != MapView.Insight.Unknown, "at war with a far realm, Rome should know it at least roughly");
        map.Game.Wars.End(strangerWar);
        int beyond = map.Game.Realms.Keys.FirstOrDefault(id => id != rome && !map.CanDealWith(id));
        Check(beyond != 0 && map.CanDealWith(etruscans), "under full fog you deal only with realms you know");
        Check(map.ProposeAlliance(beyond).Contains("knows nothing") && map.DeclareWar(beyond) == null, "no treaties or wars with realms beyond your knowledge");
        map.Game.DiplomacyFog = FogLevel.Off;
        Check(map.CanDealWith(beyond), "with the fog off you can deal with anyone");
        map.Game.DiplomacyFog = FogLevel.Full;
        map.Game.DiplomacyFog = FogLevel.Light;

        // News travels: Egypt's news takes months, a war is known at once, the far side of the world is never heard of.
        int egyptMonths = map.NewsMonths(egypt);
        Check(egyptMonths >= 1 && egyptMonths <= 6, $"news from Alexandria should take a few months ({egyptMonths})");
        int farthest = map.Game.Realms.Keys.OrderByDescending(id => map.NewsMonths(id)).First();
        int farMonths = map.NewsMonths(farthest);
        var roadKind = RoadCatalog.Instance.Kinds.Where(k => !k.Wall && !k.Water && k.Pace < 1).OrderBy(k => k.Pace).First();
        var everywhere = new Road { Id = 9999, Kind = roadKind.Id, Realm = rome };
        everywhere.Nodes.AddRange(Enumerable.Range(0, map.Population!.Width * map.Population.Height));
        map.Game.Roads.Add(everywhere);
        int roadMonths = map.NewsMonths(farthest);
        map.Game.Roads.Remove(everywhere);
        Check(farMonths >= 3 && roadMonths < farMonths, $"news along roads should come faster ({farMonths} months without, {roadMonths} with)");
        map.PlayerState.Techs.Add("telegraph");
        Check(map.NewsMonths(farthest) == 0, "with the telegraph news should come at once");
        map.PlayerState.Techs.Remove("telegraph");
        var succession = new Facsimilia.Dynasties.ChronicleEvent(Facsimilia.Dynasties.ChronicleKind.Succession, egypt, "Egypt has a new king.");
        var war = new Facsimilia.Dynasties.ChronicleEvent(Facsimilia.Dynasties.ChronicleKind.War, egypt, "Egypt goes to war.");
        var heardNow = map.NewsArriving(new[] { succession, war });
        Check(heardNow.Count == 1 && heardNow[0] == war && map.Game.NewsOnTheRoad.Count == 1, "a succession in Egypt should be on the road while the war is known at once");
        int monthsBefore = map.Game.MonthsMarched;
        map.Game.MonthsMarched += egyptMonths;
        var later = map.NewsArriving(Array.Empty<Facsimilia.Dynasties.ChronicleEvent>());
        Check(later.Count == 1 && later[0].Text.Contains("to reach us") && map.Game.NewsOnTheRoad.Count == 0, "the news from Egypt should arrive, marked with its age");
        map.Game.MonthsMarched = monthsBefore;
        map.Game.DiplomacyFog = FogLevel.Full;
        int farRealm = map.Game.Realms.Keys.FirstOrDefault(id => !map.NewsReaches(id));
        Check(farRealm != 0, "with full fog some far realm's news should never reach Rome");
        Check(map.NewsArriving(new[] { new Facsimilia.Dynasties.ChronicleEvent(Facsimilia.Dynasties.ChronicleKind.Succession, farRealm, "far news") }).Count == 0
            && map.Game.NewsOnTheRoad.Count == 0, "news that never reaches you shouldn't be heard or queued");
        map.Game.DiplomacyFog = FogLevel.Light;
        map.NewsArriving(new[] { succession });   // on the road through the save

        // Last seen: an enemy army that slips out of sight stays where it was seen, until that spot is seen empty.
        var foe = et.Armies.First(a => !a.IsEmpty);
        map.UpdateLastSeen();
        Check(map.Game.LastSeen.ContainsKey((etruscans, foe.Id)), "an enemy army in sight should be noted");
        int seenAt = map.Game.LastSeen[(etruscans, foe.Id)].Node;
        var hiddenNodes = pop2.LandNodes.Where(i => !map.KnowsNode(i)).Take(2).ToArray();
        int monthNow = map.DemoYear * 12 + map.Game.MonthsMarched;
        foe.Node = hiddenNodes[0];
        map.Game.LastSeen[(etruscans, foe.Id)] = (hiddenNodes[1], monthNow - 2);   // seen there two months ago
        map.UpdateLastSeen();
        Check(map.Game.LastSeen.TryGetValue((etruscans, foe.Id), out var sighting) && sighting.Node == hiddenNodes[1],
            "an army out of sight should be remembered where it was last seen");
        map.Game.LastSeen[(etruscans, foe.Id)] = (hiddenNodes[1], monthNow - MapView.LastSeenMonths - 1);
        map.UpdateLastSeen();
        Check(!map.Game.LastSeen.ContainsKey((etruscans, foe.Id)), "a sighting over a year old should be forgotten");
        map.Game.LastSeen[(etruscans, foe.Id)] = (seenAt, monthNow);
        map.UpdateLastSeen();
        Check(!map.Game.LastSeen.ContainsKey((etruscans, foe.Id)), "a sighting is forgotten when its spot is seen empty");
        foe.Node = seenAt;

        // Search: it finds what Rome knows, and not what it doesn't.
        map.Game.DiplomacyFog = FogLevel.Full;
        Check(map.Search("Latium").Any(f => f.Kind == MapView.FoundKind.Province), "searching should find Latium");
        Check(map.Search("Carthage").Any(f => f.Kind == MapView.FoundKind.Realm), "searching should find Carthage, which Rome knows");
        int farOff = map.Game.Realms.Keys.First(id => !map.CanDealWith(id) && map.Registry.Realms.ContainsKey(id));
        Check(!map.Search(map.RealmName(farOff)).Any(f => f.Kind == MapView.FoundKind.Realm && f.Id == farOff), "searching shouldn't find a realm beyond Rome's knowledge");
        map.Game.DiplomacyFog = FogLevel.Light;

        // Save and load.
        map.Game.Pins.Add($"realm:{egypt}");
        map.Game.Notes.Add((map.CapitalProvince(rome), map.DemoYear, "Muster here in spring"));
        map.SendArmy(rome, home, far);
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var home2 = map2.Game.Realm(rome).Armies[0];
        Check(home2.Route.SequenceEqual(home.Route) && Math.Abs(home2.Supply - home.Supply) < 1e-6, "routes and baggage didn't survive the save");
        Check(map2.Game.MonthsMarched == map.Game.MonthsMarched, "the month didn't survive the save");
        Check(map2.Game.DiplomacyFog == FogLevel.Light && map2.Game.MapFog == FogLevel.Full
            && map2.Game.Explored.SequenceEqual(map.Game.Explored), "the fog settings and explored land didn't survive the save");
        Check(map2.Game.Pins.Contains($"realm:{egypt}"), "pins didn't survive the save");
        Check(map2.Game.Notes.Any(n => n.Text == "Muster here in spring"), "notes didn't survive the save");
        Check(map2.Game.NewsOnTheRoad.Count == 1 && map2.Game.NewsOnTheRoad[0].Text == "Egypt has a new king.", "news on the road didn't survive the save");

        // A whole year with the turn button marches all twelve months.
        map.AdvanceYear();
        Check(map.Game.MonthsMarched == 0, "after a year the months should start again");

        Finish($"March tests passed: across Roman Italy in {marched} month(s) (estimated {months:0.0}); closed borders; " +
            $"starving in the desert ({men - Military.Soldiers(lost):N0} men lost); {report.Title}, {report.Winner} won; fog; saves keep it.");
        map.Free();
        map2.Free();
    }
}
