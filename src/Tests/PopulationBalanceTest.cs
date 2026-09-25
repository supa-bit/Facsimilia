using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The growth drivers' balance tests: the contract in MECHANICS.md,
/// "Balance tests: the contract". Every test runs on the real HYDE grid with
/// the 38 geographic regions, and all must pass whenever a constant, weight
/// or factor formula changes. The seventh, founded cities, is
/// PopulationHydeTest.
///
/// The "Greece-sized realm" is the region Hellas, all held by the player.
/// Except where history itself is the point (Do nothing), history is held
/// at 300 BC, so "history" is the starting population.
/// </summary>
public partial class PopulationBalanceTest : TestRunner
{
    const int Player = 1, Bot = 2, Start = -300;

    PopulationEngine _loaded = null!;
    double[]? _foodCapacity;
    int _hellas, _italia;
    readonly List<string> _report = new();

    /// <summary>An engine on the real grid, history held at 300 BC unless allKeyframes.</summary>
    PopulationEngine Engine(IEnumerable<int> playerRegions, bool allKeyframes = false)
    {
        var e = new PopulationEngine();
        if (allKeyframes)
            e.SetKeyframes(_loaded.Width, _loaded.Height, _loaded.Keyframes.Select(k => k.Year).ToArray(),
                _loaded.Keyframes.Select(k => k.Pop).ToArray());
        else
            e.SetKeyframes(_loaded.Width, _loaded.Height, new[] { Start }, new[] { _loaded.Keyframes[0].Pop });
        e.SetRegions(_loaded.NodeRegion, _loaded.Regions);
        e.SetFoodCapacity(_foodCapacity);
        var owners = new int[e.Width * e.Height];
        var mine = new HashSet<int>(playerRegions);
        foreach (int i in e.LandNodes)
            owners[i] = mine.Contains(e.RegionOf(i)) ? Player : Bot;
        e.SetOwnership(owners, Player);
        e.Start(Start);
        return e;
    }

    static double[] RegionTotals(PopulationEngine e) =>
        Enumerable.Range(0, e.RegionCount + 1).Select(r => r == 0 ? 0.0 : e.RegionPopulation(r)).ToArray();

    int RegionId(string name) => _loaded.Regions.First(r => r.Name == name).Id;
    string Name(int r) => _loaded.Regions[r - 1].Name;

    protected override Task Run()
    {
        if (!PopulationEngine.HydeAvailable() || !PopulationEngine.RegionsAvailable())
        {
            Check(false, "needs data/population/ and data/regions/");
            Finish("");
            return Task.CompletedTask;
        }
        _loaded = new PopulationEngine();
        _loaded.LoadHyde();
        // Carrying capacity from the crop model on the land layer, as in the game.
        var land = LandLayer.Load();
        var crops = land != null ? CropModel.Build(land, _loaded.LandNodes) : null;
        Check(crops != null, "needs data/land/ and data/crops.json for the food capacity");
        _foodCapacity = crops?.RegionCapacity(_loaded);
        Check(_loaded.RegionCount == 38, $"expected 38 regions, got {_loaded.RegionCount}");
        _hellas = RegionId("Hellas");
        _italia = RegionId("Italia");

        DoNothing();
        MaxedAttraction();
        MaxedGrowth();
        CapacityBinds();
        Stacking();
        Catastrophe();
        Conservation();

        Finish("Population balance tests passed on the real grid with 38 regions:\n  " + string.Join("\n  ", _report));
        return Task.CompletedTask;
    }

    /// <summary>A player realm with every factor at its historical baseline tracks HYDE for 300 years.</summary>
    void DoNothing()
    {
        var e = Engine(new[] { _italia }, allKeyframes: true);
        double worst = 0.0;
        string worstAt = "";
        for (int t = 1; t <= 300; t++)
        {
            e.Tick(Start + t);
            if (t % 50 != 0)
                continue;
            for (int r = 1; r <= e.RegionCount; r++)
            {
                double dev = Math.Abs(e.RegionPopulation(r) / e.RegionHistoricalPopulation(r) - 1.0);
                if (dev > worst)
                {
                    worst = dev;
                    worstAt = $"{Name(r)} in year {Start + t}";
                }
            }
        }
        double hist = e.LandNodes.Sum(i => (double)e.HistPop[i]);
        double mapDev = Math.Abs(e.TotalPopulation() / hist - 1.0);
        Check(worst <= 0.05, $"do nothing: {worstAt} is {worst:P2} off HYDE (limit 5%)");
        Check(mapDev <= 0.05, $"do nothing: map total {mapDev:P2} off HYDE");
        _report.Add($"Do nothing: 300 years, every region within {worst:P3} of HYDE (worst {worstAt}), map total within {mapDev:P3}.");
    }

