using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Plagues, famines and disasters (decision "Playable 28"): history's own
/// strike at their dates and places; chance adds earthquakes along the
/// quake belts and local epidemics anywhere. Plague deaths fall with the
/// owner's medicine. The dead are taken from the people on the map, who
/// recover over the following decades.
/// </summary>
public partial class MapView
{
    (double Lon, double Lat) NodeLonLat(int node) =>
        (LonMin + (node % Population!.Width + 0.5) / Population.Width * (LonMax - LonMin),
         LatMax - (node / Population.Width + 0.5) / Population.Height * (LatMax - LatMin));

    internal List<ChronicleEvent> DisastersYear()
    {
        var events = new List<ChronicleEvent>();
        if (Population == null)
            return events;
        var cat = DisasterCatalog.Instance;
        var strikes = cat.Events.Where(e => e.Year == DemoYear).ToList();

        // Chance: one draw per region, fixed by the year.
        var nodesOf = new Dictionary<int, List<int>>();
        foreach (int i in Population.LandNodes)
            if (Population.Pop[i] > 0)
            {
                int r = Population.RegionOf(i);
                if (!nodesOf.TryGetValue(r, out var list))
                    nodesOf[r] = list = new List<int>();
                list.Add(i);
            }
        foreach (var info in Population.Regions)
        {
            if (!nodesOf.TryGetValue(info.Id, out var nodes) || nodes.Count == 0)
                continue;
            var rng = new Random(StableHash.Of(DemoYear, info.Id, 4051));
            int at = nodes[rng.Next(nodes.Count)];
            var (lon, lat) = NodeLonLat(at);
            string place = PlaceCatalog.Instance.Nearest(lon, lat, DemoYear) ?? ProvinceAtNode(at)?.Name ?? info.Name;
            if (rng.NextDouble() < cat.QuakeOdds(lon, lat))
                strikes.Add(new Disaster(DemoYear, "earthquake", $"An earthquake strikes {place}", lon, lat,
                    60 + rng.NextDouble() * 120, 0.01 + rng.NextDouble() * rng.NextDouble() * 0.1));
            double crowd = Math.Min(3, Population.RegionPopulation(info.Id) / 1_000_000.0);
            if (rng.NextDouble() < DisasterCatalog.EpidemicOdds * (0.5 + crowd))
                strikes.Add(new Disaster(DemoYear, "plague", $"An epidemic breaks out in {place}", lon, lat,
                    150 + rng.NextDouble() * 250, 0.04 + rng.NextDouble() * 0.1));
        }
        if (strikes.Count == 0)
            return events;

        var medicine = Game.Realms.ToDictionary(kv => kv.Key, kv =>
            DisasterCatalog.MedicineCut(TechCatalog.Instance.Effect(kv.Value, "growth")));
        foreach (var d in strikes)
        {
            var dead = new Dictionary<int, double>();
            foreach (int i in Population.LandNodes)
            {
                float pop = Population.Pop[i];
                if (pop <= 0)
                    continue;
                var (lon, lat) = NodeLonLat(i);
                if (Math.Abs(lat - d.Lat) * 111.2 > d.Km)
                    continue;
                double share = DisasterCatalog.DeathsAt(d, DisasterCatalog.Km(d.Lon, d.Lat, lon, lat));
                if (share <= 0)
                    continue;
                int owner = Population.NodeOwner[i];
                if (d.Kind == "plague" && medicine.TryGetValue(owner, out double cut))
                    share *= 1 - cut;
                double k = pop * share;
                Population.Pop[i] = (float)(pop - k);
                dead[owner] = dead.GetValueOrDefault(owner) + k;
            }
            double total = dead.Values.Sum();
            if (total < 100)
                continue;
            double mine = dead.GetValueOrDefault(PlayerRealmId);
            bool historic = cat.Events.Contains(d);
            if (mine >= 100)
                events.Add(new ChronicleEvent(ChronicleKind.Disaster, PlayerRealmId,
                    $"{d.Name}: {mine:N0} of our people die."));
            else if (historic)
                events.Add(new ChronicleEvent(ChronicleKind.Disaster, 0, $"{d.Name}: {total:N0} die."));
        }
        InvalidateProvincePeople();
        return events;
    }

    /// <summary>The province at a population node's centre.</summary>
    Province? ProvinceAtNode(int node)
    {
        if (Provinces == null || Population == null)
            return null;
        int cx = (int)((node % Population.Width + 0.5) * GridWidth / Population.Width);
        int cy = (int)((node / Population.Width + 0.5) * GridHeight / Population.Height);
        return Provinces.ProvinceAt(cx, cy);
    }
}
