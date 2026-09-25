using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// Population engine tests on a deterministic 100x50 toy world: rural
/// background ~1,000 people per node plus 300 cities on an ancient-style
/// rank-size curve (300,000 * rank^-0.6), all one region (no region mask),
/// so it's a single map-wide pool. The climb timings pin the engine to
/// tools/calibrate_population.py's numpy model of the same rules on its toy
/// world (--check-only): 57 years to the top 50 for an ordinary city, 6 for
/// a country capital, 27 for a province capital, and none reaches the top
/// 10. The constants are fitted on the real HYDE grid instead, where that
/// fit is checked: PopulationHydeTest.
/// </summary>
public partial class PopulationEngineTest : TestRunner
{
    const int W = 100, H = 50, N = W * H;
    const int Player = 1, Bot = 2, Start = -300;

    static double Frac(double x) => x - Math.Floor(x);
    static double U(int i) => Frac(Math.Sin(i * 12.9898) * 43758.5453);

    static float[] Fixture()
    {
        var pop = new float[N];
        for (int i = 0; i < N; i++)
        {
            double g = (U(3 * i + 1) + U(3 * i + 2) + U(3 * i + 3) - 1.5) * 2.0;
            pop[i] = (float)(1000.0 * Math.Exp(0.6 * g));
        }
        for (int r = 1; r <= 300; r++)
            pop[(r * 997 + 13) % N] = (float)(300000.0 * Math.Pow(r, -0.6));
        return pop;
    }

    /// <summary>Median-heat node: an ordinary rural spot, the "found a city here" site.</summary>
    static int Barren(float[] pop) =>
        Enumerable.Range(0, N).OrderBy(i => pop[i]).ThenBy(i => i).ElementAt(N / 2);

    static PopulationEngine Engine(int[] years, float[][] pops, int[] owners)
    {
        var e = new PopulationEngine();
        e.SetKeyframes(W, H, years, pops);
        e.SetOwnership(owners, Player);
        e.Start(years[0]);
        return e;
    }

    static PopulationEngine Engine(float[] hist, int[] owners) => Engine(new[] { Start }, new[] { hist }, owners);

    static int[] Owners(IEnumerable<int> playerNodes)
    {
        var owners = new int[N];
        Array.Fill(owners, Bot);
        foreach (int i in playerNodes)
            owners[i] = Player;
        return owners;
    }

    /// <summary>Years until node reaches world top 50 and top 10 (0 if never within `years`).</summary>
    static (int Top50, int Top10) Climb(PopulationEngine e, int node, int years)
    {
        int r50 = 0;
        for (int t = 1; t <= years; t++)
        {
            e.Tick(Start + t);
            int rank = e.RankOf(node);
            if (r50 == 0 && rank <= 50)
                r50 = t;
            if (rank <= 10)
                return (r50, t);
        }
        return (r50, 0);
    }

