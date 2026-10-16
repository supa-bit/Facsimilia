using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// How fast the game plays (your answer "Under 3 seconds" a turn, and the
/// question of whether the game keeps up as the world grows): plays ten
/// years of the real world and fails if a year takes too long on average,
/// or any single year far too long, or a save gets slow. The times are
/// printed, so a slowdown shows long before it fails.
/// </summary>
public partial class SpeedTest : TestRunner
{
    /// <summary>Your limit for a one-year turn, in milliseconds.</summary>
    public const int YearLimitMs = 3000;
    /// <summary>No single year should take more than twice that (the first, which warms up, is not counted).</summary>
    public const int WorstYearMs = 2 * YearLimitMs;
    /// <summary>Turning the whole game into save data.</summary>
    public const int SaveLimitMs = 1500;
    const int Years = 10;

    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();

        var times = new long[Years];
        var clock = new Stopwatch();
        for (int y = 0; y < Years; y++)
        {
            clock.Restart();
            map.AdvanceYear();
            times[y] = clock.ElapsedMilliseconds;
        }
        var counted = times.Skip(1).ToArray();   // the first year warms the code up
        double average = counted.Average();
        long worst = counted.Max();

        clock.Restart();
        string save = Json.Stringify(map.Game.ToDict());
        long saveMs = clock.ElapsedMilliseconds;

        string report = $"years: {string.Join(", ", times)} ms (average {average:0} ms, worst {worst} ms); " +
                        $"save {saveMs} ms, {save.Length / 1024} KB; {map.Provinces!.Provinces.Count} provinces, {map.Game.Realms.Count} realms";
        GD.Print(report);
        Check(average < YearLimitMs, $"a year takes {average:0} ms on average; the limit is {YearLimitMs} ms ({report})");
        Check(worst < WorstYearMs, $"one year took {worst} ms; none should pass {WorstYearMs} ms ({report})");
        Check(saveMs < SaveLimitMs, $"saving took {saveMs} ms; the limit is {SaveLimitMs} ms ({report})");
        Finish("Speed test passed: " + report);
    }
}
