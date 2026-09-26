using System.Linq;
using System.Threading.Tasks;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// A MapView seeded with the real 300 BC world, outside the scene tree (so
/// generation runs straight through with no frame yields, and no camera or
/// window is needed). Free() it when done.
/// </summary>
static class WorldFixture
{
    public static async Task<MapView> Seeded()
    {
        var map = new MapView();
        await map.SeedLandAndSea();
        await map.SeedRealCivs();
        await map.SeedFrontierZones();
        return map;
    }

    public static readonly string[] CivKeys = MapView.RealCivs.Select(c => c.Key).ToArray();

    public static int CountCells(MapView map, int owner)
    {
        int n = 0;
        foreach (int c in map.Grid.Cells)
            if (c == owner)
                n++;
        return n;
    }
}