    public override void _Initialize()
    {
        float[] hist = Fixture();
        int barren = Barren(hist);

        // 1. The historical start is a fixed point: nothing drifts, the world
        //    total holds, and nobody exceeds the ceiling.
        var e = Engine(hist, Owners(Array.Empty<int>()));
        double total0 = e.TotalPopulation();
        for (int t = 1; t <= 50; t++)
            e.Tick(Start + t);
        double worst = 0.0;
        for (int i = 0; i < N; i++)
            worst = Math.Max(worst, Math.Abs(e.Pop[i] - hist[i]) / hist[i]);
        Check(worst < 1e-3, $"historical start drifted by {worst}");
        Check(Math.Abs(e.TotalPopulation() - total0) / total0 < 1e-4, "world total drifted");

        // 2. Climb: an ordinary player city with best-in-world drivers.
        e = Engine(hist, Owners(new[] { barren }));
        e.DriverMods[barren] = PopulationEngine.FullAttraction;
        var ordinary = Climb(e, barren, 400);
        Check(Near(ordinary.Top50, 57) && ordinary.Top10 == 0, $"ordinary city climb {ordinary}, expected (57, never)");

        // 3. Climb: country and province capitals, settlers drawn from a
        //    small player realm (nodes 0-399, 19 cities).
        var realm = Enumerable.Range(0, 400).Append(barren).ToArray();
        var caps = new Dictionary<CapitalKind, (int Top50, int Top10)>();
        foreach (var kind in new[] { CapitalKind.Country, CapitalKind.Province })
        {
            e = Engine(hist, Owners(realm));
            e.DriverMods[barren] = PopulationEngine.FullAttraction;
            double before = e.TotalPopulation();
            e.DesignateCapital(barren, kind, Player);
            Check(Math.Abs(e.TotalPopulation() - before) < 1.0, "resettlement must move people, not create them");
            caps[kind] = Climb(e, barren, 400);
        }
        var country = caps[CapitalKind.Country];
        var province = caps[CapitalKind.Province];
        Check(Near(country.Top50, 6) && country.Top10 == 0, $"country capital climb {country}, expected (6, never)");
        Check(Near(province.Top50, 27) && province.Top10 == 0, $"province capital climb {province}, expected (27, never)");

        // 4. Resettlement happens only the first time: re-designating after
        //    losing capital status moves nobody.
        e = Engine(hist, Owners(realm));
        e.DesignateCapital(barren, CapitalKind.Country, Player);
        float afterFirst = e.Pop[barren];
        Check(afterFirst > hist[barren] * 5.0, "first designation didn't resettle");
        e.ClearCapital(barren);
        e.DesignateCapital(barren, CapitalKind.Country, Player);
        Check(e.Pop[barren] == afterFirst, "second designation must not resettle again");

        // 5. Bots can't exceed history, however hard they push.
        e = Engine(hist, Owners(Array.Empty<int>()));
        e.DriverMods[barren] = 5f;
        e.DesignateCapital(barren, CapitalKind.Country, Bot);
        for (int t = 1; t <= 200; t++)
        {
            e.Tick(Start + t);
            if (!Check(e.Pop[barren] <= e.HistPop[barren] + 0.01, "bot node exceeded its historical ceiling"))
                break;
        }

        // 6. When history declines, bot-held places decline with it: the
        //    largest city falls to a tenth of its size over a century.
        int big = (1 * 997 + 13) % N;
        var later = (float[])hist.Clone();
        later[big] = (float)(hist[big] * 0.1);
        e = Engine(new[] { Start, Start + 100 }, new[] { hist, later }, Owners(Array.Empty<int>()));
        for (int t = 1; t <= 100; t++)
        {
            e.Tick(Start + t);
            if (!Check(e.Pop[big] <= e.HistPop[big] + 0.01, "bot city lagged above a declining history"))
                break;
        }
        Check(e.Pop[big] <= hist[big] * 0.1 + 1.0, "bot city didn't follow history down");

        // 7. The player can exceed history; after the player loses the city,
        //    it fades back gradually (no snap), about 60 years per halving of
        //    the log excess.
        e = Engine(hist, Owners(new[] { barren }));
        e.DriverMods[barren] = PopulationEngine.FullAttraction;
        for (int t = 1; t <= 300; t++)
            e.Tick(Start + t);
        float peak = e.Pop[barren];
        Check(peak > hist[barren] * 5.0, "player city failed to exceed history");
        e.SetOwnership(Owners(Array.Empty<int>()), Player);
        e.Tick(Start + 301);
        // A far-above-history city sheds a few percent in its first year (a
        // percentage rate); a snap would drop it straight to history.
        Check(e.Pop[barren] >= peak * 0.9,
            $"lost player city snapped down instead of fading: {e.Pop[barren] / peak} of peak");
        for (int t = 302; t < 362; t++)
            e.Tick(Start + t);
        double logExcess60 = Math.Log(e.Pop[barren] / hist[barren]);
        double logExcess0 = Math.Log(peak / hist[barren]);
        Check(logExcess60 < logExcess0 * 0.6 && logExcess60 > logExcess0 * 0.4,
            $"legacy fade: log excess {logExcess0} -> {logExcess60} after 60 years");

        // 8. A razed node stays empty for a generation, then resettles at its
        //    historical population.
        e = Engine(hist, Owners(Array.Empty<int>()));
        e.Pop[barren] = 0f;
        e.EmptySince[barren] = Start;
        for (int t = 1; t < 25; t++)
        {
            e.Tick(Start + t);
            if (!Check(e.Pop[barren] == 0f, "razed node resettled early"))
                break;
        }
        e.Tick(Start + 25);
        Check(e.Pop[barren] >= 1f, "razed node never resettled");
        Check(e.Pop[barren] <= hist[barren] + 0.01, "resettled above history");

        // 9. Save/load round-trip continues identically.
        e = Engine(hist, Owners(realm));
        e.DriverMods[barren] = PopulationEngine.FullAttraction;
        e.DesignateCapital(barren, CapitalKind.Province, Player);
        for (int t = 1; t <= 10; t++)
            e.Tick(Start + t);
        byte[] saved = GD.VarToBytes(e.ToDict());
        var e2 = new PopulationEngine();
        e2.SetKeyframes(W, H, new[] { Start }, new[] { hist });
        e2.SetOwnership(Owners(realm), Player);
        Check(e2.LoadFromDict(GD.BytesToVar(saved).AsGodotDictionary()), "load_from_dict rejected a fresh save");
        e.Tick(Start + 11);
        e2.Tick(Start + 11);
        for (int i = 0; i < N; i++)
        {
            if (!Check(e.Pop[i] == e2.Pop[i], $"save/load diverged at node {i}"))
                break;
        }

        Finish($"PopulationEngine tests passed: fixed-point start, ordinary {ordinary}, country capital {country}, " +
            $"province capital {province} (years to top 50 / top 10), ceilings, history decline, legacy fade, " +
            "resettlement, save/load.");
    }
}
