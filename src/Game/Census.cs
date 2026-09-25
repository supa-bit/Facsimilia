using System;
using System.Collections.Generic;
using Facsimilia.World;

namespace Facsimilia.Game;

/// <summary>What a realm holds this year, counted from the map.</summary>
public sealed class RealmCensus
{
    public double People { get; set; }
    public double OrganizedPeople { get; set; }   // in the realm's own provinces: taxable
    public double UrbanPeople { get; set; }       // living in large places: craftsmen, traders
    public int Provinces { get; set; }
    public int Nodes { get; set; }
    public bool Coastal { get; set; }
    public HashSet<string> Resources { get; } = new();   // land fields (res_*) the realm holds a good source of
}

/// <summary>
/// Counts every realm's people, provinces and resources from the population
/// engine's nodes: a node belongs to the realm that owns its centre cell,
/// and is organized if it lies in one of that realm's provinces.
/// </summary>
public static class Census
{
    /// <summary>Nodes with more people than this count their excess as townspeople.</summary>
    public const double UrbanThreshold = 10000;
    /// <summary>A land value at least this high counts as a source of the resource.</summary>
    public const double ResourceThreshold = 0.3;

    public static readonly string[] TrackedResources =
        { "res_iron", "res_horses", "res_elephants", "res_ship_timber", "res_copper", "res_tin", "res_silver", "res_gold", "res_salt" };

    public static Dictionary<int, RealmCensus> Take(PopulationEngine pop, ProvinceMap? provinces, int gridWidth,
        int gridHeight, LandLayer? land)
    {
        var result = new Dictionary<int, RealmCensus>();
        float[] people = pop.Pop;
        int[] owners = pop.NodeOwner;
        byte[]? coast = land?.Has("coast_km") == true ? land.Bytes("coast_km") : null;
        var coastField = coast != null ? land!.Field("coast_km") : null;
        var resourceBytes = new List<(string Name, byte[] Bytes, byte Threshold)>();
        if (land != null)
            foreach (string r in TrackedResources)
                if (land.Has(r))
                {
                    var f = land.Field(r);
                    resourceBytes.Add((r, land.Bytes(r), (byte)Math.Ceiling((ResourceThreshold - f.Min) / (f.Max - f.Min) * 255)));
                }

        foreach (int i in pop.LandNodes)
        {
            int owner = owners[i];
            if (owner <= 0)
                continue;
            if (!result.TryGetValue(owner, out var c))
                result[owner] = c = new RealmCensus();
            double p = people[i];
            c.People += p;
            c.Nodes++;
            if (p > UrbanThreshold)
                c.UrbanPeople += p - UrbanThreshold;
            if (provinces != null)
            {
                int nx = i % pop.Width, ny = i / pop.Width;
                int cx = (int)((nx + 0.5) * gridWidth / pop.Width), cy = (int)((ny + 0.5) * gridHeight / pop.Height);
                int prov = provinces.Cells[cy * gridWidth + cx];
                if (prov != ProvinceMap.None && provinces.Provinces.TryGetValue(prov, out var province)
                    && province.RealmId == owner)
                    c.OrganizedPeople += p;
            }
            if (coast != null && coastField!.Min + coast[i] / 255.0 * (coastField.Max - coastField.Min) < 15)
                c.Coastal = true;
            foreach (var (name, bytes, threshold) in resourceBytes)
                if (bytes[i] >= threshold)
                    c.Resources.Add(name);
        }
        if (provinces != null)
            foreach (var p in provinces.Provinces.Values)
                if (result.TryGetValue(p.RealmId, out var c))
                    c.Provinces++;
        return result;
    }
}
