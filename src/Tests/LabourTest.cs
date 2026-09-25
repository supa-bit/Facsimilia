using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Labour as history had it: the shares of free, dependent and enslaved
/// follow data/labour.json through time (Italy's slave economy grows in
/// the late Republic, Egypt's royal farmers are dependents, slavery ends
/// at abolition); only free men (and half the dependents) fill the levies;
/// the enslaved dig more ore; conquest takes captives, who are sold beyond
/// what the realm keeps; and captives survive a save.
/// </summary>
public partial class LabourTest : TestRunner
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
        var h = map.LabourHistory()!;
        Check(h != null, "no labour history");
        var (_, italy300) = h!.Shares("Italia", -300);
        var (_, italy50) = h.Shares("Italia", -50);
        Check(italy50 > italy300 * 1.5, $"Italy's enslaved share should grow in the late Republic ({italy300:P0} -> {italy50:P0})");
        Check(h.Shares("Aegyptus", -300).Dependent > 0.4, "Egypt's royal farmers should be dependents");
        Check(h.Shares("Gallia", 1900).Enslaved == 0 && h.Shares("Gallia", 1800).Enslaved >= 0, "slavery should end at abolition (France, 1848)");

        int rome = map.CivRealmIds["rome"];
        var c = map.CensusOf(rome);
        Check(Math.Abs(c.Free + c.Dependent + c.Enslaved - c.People) < c.People * 0.01, "free, dependent and enslaved should add up to the people");
        Check(c.LevyShare < 1 && c.LevyShare > 0.7, $"Rome's levies should come from its free men ({c.LevyShare:P0})");
        Check(Labour.MineFactor(0.3) > Labour.MineFactor(0.05), "more enslaved, more ore");
        Check(Labour.ServileUnrest(0.3) > 0 && Labour.ServileUnrest(0.05) == 0, "many enslaved should breed unrest");

        // Captives: a big haul is partly kept, the rest sold.
        var r = map.Game.Realm(rome);
        r.Captives = c.People * 0.2;
        double before = r.Treasury;
        map.AdvanceYear();
        Check(r.LastCaptiveSales > 0 && r.Captives <= map.CensusOf(rome).People * h.KeptShare + 1,
            $"captives beyond what Rome keeps should be sold (sold for {r.LastCaptiveSales:0} talents)");
        Check(map.CensusOf(rome).Enslaved > c.Enslaved, "kept captives should count among the enslaved");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(Math.Abs(map2.Game.Realm(rome).Captives - r.Captives) < 1, "captives didn't survive the save");

        Finish($"Labour tests passed: Italy {italy300:P0} -> {italy50:P0} enslaved (300 -> 50 BC); Rome's levy share {c.LevyShare:P0}; " +
            $"captives sold for {r.LastCaptiveSales:N0} talents; abolition ends slavery.");
        map.Free();
        map2.Free();
    }
}