    /// <summary>
    /// Every pull factor maxed in a Greece-sized realm for 200 years:
    /// migration alone (natural increase off) takes the realm to at most
    /// 1.5x, and no other region loses more than 10%.
    /// </summary>
    void MaxedAttraction()
    {
        var e = Engine(new[] { _hellas });
        e.NaturalIncreaseEnabled = false;
        foreach (int i in e.RegionNodes(_hellas))
            e.DriverMods[i] = PopulationEngine.FullAttraction;
        double[] before = RegionTotals(e);
        for (int t = 1; t <= 200; t++)
            e.Tick(Start + t);
        double[] after = RegionTotals(e);
        double gain = after[_hellas] / before[_hellas];
        int worstLoser = Enumerable.Range(1, e.RegionCount).Where(r => r != _hellas)
            .OrderBy(r => after[r] / before[r]).First();
        double worstLoss = 1.0 - after[worstLoser] / before[worstLoser];
        Check(gain <= 1.5, $"maxed attraction: realm grew {gain:F3}x by migration (limit 1.5x)");
        Check(gain > 1.05, $"maxed attraction: realm only grew {gain:F3}x - migration isn't pulling");
        Check(worstLoss <= 0.10, $"maxed attraction: {Name(worstLoser)} lost {worstLoss:P1} (limit 10%)");
        _report.Add($"Maxed attraction: Hellas {gain:F2}x after 200 years; the hardest-hit region, {Name(worstLoser)}, lost {worstLoss:P1}.");
    }

    /// <summary>
    /// Every natural-increase factor maxed for 200 years: the player's
    /// realm stays within 2.7x history and its carrying capacity; a bot
    /// region with the same factors can't rise above history at all.
    /// </summary>
    void MaxedGrowth()
    {
        var e = Engine(new[] { _hellas });
        for (int r = 1; r <= e.RegionCount; r++)
            for (int f = 0; f < PopulationEngine.FactorCount; f++)
                e.SetFactor(r, (GrowthFactor)f, 1.0);
        double[] before = RegionTotals(e);
        double peakRatio = 0.0, botPeak = 0.0;
        for (int t = 1; t <= 200; t++)
        {
            e.Tick(Start + t);
            peakRatio = Math.Max(peakRatio, e.RegionPopulation(_hellas) / before[_hellas]);
            botPeak = Math.Max(botPeak, e.RegionPopulation(_italia) / before[_italia]);
        }
        Check(peakRatio <= 2.7, $"maxed growth: realm reached {peakRatio:F3}x history (limit 2.7x)");
        double capacityRatio = e.RegionCapacity(_hellas) / before[_hellas];
        Check(peakRatio <= capacityRatio, $"maxed growth: realm passed its carrying capacity ({capacityRatio:F2}x)");
        Check(peakRatio > 1.3, $"maxed growth: realm only reached {peakRatio:F3}x - growth factors do nothing");
        Check(botPeak <= 1.0 + 1e-4, $"maxed growth: a bot region rose to {botPeak:F4}x history");
        _report.Add($"Maxed growth: Hellas {peakRatio:F2}x after 200 years (food capacity {capacityRatio:F1}x); " +
            $"a bot region with the same factors stayed at {botPeak:F4}x.");
    }

    /// <summary>
    /// Food capacity binds: a player-held Syria (its land feeds about 2.6
    /// times its 300 BC people) with every growth factor maxed for 400
    /// years levels off at its capacity and never passes it.
    /// </summary>
    void CapacityBinds()
    {
        int syria = RegionId("Syria");
        var e = Engine(new[] { syria });
        for (int f = 0; f < PopulationEngine.FactorCount; f++)
            e.SetFactor(syria, (GrowthFactor)f, 1.0);
        double cap = e.RegionCapacity(syria);
        double peak = 0;
        for (int t = 1; t <= 400; t++)
        {
            e.Tick(Start + t);
            peak = Math.Max(peak, e.RegionPopulation(syria));
        }
        double end = e.RegionPopulation(syria);
        Check(peak <= cap * 1.001, $"capacity: Syria reached {peak:N0}, over its capacity of {cap:N0}");
        Check(end >= cap * 0.75, $"capacity: Syria only reached {end / cap:P0} of its capacity - growth isn't approaching it");
        _report.Add($"Capacity binds: Syria, every factor maxed for 400 years, reached {end / cap:P0} of the {cap / 1e6:F1} million its land can feed, never more.");
    }

