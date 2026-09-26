using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Buildings, province taxes and remedies: a building needs what the land
/// offers (a harbour a coast), costs silver and takes years; irrigation
/// grows more grain, walls hold longer, temples calm; a province can be
/// taxed apart from the realm; a treasury in trouble is offered history's
/// remedies, each with its price; and it all survives a save.
/// </summary>
public partial class BuildingsTest : TestRunner
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
        var cat = BuildingCatalog.Instance;
        int rome = map.PlayerRealmId;
        var provs = map.Provinces!.Provinces.Values;
        var latium = provs.First(p => p.Name == "Latium");
        var campania = provs.First(p => p.Name == "Campania");
        var s = map.PlayerState;
        s.Treasury = 5000;

        Check(map.CanBuild(latium, cat["harbour"]!) == null, $"Latium has a coast: {map.CanBuild(latium, cat["harbour"]!)}");
        var inland = provs.Where(p => p.RealmId == rome).FirstOrDefault(p => map.CanBuild(p, cat["harbour"]!) == "Needs a coast.");
        Check(provs.Where(p => p.RealmId != rome).All(p => map.CanBuild(p, cat["granary"]!) != null), "only your own provinces");

        // Building takes silver and years.
        double before = s.Treasury;
        Check(map.StartBuilding(latium, cat["walls"]!), "couldn't start the walls");
        Check(s.Treasury == before - cat["walls"]!.Cost, "the walls should be paid for");
        Check(map.CanBuild(latium, cat["temple"]!) != null, "one building at a time in a province");
        double garrison = map.GarrisonOf(latium);
        for (int y = 0; y < cat["walls"]!.Years; y++)
            map.AdvanceYear();
        var ps = map.ProvinceStateOf(latium.Id);
        Check(ps.Level("walls") == 1 && ps.Building == "", "the walls should be finished after their years");
        Check(map.GarrisonOf(latium) >= garrison + 3.9, "walls should strengthen the garrison");

        // Irrigation grows more grain.
        var goodsCat = map.GoodsCatalog()!;
        double Grain() => new[] { "wheat", "durum_wheat", "emmer", "barley" }.Sum(g => map.CensusOf(rome).Goods!.Produced[goodsCat[g].Index]);
        map.InvalidateGoods();
        double grain = Grain();
        map.ProvinceStateOf(campania.Id).Buildings["irrigation"] = 1;
        map.InvalidateGoods();
        Check(Grain() > grain * 1.03, $"irrigation in Campania should grow more grain ({grain:0} -> {Grain():0})");

        // A province's own tax rate.
        map.InvalidateGoods();
        double tax = Economy.Revenue(map.CensusOf(rome), s.Tax).Tax;
        map.ProvinceStateOf(campania.Id).Tax = TaxRate.Crushing;
        map.InvalidateCensus();
        Check(Economy.Revenue(map.CensusOf(rome), s.Tax).Tax > tax * 1.05, "crushing taxes in Campania should raise the realm's tax");
        double unrest = map.ProvinceStateOf(campania.Id).Unrest;
        map.AdvanceYear();
        Check(map.ProvinceStateOf(campania.Id).Unrest > unrest + 0.2, "crushing taxes should make Campania restless");
        map.ProvinceStateOf(campania.Id).Tax = null;

        // Remedies only in trouble.
        s.Treasury = 5000;
        s.Debt = 0;
        Check(map.RemediesNow().All(r => r.Problem != null), "no remedies needed with a full treasury");
        s.Treasury = 0;
        s.Debt = 3000;
        var debase = map.RemediesNow().First(r => r.Remedy == Remedies.Debase);
        Check(debase.Problem == null && debase.Silver > 0, "debasing the coin should be on offer in debt");
        Check(map.TakeRemedy("debase") != null && Remedies.Active(s, "debase") && s.Debt < 3000, "debasing should bring silver and be remembered");
        Check(map.RemediesNow().First(r => r.Remedy == Remedies.Debase).Problem != null, "a remedy can't be taken again while felt");
        Check(Remedies.Unrest(s) > 0 && Remedies.CustomsKept(s) < 1, "debasement should cost trust and calm");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.ProvinceStateOf(latium.Id).Level("walls") == 1 && map2.ProvinceStateOf(campania.Id).Level("irrigation") == 1
            && map2.PlayerState.RemedyYears.ContainsKey("debase"), "buildings or remedies didn't survive the save");

        Finish($"Buildings tests passed: {cat.All.Count} buildings; walls built in {cat["walls"]!.Years} years raise the garrison " +
            $"{garrison:0.0} -> {map.GarrisonOf(latium):0.0}; irrigation grows more grain; province taxes; remedies in debt; saves.");
        map.Free();
        map2.Free();
    }
}
