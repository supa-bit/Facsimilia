using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Provinces, part one: a focus that takes hold over ten years and turns
/// the province's work; each province's historical seat; splitting and
/// joining your provinces at a cost of loyalty; each province's history;
/// and it all survives a save.
/// </summary>
public partial class ProvinceWorkTest : TestRunner
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
        int rome = map.PlayerRealmId;
        var provs = map.Provinces!.Provinces.Values;
        var latium = provs.First(p => p.Name == "Latium");
        var campania = provs.First(p => p.Name == "Campania");

        // Seats.
        Check(map.ProvinceSeat(latium.Id) == "Roma", $"Latium's seat is Roma ({map.ProvinceSeat(latium.Id)})");
        Check(provs.Where(p => p.RealmId > 0).All(p => map.ProvinceSeat(p.Id) != null || map.TownsIn(p.Id).Count == 0), "every province with towns has a seat");

        // Focus: herding raises the herds slowly, and costs the fields a little.
        var goods = map.GoodsCatalog()!;
        double Herd() => goods.Goods.Where(g => g.Work == "herd").Sum(g => map.CensusOf(rome).Goods!.Produced[g.Index]);
        double Field() => goods.Goods.Where(g => g.Work == "field").Sum(g => map.CensusOf(rome).Goods!.Produced[g.Index]);
        map.InvalidateGoods();
        double herd = Herd(), field = Field();
        Check(map.SetFocus(campania.Id, "herding") == null, "turn Campania to herding");
        var cs = map.ProvinceStateOf(campania.Id);
        Check(cs.FocusLevel == 0, "a new focus starts from nothing");
        cs.FocusLevel = 1;
        map.InvalidateGoods();
        Check(Herd() > herd && Field() < field, $"herding at full: herds {herd:0} -> {Herd():0}, fields {field:0} -> {Field():0}");
        cs.FocusLevel = 0;
        map.AdvanceYear();
        Check(Math.Abs(cs.FocusLevel - MapView.FocusPerYear) < 1e-9, "a focus takes hold a tenth a year");
        Check(cs.Records.Count == 1 && cs.Records[0].People > 0, "each year is recorded for the province's graphs");
        double manpower = map.RealmBuildingEffects(rome, map.CensusOf(rome).People).Manpower;
        map.SetFocus(latium.Id, "soldiers");
        map.ProvinceStateOf(latium.Id).FocusLevel = 1;
        Check(map.RealmBuildingEffects(rome, map.CensusOf(rome).People).Manpower > manpower, "a soldiers' focus raises the levies");
        Check(cs.History.Any(h => h.Text.Contains("herding")), "the focus is in the province's history");

        // Split and join.
        var big = provs.Where(p => p.RealmId == rome).OrderByDescending(p => p.CellCount).First();
        int cells = big.CellCount;
        double unrest = map.ProvinceStateOf(big.Id).Unrest;
        var piece = map.SplitProvince(big.Id);
        Check(piece != null && piece.RealmId == rome && big.CellCount + piece.CellCount == cells && Math.Abs(big.CellCount - piece.CellCount) < cells / 5,
            $"{big.Name} splits in two halves ({big.CellCount} and {piece?.CellCount} of {cells} cells)");
        Check(map.ProvinceStateOf(big.Id).Unrest > unrest && map.ProvinceStateOf(piece!.Id).Unrest > unrest, "splitting costs loyalty for a while");
        Check(map.CanJoin(big, piece) == null, "the halves border each other");
        Check(map.JoinProvince(big.Id, piece.Id) == null && big.CellCount == cells && !map.Provinces.Provinces.ContainsKey(piece.Id), "joined again into one");
        var foreign = provs.First(p => p.RealmId != rome && p.RealmId > 0);
        Check(map.CanJoin(big, foreign) != null && map.CanSplit(foreign) != null, "only your own provinces");

        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var cs2 = map2.ProvinceStateOf(campania.Id);
        Check(cs2.Focus == "herding" && Math.Abs(cs2.FocusLevel - cs.FocusLevel) < 1e-9 && cs2.History.Count == cs.History.Count && cs2.Records.Count == cs.Records.Count, "focus, history and graphs didn't survive the save");

        Finish($"Province tests passed: seats ({map.ProvinceSeat(latium.Id)}, {map.ProvinceSeat(campania.Id)}); focus; split and join; history ({cs.History.Count} lines in Campania); saves.");
        map.Free();
        map2.Free();
    }
}
