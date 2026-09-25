using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Harvests, weather and renewable stocks on the map: each region's yearly
/// weather draw, its soil, forests, pasture and fish, and what they do to
/// production, carrying capacity and growth.
/// </summary>
public partial class MapView
{
    double[]? _baseCapacity, _rainVariability, _irrigation;

    public RegionNature NatureOf(int region)
    {
        if (!Game.Nature.TryGetValue(region, out var n))
            Game.Nature[region] = n = new RegionNature();
        return n;
    }

    void PrepareNature()
    {
        if (_baseCapacity != null || Population == null || Crops == null || Land == null)
            return;
        _baseCapacity = Crops.RegionCapacity(Population);
        int count = Population.RegionCount + 1;
        _rainVariability = new double[count];
        _irrigation = new double[count];
        var nodes = new double[count];
        bool rv = Land.Has("rain_variability"), irr = Land.Has("irrigation");
        foreach (int i in Population.LandNodes)
        {
            int r = Population.RegionOf(i);
            nodes[r]++;
            if (rv)
                _rainVariability[r] += Land.Value("rain_variability", i);
            if (irr)
                _irrigation[r] += Land.Value("irrigation", i);
        }
        for (int r = 0; r < count; r++)
            if (nodes[r] > 0)
            {
                _rainVariability[r] /= nodes[r];
                _irrigation[r] /= nodes[r];
            }
    }

    /// <summary>How each kind of work does in each region this year (index = region id), for the goods.</summary>
    Dictionary<string, double[]>? WorkFactors()
    {
        if (Population == null || Game.Nature.Count == 0)
            return null;
        int count = Population.RegionCount + 1;
        var f = new Dictionary<string, double[]>
        {
            ["field"] = new double[count], ["orchard"] = new double[count], ["herd"] = new double[count],
            ["gather"] = new double[count], ["forest"] = new double[count],
        };
        for (int r = 0; r < count; r++)
        {
            var n = NatureOf(r);
            f["field"][r] = Nature.FieldYield(n);
            f["orchard"][r] = Nature.OrchardYield(n);
            f["herd"][r] = Nature.HerdYield(n);
            f["gather"][r] = Nature.FishYield(n);
            f["forest"][r] = Nature.ForestYield(n);
        }
        return f;
    }

    /// <summary>
    /// The year's weather and the land's wear: every region draws its
    /// harvest, its stocks wear under its people or recover, carrying
    /// capacity follows the soil and pasture, and the growth drivers learn
    /// of good and bad years. Famines and bumper harvests in the player's
    /// lands make the chronicle.
    /// </summary>
    internal List<ChronicleEvent> NatureYear()
    {
        var events = new List<ChronicleEvent>();
        PrepareNature();
        if (Population == null || _baseCapacity == null)
            return events;
        // How much of the dung goes back on the fields rather than into the fire.
        double dungKept = 0.5;
        var cat = GoodsCatalog();
        if (cat != null && _realmGoods != null && cat.Has("dung"))
        {
            int dung = cat["dung"].Index;
            double made = _realmGoods.Values.Sum(g => g.Produced[dung]), used = _realmGoods.Values.Sum(g => g.Used[dung]);
            dungKept = made > 0 ? Math.Clamp(1 - used / made, 0, 1) : 0.5;
        }
        var capacity = new double[_baseCapacity.Length];
        var playerRegions = new HashSet<int>();
        foreach (int i in Population.LandNodes)
            if (Population.NodeOwner[i] == PlayerRealmId && Population.Pop[i] > 0)
                playerRegions.Add(Population.RegionOf(i));
        var famines = new List<string>();
        var bumper = new List<string>();
        foreach (var info in Population.Regions)
        {
            int r = info.Id;
            if (r <= 0 || r >= capacity.Length)
                continue;
            var n = NatureOf(r);
            n.Harvest = Nature.Harvest(DemoYear, r, _rainVariability![r], _irrigation![r]);
            double pressure = _baseCapacity[r] > 0 ? Population.RegionPopulation(r) / _baseCapacity[r] : 0;
            Nature.Tick(n, pressure, dungKept);
            capacity[r] = _baseCapacity[r] * Nature.CapacityShare(n);
            Population.SetFactor(r, GrowthFactor.Climate, (n.Harvest - 1) * 3);
            Population.SetFactor(r, GrowthFactor.Food, (Nature.CapacityShare(n) - 1) * 3);
            if (playerRegions.Contains(r))
            {
                if (n.Harvest < Nature.FamineHarvest)
                    famines.Add(info.Name);
                else if (n.Harvest > Nature.BumperHarvest)
                    bumper.Add(info.Name);
            }
        }
        Population.SetFoodCapacity(capacity);
        if (famines.Count > 0)
            events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                $"The harvest fails in {string.Join(", ", famines)}: famine."));
        if (bumper.Count > 0)
            events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                $"A bumper harvest in {string.Join(", ", bumper)}."));
        return events;
    }
}
