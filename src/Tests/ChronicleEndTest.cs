using System.Linq;
using System.Threading.Tasks;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The end-of-game history: every 5 years each realm's people, silver,
/// soldiers and score are recorded; history's campaigns are checked at
/// their end dates for the closeness-to-history score; the player's rulers
/// are logged; and all of it survives a save.
/// </summary>
public partial class ChronicleEndTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("egypt");
        map.StartGame();
        Check(map.Game.RulerLog.Count == 1, "the first ruler should be logged at the start");
        for (int y = 0; y < 35; y++)
            map.AdvanceYear();
        var me = map.PlayerState;
        Check(me.History.Count >= 7, $"a record every 5 years: {me.History.Count} in 35 years");
        Check(map.Game.Realms.Values.All(s => s.History.Count > 0 || !map.RealmCensus().ContainsKey(s.RealmId)), "every realm should have a record");
        var (share, matched, checkedN) = map.HistoryCloseness();
        Check(checkedN > 0 && share is >= 0 and <= 1, $"history's campaigns up to 265 BC should be checked: {matched} of {checkedN}");
        Check(!map.GameOver, "the game isn't over in 265 BC");
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.PlayerState.History.Count == me.History.Count && map2.Game.HistoryChecks.Count == checkedN
              && map2.Game.RulerLog.Count == map.Game.RulerLog.Count, "the history record should survive a save");
        Finish($"Chronicle end tests passed: {me.History.Count} records in 35 years; {matched} of {checkedN} campaigns went as history had them " +
            $"({share:P0}); {map.Game.RulerLog.Count} ruler(s) logged; saves keep it.");
        map.Free();
        map2.Free();
    }
}
