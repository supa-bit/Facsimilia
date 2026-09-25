using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>
/// An administrative province: a named piece of one realm's territory.
/// Which cells belong to it lives in ProvinceMap.Cells, never here, so
/// redrawing a boundary never needs splitting or merging logic (MECHANICS.md,
/// Layers). Stats like population are always summed over its cells.
/// </summary>
public sealed class Province
{
    public int Id { get; }
    public string Name { get; set; }
    /// <summary>The owning realm. Authoritative for every member cell (MECHANICS.md: the invariant).</summary>
    public int RealmId { get; set; }
    public int CellCount { get; internal set; }
    /// <summary>Land area; cells are smaller the farther north they are.</summary>
    public double AreaKm2 { get; internal set; }
    /// <summary>Where its name is drawn, in cell coordinates: a point inside the province near its middle.</summary>
    public Vector2 LabelCell { get; internal set; }

    public Province(int id, string name, int realmId)
    {
        Id = id;
        Name = name;
        RealmId = realmId;
    }
}

/// <summary>
/// The province_id layer: one province id per map cell (0 = unorganized or
/// not owned), plus the province records. Cells are ushort, so up to 65,535
/// provinces, at half the memory of an int per cell.
/// </summary>
public sealed class ProvinceMap
{
    public const int None = 0;
    public const int MaxId = ushort.MaxValue;

    public int Width { get; }
    public int Height { get; }
    public ushort[] Cells { get; }
    public Dictionary<int, Province> Provinces { get; } = new();
    int _nextId = 1;

    public ProvinceMap(int width, int height)
    {
        Width = width;
        Height = height;
        Cells = new ushort[width * height];
    }

    public int NextId => _nextId;

    public Province Create(string name, int realmId)
    {
        if (_nextId > MaxId)
            throw new InvalidOperationException("Out of province ids");
        var p = new Province(_nextId++, name, realmId);
        Provinces[p.Id] = p;
        return p;
    }

    public int At(int x, int y) => Cells[y * Width + x];

    public Province? ProvinceAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && Provinces.TryGetValue(Cells[y * Width + x], out var p) ? p : null;

    /// <summary>
    /// Moves one cell into a province (or None), keeping cell counts right.
    /// Returns false if nothing changed. A province left with no cells is
    /// removed.
    /// </summary>
    public bool Assign(int cell, int provinceId)
    {
        int old = Cells[cell];
        if (old == provinceId)
            return false;
        Cells[cell] = (ushort)provinceId;
        if (Provinces.TryGetValue(old, out var from))
        {
            from.CellCount--;
            if (from.CellCount <= 0)
                Provinces.Remove(old);
        }
        if (Provinces.TryGetValue(provinceId, out var to))
            to.CellCount++;
        return true;
    }

    /// <summary>
    /// Hands a whole province to another realm: the province's owner is the
    /// authority, and every member cell's ownership is written to match in
    /// one pass (MECHANICS.md, the ownership invariant).
    /// </summary>
    public void SetRealm(int provinceId, int realmId, OwnershipGrid grid)
    {
        if (!Provinces.TryGetValue(provinceId, out var province))
            return;
        province.RealmId = realmId;
        for (int i = 0; i < Cells.Length; i++)
            if (Cells[i] == provinceId)
                grid.Cells[i] = realmId;
    }

    /// <summary>Recounts every province's cells and places its name; drops provinces with no cells.</summary>
    public void Recount()
    {
        int n = Math.Max(_nextId, Provinces.Keys.DefaultIfEmpty(0).Max() + 1);
        var count = new int[n];
        var sumX = new double[n];
        var sumY = new double[n];
        var area = new double[n];
        for (int y = 0, i = 0; y < Height; y++)
        {
            double cellKm2 = CellAreaKm2(y);
            for (int x = 0; x < Width; x++, i++)
            {
                int id = Cells[i];
                if (id == None || id >= n)
                    continue;
                count[id]++;
                sumX[id] += x;
                sumY[id] += y;
                area[id] += cellKm2;
            }
        }
        foreach (int id in Provinces.Keys.ToList())
        {
            if (count[id] == 0)
            {
                Provinces.Remove(id);
                continue;
            }
            var p = Provinces[id];
            p.CellCount = count[id];
            p.AreaKm2 = area[id];
            p.LabelCell = InsidePoint(id, new Vector2((float)(sumX[id] / count[id]), (float)(sumY[id] / count[id])));
        }
    }

    /// <summary>The area of one cell in row y, in km² (the map spans fixed degrees of longitude and latitude).</summary>
    public double CellAreaKm2(int y)
    {
        double lat = MapView.LatMax - (y + 0.5) / Height * (MapView.LatMax - MapView.LatMin);
        double kmX = (MapView.LonMax - MapView.LonMin) / Width * 111.32 * Math.Cos(lat * Math.PI / 180);
        double kmY = (MapView.LatMax - MapView.LatMin) / Height * 110.57;
        return kmX * kmY;
    }

    /// <summary>The province's own cell nearest to a point (its centroid can fall outside it).</summary>
    public Vector2 InsidePoint(int id, Vector2 target)
    {
        int tx = Math.Clamp((int)target.X, 0, Width - 1), ty = Math.Clamp((int)target.Y, 0, Height - 1);
        if (Cells[ty * Width + tx] == id)
            return new Vector2(tx, ty);
        // Rings of growing radius, sampled every few cells: cheap and near enough for a label.
        for (int r = 4; r < Math.Max(Width, Height); r += 4)
        {
            float best = float.MaxValue;
            Vector2 found = default;
            for (int dy = -r; dy <= r; dy += 2)
            {
                for (int dx = -r; dx <= r; dx += (Math.Abs(dy) == r ? 2 : 2 * r))
                {
                    int x = tx + dx, y = ty + dy;
                    if (x < 0 || y < 0 || x >= Width || y >= Height || Cells[y * Width + x] != id)
                        continue;
                    float d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        found = new Vector2(x, y);
                    }
                }
            }
            if (best < float.MaxValue)
                return found;
        }
        return target;
    }

    // --- Save / load -----------------------------------------------------------------
    // The records go in the save's JSON; the cells separately, as raw bytes.

    public GDictionary ToDict()
    {
        var list = new GArray();
        foreach (var p in Provinces.Values.OrderBy(p => p.Id))
            list.Add(new GDictionary { ["id"] = p.Id, ["name"] = p.Name, ["realm"] = p.RealmId });
        return new GDictionary { ["next_id"] = _nextId, ["provinces"] = list };
    }

    public byte[] CellBytes()
    {
        var bytes = new byte[Cells.Length * 2];
        Buffer.BlockCopy(Cells, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Rebuilds a map from saved records and cell bytes; null if they don't fit this size.</summary>
    public static ProvinceMap? FromSave(int width, int height, GDictionary data, byte[] cellBytes)
    {
        if (cellBytes.Length != width * height * 2)
            return null;
        var map = new ProvinceMap(width, height);
        Buffer.BlockCopy(cellBytes, 0, map.Cells, 0, cellBytes.Length);
        foreach (Variant v in data.TryGetValue("provinces", out var list) ? list.AsGodotArray() : new GArray())
        {
            var d = v.AsGodotDictionary();
            var p = new Province(d["id"].AsInt32(), d["name"].AsString(), d["realm"].AsInt32());
            map.Provinces[p.Id] = p;
        }
        map._nextId = Math.Max(data.TryGetValue("next_id", out var next) ? next.AsInt32() : 1,
            map.Provinces.Keys.DefaultIfEmpty(0).Max() + 1);
        map.Recount();
        return map;
    }
}
