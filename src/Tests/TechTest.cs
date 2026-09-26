using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Technology, administration and the score: the tree is large and
/// consistent (every prerequisite exists); realms start knowing what their
/// people knew in 300 BC (the Successors more than the Gauls); a tech opens
/// only in its time and after its prerequisites; research is paid in points
/// and changes the game (polyremes open the quinqueremes Rome lacked in 300
/// BC); other realms research by themselves; corruption and a rival grow
/// with overreach; the score is split by category; and it all survives a save.
/// </summary>
public partial class TechTest : TestRunner
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
        var cat = TechCatalog.Instance;
        Check(cat.All.Count >= 500, $"the tree should be large: {cat.All.Count} techs");
        var ids = cat.All.Select(t => t.Id).ToHashSet();
        Check(cat.All.All(t => t.Requires.All(ids.Contains)), "every prerequisite should exist");
        Check(TechCatalog.Branches.All(b => cat.All.Count(t => t.Branch == b) >= 20), "every branch should have at least 20 techs");

        int rome = map.PlayerRealmId, seleucid = map.CivRealmIds["seleucid"], gaul = map.CivRealmIds["gaul"];
        var r = map.PlayerState;
        Check(map.Game.Realm(seleucid).Techs.Count > map.Game.Realm(gaul).Techs.Count, "the Successors should know more than the Gauls");
        Check(r.Techs.Contains("manipular") && !r.Techs.Contains("polyremes"), "Rome has the manipular legion but not yet the polyremes");

        // Gating by time and prerequisites.
        Check(cat.CanResearch(r, cat["steam_engine"]!, map.DemoYear) != null, "no steam engine in 300 BC");
        Check(cat.CanResearch(r, cat["polyremes"]!, map.DemoYear) == null, "polyremes are open to Rome");
        var quinquereme = UnitCatalog.Instance["quinqueremes"];
        var c = map.CensusOf(rome);
        r.Treasury = 10000;
        Check(Military.CanRecruit(r, c, map.CulturesOf(rome), quinquereme).Problem?.Contains("technology") == true,
            "without polyremes Rome can't build quinqueremes");
        Check(map.ChooseResearch("polyremes") == null && r.Researching == "polyremes", "choosing research");
        r.ResearchPoints = cat["polyremes"]!.Cost;
        map.AdvanceYear();
        Check(r.Techs.Contains("polyremes") && r.Researching == "", "polyremes should be learnt when paid for");
        Check(Military.CanRecruit(r, map.CensusOf(rome), map.CulturesOf(rome), quinquereme).Problem == null,
            "with polyremes Rome can build quinqueremes (as in 261 BC)");

        // Effects: more field output with a new farming tech.
        var goods = map.GoodsCatalog()!;
        double Grain() => new[] { "wheat", "barley", "emmer" }.Sum(g => map.CensusOf(rome).Goods!.Produced[goods[g].Index]);
        map.InvalidateGoods();
        double grain = Grain();
        r.Techs.Add("water_lifting");
        r.Techs.Add("seed_selection");
        map.InvalidateGoods();
        Check(Grain() > grain * 1.03, $"farming technology should grow more grain ({grain:0} -> {Grain():0})");
        int capacity = map.AdminCapacity(rome);
        r.Techs.Add("provincial_system");
        Check(map.AdminCapacity(rome) > capacity, "governance technology should let the court manage more provinces");

        // Other realms research by themselves.
        int before = map.Game.Realm(seleucid).Techs.Count;
        for (int y = 0; y < 15; y++)
            map.AdvanceYear();
        Check(map.Game.Realm(seleucid).Techs.Count > before, "other realms should research");

        // Corruption and a rival grow with overreach.
        double corruption = map.Corruption(rome);
        Check(corruption > 0 && corruption < 0.2, $"a small realm should be only a little corrupt ({corruption:P0})");
        Check(map.ScoreBreakdown(rome).Select(x => x.Category).SequenceEqual(new[] { "People", "Land", "Wealth", "Culture", "Dynasty", "Goals" }),
            "the score should be split into its categories");
        Check(map.ScoreBreakdown(rome).First(x => x.Category == "Culture").Points > 0, "knowledge should count for culture");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.PlayerState.Techs.SetEquals(r.Techs), "technologies didn't survive the save");

        Finish($"Tech tests passed: {cat.All.Count} technologies in {TechCatalog.Branches.Length} branches; Rome learnt polyremes and " +
            $"built quinqueremes; farming techs grow more; the Seleucids learnt {map.Game.Realm(seleucid).Techs.Count - before} in 15 years; " +
            $"score by category; saves.");
        map.Free();
        map2.Free();
    }
}
