using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facsimilia.World;

/// <summary>A historical region that seeds and names a province (data/province_seeds.json).</summary>
public sealed record ProvinceSeed(string Name, double Lon, double Lat);

/// <summary>
/// Divides every realm's land into its 300 BC provinces. Each historical
/// region in data/province_seeds.json that falls on a realm's land becomes a
/// province of that realm; provinces grow outward from their seeds over the
/// realm's own land (never across the sea or into another realm) until they
/// meet. Where no historical region is close enough, extra provinces are
/// added and named after the nearest region ("Northern Media"). Small islands
/// with no region of their own join the nearest province. Finally the
/// straight lines where provinces meet are given a gentle meander.
/// Deterministic: the same map always gives the same provinces.
/// </summary>
public static class ProvinceGenerator
{
    public const string SeedsPath = "res://data/province_seeds.json";

    const int Coarse = 8;               // cells per side of a coarse block, for placing extra provinces
    const int MaxReachCoarse = 26;      // any land farther than this (~200 cells, ~150 km) from a seed gets its own province
    const int MinIslandCells = 2500;    // smaller unseeded pieces (~1,500 km²) join a neighbour instead
    const int SnapRadius = 40;          // how far a seed may move to reach its realm's land (~30 km)
    const float Meander = 9f;           // cells of border meander

    static readonly string[] Directions = { "Eastern", "Southeastern", "Southern", "Southwestern", "Western", "Northwestern", "Northern", "Northeastern" };
    static readonly HashSet<string> Adjectives = new(Directions.Concat(new[]
        { "Upper", "Lower", "Central", "Greater", "Lesser", "Rough", "Middle", "Inner", "Outer", "Far" }));

    public static List<ProvinceSeed> LoadSeeds()
    {
        var parsed = Json.ParseString(Godot.FileAccess.GetFileAsString(SeedsPath)).AsGodotDictionary();
        return parsed["seeds"].AsGodotArray()
            .Select(v => v.AsGodotDictionary())
            .Select(d => new ProvinceSeed(d["name"].AsString(), d["lon"].AsDouble(), d["lat"].AsDouble()))
            .ToList();
    }

    /// <summary>The grid cell of a longitude/latitude (the map's linear projection).</summary>
    public static (int X, int Y) CellOf(double lon, double lat, int width, int height) => (
        Math.Clamp((int)((lon - MapView.LonMin) / (MapView.LonMax - MapView.LonMin) * width), 0, width - 1),
        Math.Clamp((int)((MapView.LatMax - lat) / (MapView.LatMax - MapView.LatMin) * height), 0, height - 1));

    // Realms left without provinces this run (see Generate). Generate isn't
    // re-entrant, so a static is enough.
    static readonly bool[] Unorganized = new bool[256];

    static bool IsRealm(int owner) => owner > 0 && owner != MapView.SeaOwnerId && !Unorganized[owner & 0xFF];

    sealed record Placed(int Cell, int Realm, string Name, bool Historical);

    /// <param name="unorganizedRealms">Realms that start with no provinces at all (the tribal
    /// confederations: loose alliances, not administered states).</param>
    public static ProvinceMap Generate(OwnershipGrid grid, IReadOnlyList<ProvinceSeed> seeds,
        IEnumerable<int>? unorganizedRealms = null)
    {
        Array.Clear(Unorganized);
        foreach (int realm in unorganizedRealms ?? Array.Empty<int>())
            Unorganized[realm & 0xFF] = true;
        int w = grid.Width, h = grid.Height;
        int[] owner = grid.Cells;

        // 1. Historical seeds, each moved onto its realm's land if it sits just off it.
        var placed = new List<Placed>();
        var used = new HashSet<int>();
        foreach (var seed in seeds)
        {
            var (x, y) = CellOf(seed.Lon, seed.Lat, w, h);
            if (Unorganized[owner[y * w + x] & 0xFF] && owner[y * w + x] != MapView.SeaOwnerId)
                continue;  // a tribal region: it names no province, and mustn't slide onto a neighbour
            int cell = Snap(owner, w, h, x, y, SnapRadius, realm: -1);
            if (cell < 0 || !used.Add(cell))
                continue;
            placed.Add(new Placed(cell, owner[cell], seed.Name, true));
        }

        // 2. Extra seeds wherever land is too far from any, found on a coarse grid.
        AddExtraSeeds(owner, w, h, placed);

        // 3. Grow every province from its seed over its own realm's land.
        var map = new ProvinceMap(w, h);
        int realmCells = 0;
        foreach (int o in owner)
            if (IsRealm(o))
                realmCells++;
        var queue = new int[realmCells];
        int head = 0, tail = 0;
        foreach (var seed in placed)
        {
            var province = map.Create(seed.Name, seed.Realm);
            map.Cells[seed.Cell] = (ushort)province.Id;
            queue[tail++] = seed.Cell;
        }
        ushort[] ids = map.Cells;
        while (head < tail)
        {
            int cell = queue[head++];
            int x = cell % w, y = cell / w, realm = owner[cell];
            ushort id = ids[cell];
            int x0 = x > 0 ? -1 : 0, x1 = x < w - 1 ? 1 : 0, y0 = y > 0 ? -1 : 0, y1 = y < h - 1 ? 1 : 0;
            for (int dy = y0; dy <= y1; dy++)
            {
                int row = cell + dy * w;
                for (int dx = x0; dx <= x1; dx++)
                {
                    int n = row + dx;
                    if (ids[n] != 0 || owner[n] != realm)
                        continue;
                    ids[n] = id;
                    queue[tail++] = n;
                }
            }
        }

        // 4. Islands and exclaves no seed reached join the nearest province of their realm.
        AttachUnreached(owner, map, placed, queue);

        // 5. Meandering borders.
        AddMeander(owner, map);

        map.Recount();
        return map;
    }

