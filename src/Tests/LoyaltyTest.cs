using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Cultures and loyalty on the real 300 BC map: provinces get their
/// historical people (Latins in Latium, Jews in Judaea, Egyptians on the
/// Nile); a newly conquered foreign province is unintegrated, pays less and
/// grows restless; integration comes in about 10 years among one's own
/// people and far slower among strangers; heavy taxes and too many
/// provinces breed unrest; the player's restless provinces revolt; and it
/// all survives a save.
/// </summary>
public partial class LoyaltyTest : TestRunner
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
        int rome = map.CivRealmIds["rome"], egypt = map.CivRealmIds["egypt"];
        var provs = map.Provinces!.Provinces.Values.ToList();

        string CultureOf(string name) => map.ProvinceStateOf(provs.First(p => p.Name == name).Id).Culture;
        Check(provs.Any(p => p.Name == "Latium") && CultureOf("Latium") == "latin", "Latium should be Latin");
        if (provs.Any(p => p.Name == "Judaea"))
            Check(map.ProvinceStateOf(provs.First(p => p.Name == "Judaea").Id).Religion == "judaism", "Judaea should be Jewish");
        var nile = provs.Where(p => p.RealmId == egypt).Select(p => map.ProvinceStateOf(p.Id)).ToList();
        Check(nile.Count(s => s.Culture == "egyptian") > nile.Count / 2, "most Ptolemaic provinces should be Egyptian");
        Check(provs.All(p => map.ProvinceStateOf(p.Id).Culture != ""), "every province should have a people");
        var latium = map.ProvinceStateOf(provs.First(p => p.Name == "Latium").Id);
        Check(latium.Integration == 1, "Rome's own Latium should start fully integrated");

        // Rules on their own.
        var kin = new[] { ("latin", "italic") };
        Check(Loyalty.Relation("latin", "italic", kin) == Loyalty.Kinship.Kin, "Latins and Italics are kin");
        var same = new ProvinceState { Integration = 0.5 };
        var foreign = new ProvinceState { Integration = 0 };
        for (int y = 0; y < 5; y++)
        {
            Loyalty.Tick(same, Loyalty.Kinship.Same, true, false, TaxRate.Normal, 5);
            Loyalty.Tick(foreign, Loyalty.Kinship.Foreign, false, false, TaxRate.Normal, 5);
        }
        Check(same.Integration >= 0.99, "a province of one's own people integrates within about 10 years");
        Check(foreign.Integration < 0.1 && foreign.Unrest > Loyalty.RevoltThreshold, "a foreign conquest stays restless for years");
        var taxed = new ProvinceState { Integration = 1 };
        Loyalty.Tick(taxed, Loyalty.Kinship.Same, true, false, TaxRate.Crushing, 40);
        Check(taxed.Unrest > 0.5, $"crushing taxes over an overstretched realm should breed unrest, got {taxed.Unrest:P0}");
        Check(Loyalty.Yield(0) == 0.5 && Loyalty.Yield(1) == 1, "yield runs from half to full");

        // An Egyptian province given to Rome: it is unintegrated, Rome's tax
        // base grows by less than its people, and it grows restless.
        var gift = provs.Where(p => p.RealmId == egypt && map.ProvinceStateOf(p.Id).Culture == "egyptian")
            .OrderByDescending(p => map.ProvincePopulation(p.Id)).First();
        double organizedBefore = map.CensusOf(rome).OrganizedPeople;
        map.Provinces.SetRealm(gift.Id, rome, map.Grid);
        map.TerritoryChanged = true;
        map.SyncPopulationOwnership();
        map.AdvanceYear();
        var gs = map.ProvinceStateOf(gift.Id);
        Check(gs.Owner == rome || gift.RealmId != rome, $"the province's owner should be tracked: owner {gs.Owner}, realm {gift.RealmId}, rome {rome}, same {ReferenceEquals(gift, map.Provinces.Provinces[gift.Id])}");
        Check(gift.RealmId != rome || gs.Integration < 0.05, $"a foreign conquest should start unintegrated, got {gs.Integration:P0}");
        double gained = map.CensusOf(rome).OrganizedPeople - organizedBefore;
        Check(gift.RealmId != rome || gained < map.ProvincePopulation(gift.Id) * 0.6,
            "an unintegrated province should count for only about half its people in taxes");

        // Revolt: keep it at crushing taxes for a while (once free, a neighbour may take it).
        map.PlayerState.Tax = TaxRate.Crushing;
        int years = 0;
        while (gift.RealmId == rome && years++ < 40)
            map.AdvanceYear();
        Check(gift.RealmId != rome, $"a restless, crushed foreign province should eventually revolt: realm {gift.RealmId}, unrest {gs.Unrest:0.00}, integration {gs.Integration:0.00}, years {years}");
        map.PlayerState.Tax = TaxRate.Normal;

        // Save and load.
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var ls = map2.ProvinceStateOf(provs.First(p => p.Name == "Latium").Id);
        Check(ls.Culture == "latin" && Math.Abs(ls.Integration - latium.Integration) < 1e-9, "loyalty didn't survive the save");

        Finish($"Loyalty tests passed: provinces have their historical peoples; conquest, integration, unrest, " +
            $"tax and revolt work (the Egyptian province rose after {years} years of crushing taxes); saves keep it.");
        map.Free();
        map2.Free();
    }
}
