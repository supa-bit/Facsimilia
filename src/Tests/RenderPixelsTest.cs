using System;
using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Samples the built map image at cells whose owner is known from the grid and
/// compares the pixel against that owner's color - catches byte-layout bugs in
/// BuildFullMapImage that pure ownership tests can't see.
/// </summary>
public partial class RenderPixelsTest : TestRunner
{
    // RGBA8 quantization (~0.0039 per step) always introduces sub-1% rounding.
    const float Tolerance = 0.01f;

    static bool Close(Color a, Color b) =>
        Math.Abs(a.R - b.R) <= Tolerance && Math.Abs(a.G - b.G) <= Tolerance
        && Math.Abs(a.B - b.B) <= Tolerance && Math.Abs(a.A - b.A) <= Tolerance;

    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.BuildFullMapImage();
        var image = map.MapImage!;
        int checkedCount = 0, mismatches = 0;

        var rng = new RandomNumberGenerator { Seed = 42 };
        for (int i = 0; i < 40; i++)
        {
            int x = rng.RandiRange(0, MapView.GridWidth - 1), y = rng.RandiRange(0, MapView.GridHeight - 1);
            var expected = map.ColorForOwner(map.Grid.GetOwner(x, y));
            checkedCount++;
            if (!Check(Close(image.GetPixel(x, y), expected), $"pixel ({x},{y}) is {image.GetPixel(x, y)}, expected {expected}"))
                mismatches++;
        }
        // One pixel from each realm, found on a coarse scan.
        foreach (var (key, realmId) in map.CivRealmIds)
        {
            bool found = false;
            for (int y = 0; y < MapView.GridHeight && !found; y += 37)
            {
                for (int x = 0; x < MapView.GridWidth && !found; x += 37)
                {
                    if (map.Grid.GetOwner(x, y) != realmId)
                        continue;
                    found = true;
                    checkedCount++;
                    var expected = map.GetRealm(realmId).Color;
                    if (!Check(Close(image.GetPixel(x, y), expected), $"{key} pixel ({x},{y}) is {image.GetPixel(x, y)}, expected {expected}"))
                        mismatches++;
                }
            }
            Check(found, $"no sample cell found for {key}");
        }
        map.Free();
        Finish($"Checked {checkedCount} pixels, {mismatches} mismatches.");
    }
}