    /// <summary>Doubling a saturated attraction changes drivers by 10% at most.</summary>
    void Stacking()
    {
        double worst = 0.0;
        for (double baseline = 0.0; baseline <= 1.0; baseline += 0.05)
        {
            foreach (double a in new[] { 3.0, 5.0, 10.0 })
            {
                double d1 = PopulationEngine.DriversOf(baseline, a), d2 = PopulationEngine.DriversOf(baseline, 2 * a);
                worst = Math.Max(worst, Math.Abs(d2 / d1 - 1.0));
                d1 = PopulationEngine.DriversOf(baseline, -a);
                d2 = PopulationEngine.DriversOf(baseline, -2 * a);
                if (d1 > 1e-9)
                    worst = Math.Max(worst, Math.Abs(d2 - d1) / Math.Max(baseline, 1e-9));
            }
        }
        Check(worst <= 0.10, $"stacking: doubling a saturated input changed drivers by {worst:P1}");
        _report.Add($"Stacking: doubling a saturated attraction changes drivers by at most {worst:P1}.");
    }

    /// <summary>
    /// The worst war, famine and epidemic together for 10 years: the region
    /// loses at most half, and is back to 90% of its former size within 150
    /// years of peace.
    /// </summary>
    void Catastrophe()
    {
        var e = Engine(Array.Empty<int>());
        double before = e.RegionPopulation(_italia);
        for (int f = 0; f < PopulationEngine.FactorCount; f++)
            e.SetFactor(_italia, (GrowthFactor)f, -1.0);
        double low = before;
        for (int t = 1; t <= 10; t++)
        {
            if (t <= 3)
                e.ApplyShock(_italia, 0.13);   // a Black Death-scale plague over three years, ~34%
            e.Tick(Start + t);
            low = Math.Min(low, e.RegionPopulation(_italia));
        }
        for (int f = 0; f < PopulationEngine.FactorCount; f++)
            e.SetFactor(_italia, (GrowthFactor)f, 0.0);
        int recovered = 0;
        for (int t = 11; t <= 160; t++)
        {
            e.Tick(Start + t);
            if (recovered == 0 && e.RegionPopulation(_italia) >= 0.9 * before)
                recovered = t - 10;
        }
        double loss = 1.0 - low / before;
        Check(loss <= 0.5, $"catastrophe: lost {loss:P1} (limit 50%)");
        Check(recovered > 0, "catastrophe: not back to 90% within 150 years of peace");
        _report.Add($"Catastrophe: Italia lost {loss:P1} at worst and was back to 90% {recovered} years after peace.");
    }

    /// <summary>With natural increase off, migration neither creates nor destroys anyone.</summary>
    void Conservation()
    {
        var e = Engine(new[] { _hellas });
        e.NaturalIncreaseEnabled = false;
        foreach (int i in e.RegionNodes(_hellas))
            e.DriverMods[i] = PopulationEngine.FullAttraction;
        foreach (int i in e.RegionNodes(_italia))
            e.DriverMods[i] = -PopulationEngine.FullAttraction;
        double total0 = e.TotalPopulation();
        double worst = 0.0;
        for (int t = 1; t <= 100; t++)
        {
            e.Tick(Start + t);
            worst = Math.Max(worst, Math.Abs(e.TotalPopulation() - total0));
        }
        // Each node stores its people as a 32-bit float, like HYDE and the
        // saves: summing 285,000 of them can't be exact to one person, so
        // the bound is one person per million.
        double limit = Math.Max(1.0, total0 * 1e-6);
        Check(worst <= limit, $"conservation: map total drifted by {worst:F1} people (limit {limit:F0})");
        _report.Add($"Conservation: migration only, 100 years, map total of {total0:N0} held to within {worst:F1} people.");
    }
}
