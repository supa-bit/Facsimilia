using System;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Population engine calibration on the real map: the baked HYDE 300 BC
/// keyframe (data/population/), history held there, as in
/// `python3 tools/calibrate_population.py --hyde`. The test site is the
/// median populated land node, given best-in-world drivers. Expected years
/// to the map's top 50 / top 10 are that script's numbers at the engine's
/// constants (102/168 ordinary, 33/78 country capital, 62/128 province
/// capital), against the historical 100/166, 24/78 and 62/122.
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

        // The median populated land node, as the calibration script picks it.
        int[] populated = loaded.LandNodes.Where(i => hist[i] >= 1f).OrderBy(i => hist[i]).ThenBy(i => i).ToArray();
        int site = populated[populated.Length / 2];
        double maxHist = loaded.LandNodes.Max(i => (double)hist[i]);
        double hSite = Math.Sqrt(hist[site] / maxHist);

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
            var owners = new int[loaded.Width * loaded.Height];
            Array.Fill(owners, Bot);
            foreach (int i in run == 0 ? new[] { site } : realm)
                owners[i] = Player;
            e.SetOwnership(owners, Player);
            e.Start(Start);
            e.DriverMods[site] = (float)(1.0 - hSite);
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

        Check(Near(ordinary.Top50, 102, 3) && Near(ordinary.Top10, 168, 3),
            $"ordinary city climb {ordinary}, expected (102, 168)");
        Check(Near(country.Top50, 33, 3) && Near(country.Top10, 78, 3),
            $"country capital climb {country}, expected (33, 78)");
        Check(Near(province.Top50, 62, 3) && Near(province.Top10, 128, 3),
            $"province capital climb {province}, expected (62, 128)");

        Finish("HYDE calibration test passed (years to top 50 / top 10 on the real 300 BC grid): " +
            $"ordinary ({ordinary.Top50}, {ordinary.Top10}), country capital ({country.Top50}, {country.Top10}), " +
            $"province capital ({province.Top50}, {province.Top10}). Loaded {loaded.Keyframes.Count} keyframes " +
            $"in {loadMs} ms; {(double)tickMs / ticks:F1} ms per tick (with a rank query) on " +
            $"{loaded.LandNodes.Length:N0} land nodes.");
    }
}
