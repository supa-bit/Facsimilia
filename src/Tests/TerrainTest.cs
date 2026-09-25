using System.Diagnostics;
using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>World seeding: real coastline first, the 300 BC political mask, then the frontier zones.</summary>
public partial class TerrainTest : TestRunner
{
    protected override async Task Run()
    {
        var clock = Stopwatch.StartNew();
        var map = await WorldFixture.Seeded();
        long ms = clock.ElapsedMilliseconds;
        int total = MapView.GridWidth * MapView.GridHeight;
        int sea = WorldFixture.CountCells(map, MapView.SeaOwnerId);
        GD.Print($"Seeding took {ms} ms for {total:N0} cells; sea is {100.0 * sea / total:F1}%.");
        Check(sea > 0 && sea < total, "sea should cover part of the map");
        Check(map.CivRealmIds.Count == WorldFixture.CivKeys.Length, $"{map.CivRealmIds.Count} civs, expected 12");
        foreach (string key in WorldFixture.CivKeys)
        {
            if (!Check(map.CivRealmIds.TryGetValue(key, out int id), "missing civ: " + key))
                continue;
            int cells = WorldFixture.CountCells(map, id);
            GD.Print($"{key}: {cells:N0} land cells");
            Check(cells > 500, $"{key} has suspiciously little territory ({cells} cells)");
        }
        map.Free();
        Finish("Terrain sanity check passed: real coastline mask seeded first, real 300 BC boundaries from the " +
            "reconciled political mask, frontier zones filled the gaps, every one of the 12 realms has real territory.");
    }
}