    /// <summary>The nearest land cell of a realm (any realm when realm is -1) within radius, or -1.</summary>
    static int Snap(int[] owner, int w, int h, int x, int y, int radius, int realm)
    {
        bool Ok(int o) => realm == -1 ? IsRealm(o) : o == realm;
        if (Ok(owner[y * w + x]))
            return y * w + x;
        int best = -1, bestD = int.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = x + dx, ny = y + dy, d = dx * dx + dy * dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || d >= bestD || d > radius * radius || !Ok(owner[ny * w + nx]))
                    continue;
                best = ny * w + nx;
                bestD = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Farthest-point placement on a coarse grid: repeatedly takes the realm
    /// land farthest from every seed and, if it's beyond reach, seeds a
    /// province there - so no province grows much larger than the reach.
    /// </summary>
    static void AddExtraSeeds(int[] owner, int w, int h, List<Placed> placed)
    {
        int cw = (w + Coarse - 1) / Coarse, ch = (h + Coarse - 1) / Coarse;
        var coarseOwner = new int[cw * ch];
        for (int cy = 0; cy < ch; cy++)
            for (int cx = 0; cx < cw; cx++)
            {
                int o = owner[Math.Min(cy * Coarse + Coarse / 2, h - 1) * w + Math.Min(cx * Coarse + Coarse / 2, w - 1)];
                coarseOwner[cy * cw + cx] = IsRealm(o) ? o : 0;
            }

        var dist = new int[cw * ch];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        void Relax(int start)
        {
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % cw, y = c / cw;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= cw || ny >= ch)
                            continue;
                        int n = ny * cw + nx;
                        if (coarseOwner[n] != coarseOwner[c] || dist[n] <= dist[c] + 1)
                            continue;
                        dist[n] = dist[c] + 1;
                        queue.Enqueue(n);
                    }
            }
        }
        void Seed(int coarseCell)
        {
            dist[coarseCell] = 0;
            Relax(coarseCell);
        }
        foreach (var p in placed)
        {
            int c = p.Cell % w / Coarse + p.Cell / w / Coarse * cw;
            if (coarseOwner[c] == p.Realm)
                Seed(c);
        }

        var tooSmall = new bool[cw * ch];
        int minIslandCoarse = MinIslandCells / (Coarse * Coarse);
        while (true)
        {
            int far = -1;
            for (int c = 0; c < dist.Length; c++)
                if (coarseOwner[c] != 0 && !tooSmall[c] && dist[c] > MaxReachCoarse && (far == -1 || dist[c] > dist[far]))
                    far = c;
            if (far == -1)
                break;
            if (dist[far] == int.MaxValue)
            {
                // Unreached land: only a piece big enough gets its own province.
                var piece = Flood(coarseOwner, cw, ch, far);
                if (piece.Count < minIslandCoarse)
                {
                    foreach (int c in piece)
                        tooSmall[c] = true;
                    continue;
                }
                far = piece.OrderBy(c => c).ElementAt(piece.Count / 2);  // somewhere in the middle, not on an edge
            }
            int realm = coarseOwner[far];
            int fine = Snap(owner, w, h, Math.Min(far % cw * Coarse + Coarse / 2, w - 1),
                Math.Min(far / cw * Coarse + Coarse / 2, h - 1), Coarse, realm);
            Seed(far);
            if (fine >= 0)
                placed.Add(new Placed(fine, realm, ExtraName(placed, fine, realm, w), false));
        }
    }

    static List<int> Flood(int[] coarseOwner, int cw, int ch, int start)
    {
        var seen = new HashSet<int> { start };
        var stack = new Stack<int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            int c = stack.Pop();
            int x = c % cw, y = c / cw;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cw || ny >= ch)
                        continue;
                    int n = ny * cw + nx;
                    if (coarseOwner[n] == coarseOwner[start] && seen.Add(n))
                        stack.Push(n);
                }
        }
        return seen.ToList();
    }

    /// <summary>"Northern Media": the direction from the nearest historical region of the same realm.</summary>
    static string ExtraName(List<Placed> placed, int cell, int realm, int w)
    {
        var from = placed.Where(p => p.Historical && p.Realm == realm)
            .OrderBy(p => Dist2(p.Cell, cell, w)).FirstOrDefault()
            ?? placed.Where(p => p.Historical).OrderBy(p => Dist2(p.Cell, cell, w)).FirstOrDefault();
        string baseName = from == null ? "Frontier" : StripAdjective(from.Name);
        string dir = "Outer";
        if (from != null)
        {
            double dx = cell % w - from.Cell % w, dy = cell / w - from.Cell / w;
            double angle = Math.Atan2(dy, dx);  // y grows southwards
            int octant = (int)Math.Round(angle / (Math.PI / 4)) & 7;
            dir = Directions[octant];
        }
        var taken = placed.Select(p => p.Name).ToHashSet();
        foreach (string candidate in new[] { $"{dir} {baseName}", $"Far {dir} {baseName}", $"Outer {baseName}", $"Inner {baseName}" })
            if (!taken.Contains(candidate))
                return candidate;
        for (int n = 2; ; n++)
            if (!taken.Contains($"{dir} {baseName} {n}"))
                return $"{dir} {baseName} {n}";
    }

    static string StripAdjective(string name)
    {
        int space = name.IndexOf(' ');
        return space > 0 && Adjectives.Contains(name[..space]) ? name[(space + 1)..] : name;
    }

    static long Dist2(int a, int b, int w)
    {
        long dx = a % w - b % w, dy = a / w - b / w;
        return dx * dx + dy * dy;
    }

    /// <summary>Gives every realm cell no province reached to the nearest province seed of its realm.</summary>
    static void AttachUnreached(int[] owner, ProvinceMap map, List<Placed> placed, int[] scratch)
    {
        int w = map.Width, h = map.Height;
        var byRealm = placed.Select((p, i) => (p, id: i + 1)).GroupBy(t => t.p.Realm)
            .ToDictionary(g => g.Key, g => g.ToList());
        for (int i = 0; i < owner.Length; i++)
        {
            if (map.Cells[i] != 0 || !IsRealm(owner[i]))
                continue;
            // Flood the unreached piece, then hand it all to one province.
            int realm = owner[i], count = 0;
            scratch[count++] = i;
            map.Cells[i] = ushort.MaxValue;  // visited marker
            for (int k = 0; k < count; k++)
            {
                int c = scratch[k], x = c % w, y = c / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int n = ny * w + nx;
                        if (map.Cells[n] != 0 || owner[n] != realm)
                            continue;
                        map.Cells[n] = ushort.MaxValue;
                        scratch[count++] = n;
                    }
            }
            int id;
            if (byRealm.TryGetValue(realm, out var candidates))
                id = candidates.OrderBy(t => Dist2(t.p.Cell, i, w)).First().id;
            else
            {
                // A realm with no seed at all (only tiny pieces): one province for all of it.
                var province = map.Create("Province " + map.NextId, realm);
                id = province.Id;
                placed.Add(new Placed(i, realm, province.Name, false));
                byRealm[realm] = new() { (placed[^1], id) };
            }
            for (int k = 0; k < count; k++)
                map.Cells[scratch[k]] = (ushort)id;
        }
    }

    /// <summary>
    /// Replaces the straight lines where provinces meet with gently winding
    /// ones: each cell takes the province found a little way off along a
    /// smooth, fixed wave field - but only from its own realm, so realm
    /// borders and coastlines never move.
    /// </summary>
    static void AddMeander(int[] owner, ProvinceMap map)
    {
        int w = map.Width, h = map.Height;
        var original = (ushort[])map.Cells.Clone();
        // sin(a + b) = sin a cos b + cos a sin b: per-row and per-column tables
        // instead of four sines for each of 45 million cells.
        var (xs1, xc1) = Table(w, 1 / 97.0);
        var (ys1, yc1) = Table(h, 1 / 41.0);
        var (xs2, xc2) = Table(w, -1 / 31.0);
        var (ys2, yc2) = Table(h, 1 / 13.0);
        var (xs3, xc3) = Table(w, 1 / 53.0);
        var (ys3, yc3) = Table(h, -1 / 89.0);
        var (xs4, xc4) = Table(w, 1 / 17.0);
        var (ys4, yc4) = Table(h, 1 / 29.0);
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            for (int x = 0; x < w; x++)
            {
                int i = rowStart + x;
                if (original[i] == 0)
                    continue;
                double ox = Meander * (0.7 * (ys1[y] * xc1[x] + yc1[y] * xs1[x]) + 0.3 * (ys2[y] * xc2[x] + yc2[y] * xs2[x]));
                double oy = Meander * (0.7 * (xs3[x] * yc3[y] + xc3[x] * ys3[y]) + 0.3 * (xs4[x] * yc4[y] + xc4[x] * ys4[y]));
                int sx = x + (int)ox, sy = y + (int)oy;
                if (sx < 0 || sy < 0 || sx >= w || sy >= h)
                    continue;
                int s = sy * w + sx;
                if (original[s] != 0 && owner[s] == owner[i])
                    map.Cells[i] = original[s];
            }
        }
    }

    static (double[] Sin, double[] Cos) Table(int n, double scale)
    {
        var sin = new double[n];
        var cos = new double[n];
        for (int i = 0; i < n; i++)
        {
            sin[i] = Math.Sin(i * scale);
            cos[i] = Math.Cos(i * scale);
        }
        return (sin, cos);
    }
}
