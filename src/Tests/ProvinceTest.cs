using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Provinces on the real 300 BC map: every realm cell is in exactly one
/// province of its own realm, provinces are sensibly sized and named, and
/// generation is deterministic and quick.
/// </summary>
public partial class ProvinceTest : TestRunner
{
    protected override async Task Run()
    {
        var world = await WorldFixture.Seeded();
        var seeds = ProvinceGenerator.LoadSeeds();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var tribal = world.TribalRealmIds().ToHashSet();
        Check(tribal.Count == 3, $"expected 3 tribal realms, found {tribal.Count}");
        var map = ProvinceGenerator.Generate(world.Grid, seeds, tribal);
        long ms = clock.ElapsedMilliseconds;
        int total = map.Provinces.Count;
        int[] owner = world.Grid.Cells;

        int realmCells = 0, wrong = 0, missing = 0, strayOnSea = 0;
        for (int i = 0; i < owner.Length; i++)
        {
            int o = owner[i], p = map.Cells[i];
            bool isRealm = o > 0 && o != MapView.SeaOwnerId && !tribal.Contains(o);
            if (!isRealm)
            {
                if (p != 0) strayOnSea++;
                continue;
            }
            realmCells++;
            if (p == 0) missing++;
            else if (!map.Provinces.TryGetValue(p, out var prov) || prov.RealmId != o) wrong++;
        }
        Check(missing == 0, $"{missing} realm cells have no province");
        Check(wrong == 0, $"{wrong} cells are in a province of another realm");
        Check(strayOnSea == 0, $"{strayOnSea} sea or unclaimed cells have a province");
        Check(map.Provinces.Values.Sum(p => p.CellCount) == realmCells, "province cell counts don't add up");

        var sizes = map.Provinces.Values.Select(p => p.CellCount).OrderBy(n => n).ToList();
        var names = map.Provinces.Values.Select(p => p.Name).ToList();
        Check(names.Distinct().Count() == names.Count, "two provinces share a name: " +
            string.Join(", ", names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));
        foreach (var (key, realmId) in world.CivRealmIds)
            Check(map.Provinces.Values.Any(p => p.RealmId == realmId) != tribal.Contains(realmId),
                tribal.Contains(realmId) ? $"{key} is tribal and should start unorganized" : $"{key} has no provinces");
        int historical = map.Provinces.Values.Count(p => seeds.Any(s => s.Name == p.Name));
        Check(historical > map.Provinces.Count / 2, $"only {historical} of {map.Provinces.Count} provinces are historical regions");
        foreach (var p in map.Provinces.Values)
            Check(map.At((int)p.LabelCell.X, (int)p.LabelCell.Y) == p.Id, $"{p.Name}'s name is placed outside it");

        // Deterministic.
        var again = ProvinceGenerator.Generate(world.Grid, seeds, tribal);
        Check(again.Cells.AsSpan().SequenceEqual(map.Cells), "generation isn't deterministic");

        // Handing a province to another realm moves all its cells with it.
        var latium = map.Provinces.Values.First(p => p.Name == "Latium");
        int carthage = world.CivRealmIds["carthage"];
        map.SetRealm(latium.Id, carthage, world.Grid);
        bool moved = true;
        for (int i = 0; i < owner.Length; i++)
            if (map.Cells[i] == latium.Id && owner[i] != carthage)
                moved = false;
        Check(moved && latium.RealmId == carthage, "SetRealm didn't move every cell of the province");

        // Moving cells between provinces keeps counts right; an emptied province disappears.
        var attica = map.Provinces.Values.First(p => p.Name == "Attica");
        var fresh = map.Create("Test Province", attica.RealmId);
        var atticaCells = Enumerable.Range(0, map.Cells.Length).Where(i => map.Cells[i] == attica.Id).ToList();
        int before = attica.CellCount;
        foreach (int cell in atticaCells.Take(100))
            map.Assign(cell, fresh.Id);
        Check(fresh.CellCount == 100 && attica.CellCount == before - 100, "cell counts after moving cells");
        foreach (int cell in atticaCells.Skip(100))
            map.Assign(cell, ProvinceMap.None);
        Check(!map.Provinces.ContainsKey(attica.Id), "an emptied province should be removed");
        foreach (int cell in atticaCells.Take(100))
            map.Assign(cell, ProvinceMap.None);
        Check(!map.Provinces.ContainsKey(fresh.Id), "the test province should be removed too");

        // Save round trip.
        var loaded = ProvinceMap.FromSave(map.Width, map.Height, map.ToDict(), map.CellBytes());
        Check(loaded != null && loaded.Cells.AsSpan().SequenceEqual(map.Cells)
            && loaded.Provinces.Count == map.Provinces.Count && loaded.Provinces[latium.Id].Name == "Latium"
            && loaded.Provinces[latium.Id].RealmId == carthage && loaded.NextId == map.NextId, "save round trip");

        const double KmPerCell = 0.59;
        string Report(int realmId) => $"{map.Provinces.Values.Count(p => p.RealmId == realmId)}";
        GD.Print("Provinces per realm: " + string.Join(", ", world.CivRealmIds.Select(kv => $"{kv.Key} {Report(kv.Value)}")));
        GD.Print("Smallest: " + string.Join(", ", map.Provinces.Values.OrderBy(p => p.CellCount).Take(8).Select(p => $"{p.Name} {p.CellCount * KmPerCell:N0} km²")));
        GD.Print("Largest: " + string.Join(", ", map.Provinces.Values.OrderByDescending(p => p.CellCount).Take(8).Select(p => $"{p.Name} {p.CellCount * KmPerCell:N0} km²")));
        GD.Print("Added names: " + string.Join(", ", map.Provinces.Values.Where(p => !seeds.Any(s => s.Name == p.Name)).Select(p => p.Name)));
        Check(ms < 4000, $"generation took {ms} ms");

        Finish($"Province tests passed: {total} provinces ({historical} historical regions) generated in {ms} ms; " +
            $"median {sizes[sizes.Count / 2] * KmPerCell:N0} km², every realm cell in one province of its own realm, deterministic, saves.");
        world.Free();
    }
}
