using System;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The crop model (data/crops.json on the land layer): crops, trees and
/// herds grow where history grew them, and the food capacity it gives each
/// region is consistent with history - no region held more people than its
/// land could feed in 300 BC, and the long-settled farming cores reached a
/// good share of it before 1700 (HYDE), while frontier steppes did not.
/// </summary>
public partial class CropModelTest : TestRunner
{
    LandLayer _land = null!;
    CropModel _crops = null!;

    double S(string crop, double lon, double lat) => _crops.Suitability[crop][_land.NodeAt(lon, lat)];

    void High(string place, string crop, double lon, double lat, double min = 0.5) =>
        Check(S(crop, lon, lat) >= min, $"{crop} should thrive at {place}: {S(crop, lon, lat):0.00}");

    void Low(string place, string crop, double lon, double lat, double max = 0.1) =>
        Check(S(crop, lon, lat) <= max, $"{crop} shouldn't grow at {place}: {S(crop, lon, lat):0.00}");

    public override void _Initialize()
    {
        var pop = new PopulationEngine();
        pop.LoadHyde();
        _land = LandLayer.Load()!;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _crops = CropModel.Build(_land, pop.LandNodes)!;
        long ms = clock.ElapsedMilliseconds;
        Check(_crops != null, "crop model failed to build");
        Check(_crops!.Profiles.Count >= 30, "expected at least 30 profiles");

        High("Athens", "olives", 23.7, 38.0);
        Low("the Alps", "olives", 10.0, 46.5);
        Low("central Gaul", "olives", 2.0, 46.0);
        High("Luxor", "dates", 32.65, 25.7);
        Low("Athens", "dates", 23.7, 38.0);
        High("the Nile delta", "wheat", 31.0, 30.9);
        High("the Nile delta", "barley", 31.0, 30.9);
        High("Sicily", "wheat", 14.3, 37.5, 0.4);
        High("Bordeaux", "grapes", -0.5, 44.8, 0.4);
        Low("central Sahara", "wheat", 10.0, 24.0, 0.01);
        High("the Pontic steppe", "sheep", 35.0, 47.0);
        High("the Nafud", "camels", 42.0, 28.0, 0.4);
        Low("the Po valley", "camels", 11.0, 45.0);
        High("the Sahel", "millet", 30.0, 13.0, 0.3);
        int sahara = _land.NodeAt(10, 24);
        Check(_crops.Capacity[sahara] / _crops.AreaKm2[sahara] < 0.3, "the central Sahara should feed almost nobody (a few nomads)");

        // Region capacity against history.
        var caps = _crops.RegionCapacity(pop);
        var h300 = new double[pop.RegionCount + 1];
        var hMax = new double[pop.RegionCount + 1];
        foreach (int i in pop.LandNodes)
            h300[pop.RegionOf(i)] += Math.Max(0f, pop.Keyframes[0].Pop[i]);
        foreach (var kf in pop.Keyframes.Where(k => k.Year <= 1700))
        {
            var sum = new double[pop.RegionCount + 1];
            foreach (int i in pop.LandNodes)
                sum[pop.RegionOf(i)] += Math.Max(0f, kf.Pop[i]);
            for (int r = 1; r <= pop.RegionCount; r++)
                hMax[r] = Math.Max(hMax[r], sum[r]);
        }
        double Ratio(string region, double[] h)
        {
            int r = pop.Regions.First(x => x.Name == region).Id;
            return h[r] / caps[r];
        }
        for (int r = 1; r <= pop.RegionCount; r++)
            Check(h300[r] < caps[r], $"{pop.Regions[r - 1].Name} held more people in 300 BC than its land feeds");
        foreach (string core in new[] { "Italia", "Aegyptus", "Syria", "Gallia" })
        {
            double ratio = Ratio(core, hMax);
            Check(ratio > 0.3 && ratio < 1.1, $"{core}: its pre-1700 peak is {ratio:P0} of its food capacity, expected 30-110%");
        }
        foreach (string frontier in new[] { "Sarmatia", "Scythia" })
            Check(Ratio(frontier, h300) < 0.05, $"{frontier} should be barely farmed in 300 BC");
        double total = caps.Sum();
        Check(ms < 5000, $"crop model took {ms} ms");

        Finish($"Crop model tests passed: {_crops.Profiles.Count} crops, trees, herds and fisheries grow where history " +
            $"grew them; the map could feed {total / 1e6:0} million (39 million lived there in 300 BC); Italia's pre-1700 " +
            $"peak is {Ratio("Italia", hMax):P0} of its capacity, Egypt's {Ratio("Aegyptus", hMax):P0}; built in {ms} ms.");
    }
}
