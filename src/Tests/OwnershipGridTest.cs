using System.Threading.Tasks;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>Ownership lookups, bounds, contiguity and flood fill.</summary>
public partial class OwnershipGridTest : TestRunner
{
    protected override Task Run()
    {
        var grid = new OwnershipGrid(20, 20);
        grid.FillRect(0, 0, 10, 10, 1);
        grid.FillRect(10, 0, 20, 10, 2);
        Check(grid.GetOwner(5, 5) == 1 && grid.GetOwner(15, 5) == 2 && grid.GetOwner(5, 15) == 0, "wrong owners");
        Check(grid.GetOwner(-1, 0) == -1, "out of bounds should be -1");
        Check(grid.IsContiguous(1) && grid.IsContiguous(2), "rectangles should be contiguous");

        // Detach one cell of realm 1 from the rest: contiguity breaks.
        grid.SetOwner(3, 3, 0);
        grid.SetOwner(2, 2, 5);
        grid.SetOwner(17, 17, 1);
        Check(!grid.IsContiguous(1), "a detached cell should break contiguity");

        var region = grid.FloodFillRegion(1, 1);
        Check(region.Count == 98, $"flood fill found {region.Count} cells, expected 98");
        Finish($"OwnershipGrid tests passed (flood-filled region size: {region.Count})");
        return Task.CompletedTask;
    }
}
