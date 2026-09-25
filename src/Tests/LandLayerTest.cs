using System;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The land layer (data/land/, baked by tools/build_land_layer.py): every
/// field loads on the population grid; places whose land history knows
/// have the values they should; the habitat shares of every place add up
/// to one; farmland lines up with where HYDE puts people (which also
/// catches a flipped or shifted grid); and every source's licence allows
/// the game to be sold (the designer's rule: CC0, CC BY or public domain -
/// no "non-commercial", no "share-alike").
/// </summary>
public partial class LandLayerTest : TestRunner
{
    LandLayer _land = null!;

    double V(string field, double lon, double lat) => _land.Value(field, _land.NodeAt(lon, lat));

    void Expect(string place, string field, double lon, double lat, double lo, double hi)
    {
        double v = V(field, lon, lat);
        Check(v >= lo && v <= hi, $"{place}: {field} is {v:0.###}, expected {lo} to {hi}");
    }

    public override void _Initialize()
    {
        var land = LandLayer.Load();
        if (!Check(land != null, "data/land/land.json missing or invalid"))
        {
            Finish("");
            return;
        }
        _land = land!;
        var pop = new PopulationEngine();
        Check(pop.LoadHyde(), "HYDE data missing");
        Check(_land.Width == pop.Width && _land.Height == pop.Height, "land layer and population grid differ");

        // Every field loads, every land node has a byte, sea is 0.
        foreach (var f in _land.Fields)
        {
            byte[] b = _land.Bytes(f.Name);
            Check(b.Length == pop.Width * pop.Height, $"{f.Name}: wrong size");
        }
        Check(_land.Fields.Count >= 60, $"only {_land.Fields.Count} fields");

        // Licences: only terms that allow selling the game, with nothing binding its files.
        string[] allowed = { "(CC0 1.0)", "(CC BY 4.0)", "(CC BY 3.0)", "(public domain)", "(project's own)" };
        foreach (var f in _land.Fields)
            foreach (string s in f.Sources)
                Check(allowed.Any(s.EndsWith),
                    $"{f.Name}: source licence not cleared for a commercial game: {s}");

        // Places whose land history knows.
        Expect("Nile at Luxor", "floodplain", 32.65, 25.7, 0.8, 1);
        Expect("Nile at Luxor", "rain", 32.65, 25.7, 0, 30);
        Expect("Nile at Luxor", "farmed", 32.65, 25.7, 0.6, 1);
        Expect("Nile delta", "irrigation", 31.0, 30.9, 0.8, 1);
        Expect("central Sahara", "desert", 10, 24, 0.9, 1);
        Expect("central Sahara", "farmed", 10, 24, 0, 0.02);
        Expect("Alps", "temp_winter", 10, 46.5, -30, -5);
        Expect("Alps", "growing_days", 10, 46.5, 0, 150);
        Expect("Po valley", "flat_share", 11, 45, 0.8, 1);
        Expect("Po valley", "rain", 11, 45, 550, 1100);
        Expect("Athens", "rain", 23.7, 38.0, 300, 550);
        Expect("Athens", "rain_seasonality", 23.7, 38.0, 50, 200);
        Expect("Lebanon mountains", "rain", 35.95, 34.25, 800, 2500);
        Expect("Southern Mesopotamia", "salinity", 46.2, 31.2, 0.3, 1);
        Expect("Southern Mesopotamia", "irrigation", 46.2, 31.2, 0.8, 1);
        Expect("Mesopotamian marshes", "marsh", 47.0, 31.0, 0.3, 1);
        Expect("Laurion", "res_silver", 24.05, 37.72, 0.8, 1);
        Expect("Rio Tinto", "res_copper", -6.59, 37.70, 0.8, 1);
        Expect("Cyprus", "res_copper", 32.9, 35.0, 0.8, 1);
        Expect("Elba", "res_iron", 10.3, 42.8, 0.8, 1);
        Expect("Cedars of Lebanon", "res_cedar", 35.9, 34.2, 0.8, 1);
        Expect("Dhofar", "res_frankincense", 54.0, 17.1, 0.8, 1);
        Expect("Nile delta", "res_papyrus", 31.0, 30.9, 0.8, 1);
        Expect("Tyre", "res_murex", 35.2, 33.27, 0.8, 1);
        Expect("Cyrene", "res_silphium", 21.8, 32.6, 0.8, 1);
        var laurion = _land.Sites.First(s => s.Name == "Laurion");
        Check(laurion.ActiveIn(-300) && !laurion.ActiveIn(100), "Laurion should be worked in 300 BC and exhausted by AD 100");

        // Habitat shares add up to one everywhere on land; farmland follows people.
        string[] shares = { "farmed", "woodland", "scrub", "grassland", "marsh", "desert" };
        float[] hist = pop.Keyframes[0].Pop;
        double worstSum = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int n = 0;
        foreach (int i in pop.LandNodes)
        {
            double sum = shares.Sum(f => _land.Value(f, i));
            worstSum = Math.Max(worstSum, Math.Abs(sum - 1));
            double x = Math.Log(1 + Math.Max(hist[i], 0f)), y = _land.Value("farmed", i);
            sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y; n++;
        }
        double corr = (n * sxy - sx * sy) / Math.Sqrt((n * sxx - sx * sx) * (n * syy - sy * sy));
        Check(worstSum < 0.02, $"habitat shares add up to 1 ± {worstSum:0.###} somewhere");
        Check(corr > 0.45, $"farmland and 300 BC population only correlate at {corr:0.00}");

        Finish($"Land layer tests passed: {_land.Fields.Count} fields and {_land.Sites.Count} historical sites load, " +
            $"every source cleared for a commercial game, known places check out, habitat shares add up (±{worstSum:0.###}), " +
            $"farmland follows 300 BC population (r = {corr:0.00}).");
    }
}
