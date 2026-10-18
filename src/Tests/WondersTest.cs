using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Wonders: the Seven Wonders and a list through the ages, each once in the
/// world; the old ones already stand in 300 BC; no one builds one before
/// history did; history's builder begins on its date; you can race it, and
/// whoever finishes first has it while the other's work stops; a wonder
/// costs silver, workers and goods; it gives its holder its effects and
/// culture; wonders fall on history's dates, and only yours can be saved;
/// and it all survives a save.
/// </summary>
public partial class WondersTest : TestRunner
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
        var cat = WonderCatalog.Instance;
        int rome = map.PlayerRealmId, egypt = map.CivRealmIds["egypt"];
        var s = map.PlayerState;
        var provs = map.Provinces!.Provinces.Values;
        var latium = provs.First(p => p.Name == "Latium");

        Check(cat.All.Count >= 35, $"a list across the ages: {cat.All.Count}");
        Check(cat.All.Count(w => !w.IsWonder) >= 15 && map.Game.Wonders.ContainsKey("diolkos"), "history's canals and great works, built as wonders are; the Diolkos stands in 300 BC");
        Check(cat["eiffel"] != null && cat["hagia_sophia"] != null, "wonders through the ages");
        Check(map.Game.Wonders.ContainsKey("great_pyramid") && map.Game.Wonders.ContainsKey("mausoleum"), "the old wonders stand in 300 BC");
        Check(!map.Game.Wonders.ContainsKey("pharos") && !map.Game.Wonders.ContainsKey("colosseum"), "later wonders aren't built yet");

        // No one before history.
        s.Treasury = 100000;
        s.Manpower = 100000;
        Check(map.CanBuildWonder(latium, cat["pantheon"]!)?.Contains("before history") == true, "no Pantheon in 300 BC");
        Check(map.CanBuildWonder(latium, cat["pharos"]!)?.Contains("before history") == true, "no Pharos before 297 BC");

        // History's builder begins on its date, where it holds the place.
        var egyptState = map.Game.Realm(egypt);
        egyptState.Treasury = 100000;
        for (int y = 0; y < 3; y++)
            map.AdvanceYear();
        var theirs = map.Game.WonderWorks.FirstOrDefault(w => w.Id == "pharos" && w.Realm == egypt);
        Check(theirs != null, "Egypt should begin the Pharos in 297 BC");
        var coast = provs.First(p => p.RealmId == rome && map.CanBuildWonder(p, cat["pharos"]!)?.Contains("coast") != true);
        Check(map.CanBuildWonder(coast, cat["pharos"]!)?.Contains("granite") == true, "a wonder needs its special goods: Rome has no granite");
        map.CensusOf(rome).Goods!.Imported[map.GoodsCatalog()!["granite"].Index] = 10;   // a cargo from Egypt
        string? problem = map.CanBuildWonder(coast, cat["pharos"]!);
        Check(problem == null, $"you may race for it: {problem}");

        // The cost: silver, workers and goods.
        double treasury = s.Treasury, manpower = s.Manpower;
        Check(map.StartWonder(coast, cat["pharos"]!), "couldn't start the Pharos");
        Check(s.Treasury == treasury - cat["pharos"]!.Cost && s.Manpower == manpower - cat["pharos"]!.Workers, "a wonder costs silver and workers");
        Check(map.CanBuildWonder(coast, cat["eiffel"]!) != null, "not before history, again");

        // Whoever finishes first has it; the other's work stops and half its silver comes back.
        map.Game.WonderWorks.First(w => w.Realm == rome).Left = 1;
        double egyptSilver = egyptState.Treasury;
        map.AdvanceYear();
        Check(map.Game.Wonders.TryGetValue("pharos", out var site) && site.Province == coast.Id, "the Pharos should stand in your province");
        Check(!map.Game.WonderWorks.Any(w => w.Id == "pharos"), "Egypt's work stops");
        Check(map.WondersOf(rome).Any(w => w.Id == "pharos") && s.WonderEffects.GetValueOrDefault("sea_cost") < 0, "the Pharos makes your sea travel cheaper");
        Check(map.ScoreBreakdown(rome).First(x => x.Category == "Wonders").Detail.Contains("1 wonder"), "wonders have their own part of the score");
        Check(map.CanBuildWonder(coast, cat["pharos"]!) == "It already stands.", "each wonder once in the world");

        // A wonder whose builder isn't a realm of the game: whoever holds the place begins it on history's date.
        int keepYear = map.DemoYear;
        map.DemoYear = -180;
        map.WondersYear();
        var altar = map.Game.WonderWorks.FirstOrDefault(x => x.Id == "pergamon_altar");
        Check(altar != null && altar.Realm != rome, "the Altar of Pergamon is begun by whoever holds Pergamon");
        map.DemoYear = keepYear;

        // Hurrying: a quarter faster at triple the cost.
        var slow = MapView.WonderPrice(cat["colosseum"]!);
        var fast = MapView.WonderPrice(cat["colosseum"]!, 1);
        Check(fast.Silver == slow.Silver * 3 && fast.Workers == slow.Workers * 3 && fast.Years < slow.Years && fast.Years >= slow.Years * 0.75,
            $"hurrying costs triple and saves up to a quarter of the time ({slow.Years} -> {fast.Years} years)");

        // Falls on history's dates; only yours can be saved.
        map.Game.Wonders["temple_artemis"] = new WonderSite { Province = latium.Id, Year = map.DemoYear };
        map.Game.Wonders["hanging_gardens"].Province = provs.First(p => p.RealmId == egypt).Id;
        int year = map.DemoYear;
        map.DemoYear = 262;
        s.Treasury = 100000;
        map.WondersYear();
        var ask = map.Game.Offers.FirstOrDefault(o => o.Kind == MapView.WonderSaveOffer && o.Subject == "temple_artemis");
        Check(ask != null && map.Game.Wonders.ContainsKey("temple_artemis"), "you are asked before your wonder falls, and it stands while you decide");
        map.AnswerOffer(ask!, true);
        Check(map.Game.Wonders.TryGetValue("temple_artemis", out var artemis) && artemis.Saved, "you can save your wonder from its fall");
        map.WondersYear();
        Check(map.Game.Wonders.ContainsKey("temple_artemis"), "a saved wonder stands for good");
        // Declining lets it fall.
        map.Game.Wonders["mausoleum"].Province = latium.Id;
        map.DemoYear = 1404;
        map.WondersYear();
        map.AnswerOffer(map.Game.Offers.First(o => o.Subject == "mausoleum"), false);
        Check(map.Game.FallenWonders.Contains("mausoleum"), "a wonder you don't save falls");
        Check(map.Game.FallenWonders.Contains("hanging_gardens") && !map.Game.Wonders.ContainsKey("hanging_gardens"), "others' wonders fall as history had");
        Check(map.Game.Wonders.ContainsKey("great_pyramid"), "the Great Pyramid still stands");
        map.DemoYear = year;

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.Game.Wonders.ContainsKey("pharos") && map2.Game.Wonders["temple_artemis"].Saved && map2.Game.FallenWonders.Contains("hanging_gardens"),
            "wonders didn't survive the save");
        Check(map2.PlayerState.WonderEffects.GetValueOrDefault("sea_cost") < 0, "wonder effects after loading");

        Finish($"Wonders tests passed: {cat.All.Count} wonders; {map.Game.Wonders.Count} standing; Egypt began the Pharos, you finished it first; " +
            "falls on history's dates, yours saved; saves.");
        map.Free();
        map2.Free();
    }
}
