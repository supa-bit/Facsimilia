using System;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Population engine calibration on the real map: the baked HYDE 300 BC
/// keyframe (data/population/), history held there, with the 38 geographic
/// regions (data/regions/), so a new city draws on its own region. The test
/// site is the median populated land node, given best-in-world drivers.
/// Beta and Mu were refitted here on the regional engine (a grid search
/// around the single-pool fit of tools/calibrate_population.py --hyde).
/// Expected years to the map's top 50 / top 10: 98/165 ordinary, 32/76
/// country capital, 60/126 province capital, against the historical
/// 100/166, 24/78 and 62/122.
/// Also reports the tick time on the full 284,626-node grid.
/// </summary>
public partial class PopulationHydeTest : TestRunner
{
    const int Player = 1, Bot = 2, Start = -300;
    const int Donors = 2000;   // player-held land nodes the capital's settlers come from

    /// <summary>Years until node reaches the top 50 and top 10 (0 if never within `years`).</summary>
    static (int Top50, int Top10, int Ticks) Climb(PopulationEngine e, int node, int years)
    {
        int r50 = 0;
        for (int t = 1; t <= years; t++)
        {
            e.Tick(Start + t);
            int rank = e.RankOf(node);
            if (r50 == 0 && rank <= 50)
                r50 = t;
            if (rank <= 10)
                return (r50, t, t);
        }
        return (r50, 0, years);
    }

    public override void _Initialize()
    {
        if (!PopulationEngine.HydeAvailable())
        {
            Check(false, "no HYDE keyframes in data/population/");
            Finish("");
            return;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var loaded = new PopulationEngine();
        loaded.LoadHyde();
        long loadMs = clock.ElapsedMilliseconds;
        var (firstYear, hist) = loaded.Keyframes[0];
        Check(firstYear == Start, $"first keyframe is {firstYear}, not 300 BC");
        Check(loaded.RegionCount == 38, $"expected the 38 ancient regions, got {loaded.RegionCount}");

        // The median populated land node, as the calibration script picks it.
        int[] populated = loaded.LandNodes.Where(i => hist[i] >= 1f).OrderBy(i => hist[i]).ThenBy(i => i).ToArray();
        int site = populated[populated.Length / 2];

        int k = Array.IndexOf(loaded.LandNodes, site);
        int[] realm = Enumerable.Range(0, Donors + 1)
            .Select(j => loaded.LandNodes[(k + j) % loaded.LandNodes.Length]).ToArray();

        var results = new (int Top50, int Top10, int Ticks)[3];
        long tickMs = 0;
        int ticks = 0;
        string[] kinds = { "ordinary", "country", "province" };
        for (int run = 0; run < 3; run++)
        {
            var e = new PopulationEngine();
            e.SetKeyframes(loaded.Width, loaded.Height, new[] { Start }, new[] { hist });
            e.SetRegions(loaded.NodeRegion, loaded.Regions);
            var owners = new int[loaded.Width * loaded.Height];
            Array.Fill(owners, Bot);
            foreach (int i in run == 0 ? new[] { site } : realm)
                owners[i] = Player;
            e.SetOwnership(owners, Player);
            e.Start(Start);
            e.DriverMods[site] = PopulationEngine.FullAttraction;
            if (run == 1)
                e.DesignateCapital(site, CapitalKind.Country, Player);
            else if (run == 2)
                e.DesignateCapital(site, CapitalKind.Province, Player);
            clock.Restart();
            results[run] = Climb(e, site, 250);
            tickMs += clock.ElapsedMilliseconds;
            ticks += results[run].Ticks;
        }
        var (ordinary, country, province) = (results[0], results[1], results[2]);

        Check(Near(ordinary.Top50, 98, 3) && Near(ordinary.Top10, 165, 3),
            $"ordinary city climb {ordinary}, expected (98, 165)");
        Check(Near(country.Top50, 32, 3) && Near(country.Top10, 76, 3),
            $"country capital climb {country}, expected (32, 76)");
        Check(Near(province.Top50, 60, 3) && Near(province.Top10, 126, 3),
            $"province capital climb {province}, expected (60, 126)");

        Finish("HYDE calibration test passed (years to top 50 / top 10 on the real 300 BC grid): " +
            $"ordinary ({ordinary.Top50}, {ordinary.Top10}), country capital ({country.Top50}, {country.Top10}), " +
            $"province capital ({province.Top50}, {province.Top10}). Loaded {loaded.Keyframes.Count} keyframes " +
            $"in {loadMs} ms; {(double)tickMs / ticks:F1} ms per tick (with a rank query) on " +
            $"{loaded.LandNodes.Length:N0} land nodes.");
    }
}
