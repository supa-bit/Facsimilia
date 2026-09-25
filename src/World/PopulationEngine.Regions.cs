using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Godot;

namespace Facsimilia.World;

/// <summary>One of the ancient geographers' regions (see MECHANICS.md, "Geographic regions").</summary>
public sealed record RegionInfo(int Id, string Name, string Continent, string Includes,
    double LabelLon, double LabelLat, int[] Neighbours);

/// <summary>
/// The region layer: which of the 38 ancient geographic regions each node
/// belongs to, baked by tools/build_region_mask.py onto the same node grid.
/// Regions are fixed geography, not politics. Natural increase is computed
/// per region, and migration between regions runs only between neighbours
/// (a shared border or a sea lane).
///
/// Without the baked data (the toy-world tests), every land node is in one
/// region, which makes the engine behave as a single map-wide pool.
/// </summary>
public partial class PopulationEngine
{
    public const string RegionMetaPath = "res://data/regions/regions.json";
    const string RegionDir = "res://data/regions/";

    /// <summary>Region id per node, 1..RegionCount; 0 = sea.</summary>
    public byte[] NodeRegion { get; private set; } = Array.Empty<byte>();
    public IReadOnlyList<RegionInfo> Regions => _regions;
    public int RegionCount => _regions.Count;

    readonly List<RegionInfo> _regions = new();
    int[][] _regionNodes = Array.Empty<int[]>();   // land nodes per region, index = region id (0 unused)
    int[][] _neighbours = Array.Empty<int[]>();

    public static bool RegionsAvailable() => Godot.FileAccess.FileExists(RegionMetaPath);

    /// <summary>Loads the baked region mask. Returns false (keeping one map-wide region) if it's missing or doesn't fit.</summary>
    public bool LoadRegions()
    {
        if (!RegionsAvailable())
            return false;
        RegionMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<RegionMeta>(Godot.FileAccess.GetFileAsString(RegionMetaPath));
        }
        catch (JsonException)
        {
            meta = null;
        }
        if (meta?.regions == null || meta.width != Width || meta.height != Height)
        {
            GD.PushError($"PopulationEngine: {RegionMetaPath} is invalid or doesn't match the node grid");
            return false;
        }
        byte[] gz = Godot.FileAccess.GetFileAsBytes(RegionDir + meta.file);
        using var input = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var infos = new List<RegionInfo>();
        foreach (var r in meta.regions)
            infos.Add(new RegionInfo(r.id, r.name, r.continent, r.includes, r.label_lon, r.label_lat, r.neighbours));
        return SetRegions(bytes.ToArray(), infos);
    }

    sealed record RegionMeta(int width, int height, string file, List<RegionEntry> regions);
    sealed record RegionEntry(int id, string name, string continent, string includes,
        double label_lon, double label_lat, int[] neighbours);

    /// <summary>
    /// Region id per node (0 = sea) and each region's description; ids must
    /// run 1..regions.Count in order. Every land node needs a region.
    /// </summary>
    public bool SetRegions(byte[] nodeRegion, IReadOnlyList<RegionInfo> regions)
    {
        bool valid = nodeRegion.Length == Width * Height;
        for (int k = 0; valid && k < regions.Count; k++)
            valid = regions[k].Id == k + 1;
        foreach (int i in LandNodes)
        {
            if (!valid)
                break;
            valid = nodeRegion[i] >= 1 && nodeRegion[i] <= regions.Count;
        }
        if (!valid)
        {
            GD.PushError("PopulationEngine: region mask doesn't cover every land node; keeping one region");
            return false;
        }
        _regions.Clear();
        _regions.AddRange(regions);
        NodeRegion = nodeRegion;
        BuildRegionIndex();
        return true;
    }

    /// <summary>One region holding every land node: a single map-wide pool.</summary>
    void DefaultRegions()
    {
        _regions.Clear();
        _regions.Add(new RegionInfo(1, "World", "", "", 0.0, 0.0, Array.Empty<int>()));
        NodeRegion = new byte[Width * Height];
        foreach (int i in LandNodes)
            NodeRegion[i] = 1;
        BuildRegionIndex();
    }

    void BuildRegionIndex()
    {
        int count = _regions.Count;
        var lists = new List<int>[count + 1];
        for (int r = 0; r <= count; r++)
            lists[r] = new List<int>();
        foreach (int i in LandNodes)
            lists[NodeRegion[i]].Add(i);
        _regionNodes = new int[count + 1][];
        _neighbours = new int[count + 1][];
        _neighbours[0] = Array.Empty<int>();
        for (int r = 0; r <= count; r++)
            _regionNodes[r] = lists[r].ToArray();
        foreach (var info in _regions)
            _neighbours[info.Id] = Array.FindAll(info.Neighbours, n => n >= 1 && n <= count && n != info.Id);
        AllocateRegionState(count);
    }

    public int RegionOf(int node) => NodeRegion[node];
    public IReadOnlyList<int> RegionNodes(int region) => _regionNodes[region];

    public double RegionPopulation(int region)
    {
        double s = 0.0;
        foreach (int i in _regionNodes[region])
            s += Pop[i];
        return s;
    }

    /// <summary>What HYDE says the region held in the current year.</summary>
    public double RegionHistoricalPopulation(int region) => _histRegion[region];
}
