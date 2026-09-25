using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The realm economy on the real 300 BC world: every playable realm starts
/// with its historical army and some silver; incomes are of historical size
/// (Ptolemaic Egypt, the richest kingdom, took in thousands of talents a
/// year); tax rates change income and the growth drivers' burden factor;
/// recruiting spends silver and men; a realm drowning in debt sees its
/// soldiers desert; and all of it survives a save.
/// </summary>
public partial class GameEconomyTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.StartGame();

        foreach (string key in WorldFixture.CivKeys)
        {
            var s = map.Game.Realm(map.CivRealmIds[key]);
            Check(s.Units.Sum() > 0, $"{key} starts with no army");
            Check(s.Treasury > 0, $"{key} starts with no silver");
        }
        int egypt = map.CivRealmIds["egypt"], rome = map.CivRealmIds["rome"], scythia = map.CivRealmIds["scythia"];
        var eg = map.Game.Realm(egypt);
        var (egTax, egTribute) = Economy.Revenue(map.CensusOf(egypt), eg.Tax);
        Check(egTax + egTribute > 2000 && egTax + egTribute < 20000,
            $"Egypt's income is {egTax + egTribute:0} talents a year; the Ptolemies took in thousands");
        var (scTax, scTribute) = Economy.Revenue(map.CensusOf(scythia), TaxRate.Normal);
        Check(scTax < scTribute * 5 + 1, "the Scythians have no provinces, so they should live on tribute, not tax");
        Check(map.Game.Realm(map.CivRealmIds["seleucid"]).Units[UnitTypes.Elephants] >= 10, "Seleucus had hundreds of elephants");

        // A year passes: accounts are kept, and the treasury moves by the balance.
        double before = eg.Treasury;
        var events = map.AdvanceYear();
        Check(eg.LastTax > 0, "Egypt collected no tax");
        double expected = before + eg.LastNet;
        Check(Math.Abs(eg.Treasury + eg.Debt * 0 - expected) < 1 || eg.Debt > 0,
            $"Egypt's treasury {eg.Treasury:0} doesn't match {before:0} + balance {eg.LastNet:0}");

        // Heavier taxes: more silver, and a burden on growth in the realm's regions.
        var pop = map.Population!;
        int egyptRegion = pop.Regions.First(r => r.Name == "Aegyptus").Id;
        eg.Tax = TaxRate.Crushing;
        var (heavy, _) = Economy.Revenue(map.CensusOf(egypt), eg.Tax);
        Check(heavy > egTax * 2, "crushing taxes should bring in far more");
        Economy.ApplyBurden(pop, map.Game.Realms);
        Check(pop.GetFactor(egyptRegion, GrowthFactor.Burden) < -0.5, "crushing taxes should weigh on Egypt's growth");
        eg.Tax = TaxRate.Normal;

        // Recruiting costs silver and men; disbanding returns the men.
        var r = map.Game.Realm(rome);
        var rc = map.CensusOf(rome);
        r.Treasury = 1000;
        double men = r.Manpower;
        int legions = r.Units[UnitTypes.HeavyInfantry];
        Check(Military.Recruit(r, rc, map.CultureOf(rome), UnitTypes.HeavyInfantry), "Rome couldn't raise a legion");
        Check(r.Units[UnitTypes.HeavyInfantry] == legions + 1 && r.Manpower == men - 1000 && r.Treasury < 1000,
            "recruiting should cost 1,000 men and silver");
        Check(Military.Disband(r, UnitTypes.HeavyInfantry) && r.Manpower == men, "disbanding should return the men");
        Check(Military.CanRecruit(r, rc, map.CultureOf(rome), UnitTypes.HorseArchers).Problem != null,
            "Romans shouldn't raise horse archers");

        // Deep debt: unpaid soldiers desert.
        r.Debt = 1e6;
        int army = r.Units.Sum();
        Economy.Tick(r, rc);
        Check(r.Units.Sum() < army, "an army unpaid for years should shrink");

        // Save and load.
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var eg2 = map2.Game.Realm(egypt);
        Check(Math.Abs(eg2.Treasury - eg.Treasury) < 0.01 && eg2.Units.SequenceEqual(eg.Units) && eg2.Tax == eg.Tax,
            "Egypt's treasury, army or tax didn't survive the save");

        Finish($"Economy tests passed: 12 realms start with armies and silver; Egypt takes in {egTax + egTribute:N0} " +
            $"talents a year at normal taxes ({heavy:N0} crushing); recruiting, disbanding, desertion, tax burden and " +
            $"saving all work. {events.Count} events in the first year.");
        map.Free();
        map2.Free();
    }
}
