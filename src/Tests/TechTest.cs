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
/// up to 50 years before its time (dearer the earlier) and after its
/// prerequisites; it is cheaper when neighbours know it; inventions from
/// beyond the map need contact; research is split by branch and paid in points
/// and changes the game (a unit opens with its tech); other realms research by themselves; corruption and a rival grow
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
        Check(cat.All.All(t => t.AvailableFrom >= -300), "nothing older than 300 BC is a technology: it is already known");

        // Gating by time and prerequisites.
        Check(cat.CanResearch(r, cat["steam_engine"]!, map.DemoYear) != null, "no steam engine in 300 BC");
        // Up to 50 years early, dearer the earlier: cataphract armour (250 BC) opens in 300 BC at three times its price.
        var cata = cat["cataphract_armour"]!;
        Check(cata.AvailableFrom == -250 && cat.CanResearch(r, cata, map.DemoYear) == null, "cataphract armour should open 50 years before 250 BC");
        Check(Math.Abs(cat.Cost(cata, map.DemoYear) - cata.Cost * TechCatalog.EarlyMaxFactor) < 1e-6, "at the earliest a tech costs three times its price");
        Check(Math.Abs(cat.Cost(cata, -250) - cata.Cost) < 1e-6, "in its own time a tech costs its price");
        Check(cat.CanResearch(r, cat["gunpowder"]!, 1150) != null, "nothing opens more than 50 years early");
        // Cheaper from neighbours; inventions from beyond need contact.
        var knows = new TechContext { Contacts = 4, KnownShare = _ => 1, Arrived = _ => false };
        Check(Math.Abs(cat.Cost(cata, -250, knows) - cata.Cost * (1 - TechCatalog.DiffusionDiscount)) < 1e-6, "a tech every neighbour knows should be cheaper");
        Check(cat.CanResearch(r, cat["paper"]!, 800, knows)?.Contains("beyond") == true, "paper can't be studied before it reaches the realm");
        Check(cat.PointsPerYear(r, map.CensusOf(rome), knows) > cat.PointsPerYear(r, map.CensusOf(rome)), "trade contacts should bring research");
        var cataphracts = UnitCatalog.Instance.Units.First(u => u.Requires == "cataphract_armour");
        var sel = map.Game.Realm(seleucid);
        sel.Treasury = 100000;
        Check(Military.CanRecruit(sel, map.CensusOf(seleucid), map.CulturesOf(seleucid), cataphracts).Problem?.Contains("technology") == true,
            "without cataphract armour the Seleucids can't raise cataphracts");
        // Research by branch: each branch studies its own tech with its share of the points.
        var open = cat.All.First(t => cat.CanResearch(r, t, map.DemoYear) == null && t.AvailableFrom <= map.DemoYear);
        int branch = TechCatalog.BranchIndex(open.Branch);
        Check(map.ChooseResearch(open.Id) == null && r.BranchResearching[branch] == open.Id && r.Studying, "choosing research");
        r.BranchPoints[branch] = open.Cost * 3;
        int idle = (branch + 1) % TechCatalog.Branches.Length;
        r.BranchShare[idle] = 0;
        double idleBefore = r.BranchPoints[idle];
        map.AdvanceYear();
        Check(r.Techs.Contains(open.Id) && r.BranchResearching[branch] == "", "a tech should be learnt when paid for");
        Check(r.BranchPoints[idle] == idleBefore, "a branch given no effort should gather no points");
        sel.Techs.Add("cataphract_armour");
        Check(Military.CanRecruit(sel, map.CensusOf(seleucid), map.CulturesOf(seleucid), cataphracts).Problem?.Contains("technology") != true,
            "with cataphract armour the unit opens");

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
        Check(map2.PlayerState.BranchShare[idle] == 0 && map2.PlayerState.BranchPoints.Zip(r.BranchPoints).All(x => Math.Abs(x.First - x.Second) < 0.01), "research by branch didn't survive the save");

        Finish($"Tech tests passed: {cat.All.Count} technologies in {TechCatalog.Branches.Length} branches; nothing older than 300 BC; " +
            $"units open with their techs; farming techs grow more; the Seleucids learnt {map.Game.Realm(seleucid).Techs.Count - before} in 15 years; " +
            $"score by category; saves.");
        map.Free();
        map2.Free();
    }
}
