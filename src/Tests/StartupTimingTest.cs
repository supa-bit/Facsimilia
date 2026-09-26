using Facsimilia.World;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The one-time cost of starting a game: seeding, building the display image,
/// and computing realm-name positions (rendering setup excluded).
/// </summary>
public partial class StartupTimingTest : TestRunner
{
    protected override async Task Run()
    {
        var clock = Stopwatch.StartNew();
        var map = await WorldFixture.Seeded();
        long seeded = clock.ElapsedMilliseconds;
        await map.BuildFullMapImage();
        long imaged = clock.ElapsedMilliseconds;
        var centroids = await map.ComputeCentroids();
        long total = clock.ElapsedMilliseconds;
        GD.Print($"Seeding:          {seeded,6} ms");
        GD.Print($"Build map image:  {imaged - seeded,6} ms");
        GD.Print($"Realm centroids:  {total - imaged,6} ms");
        Check(centroids.Count == MapView.RealCivs.Length, $"{centroids.Count} centroids, expected {MapView.RealCivs.Length}");
        map.Free();
        Finish($"TOTAL startup cost: {total} ms ({total / 1000.0:F1} s), centroids for {centroids.Count} realms.");
    }
}
