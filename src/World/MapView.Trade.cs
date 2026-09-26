using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>Trade on the map: who can trade with whom, and where goods from beyond the map arrive.</summary>
public partial class MapView
{
    /// <summary>Realms that share a border on the population grid (index pairs, smaller first).</summary>
    HashSet<(int, int)> LandNeighbours()
    {
        var pairs = new HashSet<(int, int)>();
        if (Population == null)
            return pairs;
        int w = Population.Width, h = Population.Height;
        int[] owner = Population.NodeOwner;
        foreach (int i in Population.LandNodes)
        {
            int a = owner[i];
            if (a <= 0)
                continue;
            int x = i % w, y = i / w;
            if (x + 1 < w)
                Add(a, owner[i + 1]);
            if (y + 1 < h)
                Add(a, owner[i + w]);
        }
        return pairs;

        void Add(int a, int b)
        {
            if (b > 0 && b != a)
                pairs.Add(a < b ? (a, b) : (b, a));
        }
    }

    /// <summary>How far from a route's place a realm's land may lie and still hold it (ports sit at the water's edge).</summary>
    const double RoutePlaceKm = 45;
    /// <summary>Goods pass through at most this many links (borders or routes) from seller to buyer.</summary>
    const int MaxLinks = 3;

    /// <summary>Who holds each place of each open route this year (0 for no one).</summary>
    public List<(TradeRoute Route, int[] Owners)> RouteHolders()
    {
        var list = new List<(TradeRoute, int[])>();
        if (Population == null)
            return list;
        foreach (var route in TradeRouteCatalog.Instance.OpenIn(DemoYear))
            list.Add((route, route.Points.Select(p => OwnerNear(p.Lon, p.Lat)).ToArray()));
        return list;
    }

    /// <summary>The owner of the nearest land within RoutePlaceKm of a place.</summary>
    int OwnerNear(double lon, double lat)
    {
        var pop = Population!;
        int centre = pop.NodeAtLonLat(lon, lat, LonMin, LonMax, LatMin, LatMax);
        int cx = centre % pop.Width, cy = centre / pop.Width;
        double kmX = (LonMax - LonMin) / pop.Width * 111.2 * Math.Cos(lat * Math.PI / 180), kmY = (LatMax - LatMin) / pop.Height * 111.2;
        int rx = (int)Math.Ceiling(RoutePlaceKm / kmX), ry = (int)Math.Ceiling(RoutePlaceKm / kmY);
        int best = 0;
        double bestD = double.MaxValue;
        for (int y = Math.Max(0, cy - ry); y <= Math.Min(pop.Height - 1, cy + ry); y++)
            for (int x = Math.Max(0, cx - rx); x <= Math.Min(pop.Width - 1, cx + rx); x++)
            {
                int o = pop.NodeOwner[y * pop.Width + x];
                if (o <= 0)
                    continue;
                double d = Math.Pow((x - cx) * kmX, 2) + Math.Pow((y - cy) * kmY, 2);
                if (d < bestD && d <= RoutePlaceKm * RoutePlaceKm)
                {
                    bestD = d;
                    best = o;
                }
            }
        return best;
    }

    double[] RunTrade(GoodsCatalog cat, Dictionary<int, RealmGoods> goods, Dictionary<int, RealmCensus> census)
    {
        var neighbours = LandNeighbours();
        var routes = RouteHolders();
        var onRoute = new HashSet<(int, int)>();
        foreach (var (_, owners) in routes)
        {
            var held = owners.Where(o => o > 0).Distinct().ToList();
            foreach (int a in held)
                foreach (int b in held)
                    if (a < b)
                        onRoute.Add((a, b));
        }
        // Goods travel along chains of links (a shared border or route), up to MaxLinks, through middlemen.
        var links = new Dictionary<int, List<int>>();
        foreach (var (x, y) in neighbours.Concat(onRoute))
            if (goods.ContainsKey(x) && goods.ContainsKey(y) && !Game.Wars.AtWar(x, y))
            {
                if (!links.TryGetValue(x, out var lx)) links[x] = lx = new List<int>();
                if (!links.TryGetValue(y, out var ly)) links[y] = ly = new List<int>();
                if (!lx.Contains(y)) lx.Add(y);
                if (!ly.Contains(x)) ly.Add(x);
            }
        var via = new Dictionary<(int, int), List<int>>();   // (from, to) -> the realms between
        foreach (int start in links.Keys)
        {
            var prev = new Dictionary<int, int> { [start] = start };
            var frontier = new List<int> { start };
            for (int hop = 0; hop < MaxLinks && frontier.Count > 0; hop++)
            {
                var next = new List<int>();
                foreach (int u in frontier)
                    foreach (int v in links[u])
                        if (!prev.ContainsKey(v) && !Game.Wars.AtWar(start, v))
                        {
                            prev[v] = u;
                            next.Add(v);
                        }
                frontier = next;
            }
            foreach (int end in prev.Keys)
                if (end != start)
                {
                    var middle = new List<int>();
                    for (int m = prev[end]; m != start; m = prev[m])
                        middle.Add(m);
                    via[(start, end)] = middle;
                }
        }
        IEnumerable<int> Partners(int realm) =>
            goods.Keys.Where(other => other != realm && via.ContainsKey((realm, other)));
        // Where goods from beyond the map arrive: each realm's share of the people in the entry regions.
        var entryShare = new Dictionary<string, Dictionary<int, double>>();
        if (Population != null)
            foreach (var g in cat.Goods.Where(g => g.Source == GoodSource.Beyond))
            {
                var regions = Population.Regions.Where(r => g.EntryRegions.Contains(r.Name)).Select(r => r.Id).ToHashSet();
                var byRealm = new Dictionary<int, double>();
                double total = 0;
                foreach (int i in Population.LandNodes)
                    if (regions.Contains(Population.RegionOf(i)))
                    {
                        total += Population.Pop[i];
                        if (Population.NodeOwner[i] > 0)
                            byRealm[Population.NodeOwner[i]] = byRealm.GetValueOrDefault(Population.NodeOwner[i]) + Population.Pop[i];
                    }
                entryShare[g.Id] = byRealm.ToDictionary(x => x.Key, x => total > 0 ? x.Value / total : 0);
            }
        var flows = new List<(int From, int To, double Value)>();
        var prices = Trade.Run(cat, goods, Partners, (g, realm) =>
            entryShare.TryGetValue(g.Id, out var s) ? s.GetValueOrDefault(realm) : 0, flows);
        PayTransit(routes, flows, goods, via);
        return prices;
    }

    /// <summary>
    /// Tolls: goods between two realms pass the realms between them (the
    /// middlemen of a chain of links, and whoever holds the places between
    /// them on a shared route), and each takes a toll.
    /// </summary>
    static void PayTransit(List<(TradeRoute Route, int[] Owners)> routes, List<(int From, int To, double Value)> flows,
        Dictionary<int, RealmGoods> goods, Dictionary<(int, int), List<int>> via)
    {
        var between = new Dictionary<(int, int), HashSet<int>>();
        foreach (var (from, to, value) in flows)
        {
            var key = (from, to);
            if (!between.TryGetValue(key, out var tollers))
            {
                tollers = new HashSet<int>();
                int shortest = int.MaxValue;
                foreach (var (_, owners) in routes)
                    for (int i = 0; i < owners.Length; i++)
                        if (owners[i] == from)
                            for (int j = 0; j < owners.Length; j++)
                                if (owners[j] == to && Math.Abs(i - j) < shortest)
                                {
                                    shortest = Math.Abs(i - j);
                                    tollers = new HashSet<int>();
                                    for (int k = Math.Min(i, j) + 1; k < Math.Max(i, j); k++)
                                        if (owners[k] > 0 && owners[k] != from && owners[k] != to)
                                            tollers.Add(owners[k]);
                                }
                if (via.TryGetValue(key, out var middle))
                    tollers.UnionWith(middle);
                between[key] = tollers;
            }
            foreach (int t in tollers)
                if (goods.TryGetValue(t, out var g))
                    g.TransitIncome += value * TradeRouteCatalog.TransitToll;
        }
    }

    // --- On the map ----------------------------------------------------------------

    Node2D? _routeLayer;
    int _routeLayerYear = int.MinValue;

    Vector2 WorldAt(double lon, double lat) => new(
        (float)((lon - LonMin) / (LonMax - LonMin) * GridWidth * CellPixels),
        (float)((LatMax - lat) / (LatMax - LatMin) * GridHeight * CellPixels));

    /// <summary>Shows or hides the trade routes open this year: sea lanes blue, roads gold, rivers green-blue.</summary>
    void ShowTradeRoutes(bool show)
    {
        if (!show)
        {
            if (_routeLayer != null)
                _routeLayer.Visible = false;
            return;
        }
        if (_routeLayer == null)
        {
            _routeLayer = new Node2D { ZIndex = 4 };
            AddChild(_routeLayer);
        }
        if (_routeLayerYear != DemoYear)
        {
            foreach (Node child in _routeLayer.GetChildren())
                child.QueueFree();
            foreach (var route in TradeRouteCatalog.Instance.OpenIn(DemoYear))
            {
                var line = new Line2D
                {
                    Points = route.Points.Select(p => WorldAt(p.Lon, p.Lat)).ToArray(),
                    DefaultColor = route.Kind switch
                    {
                        "sea" => new Color(0.75f, 0.9f, 1f, 0.9f),
                        "river" => new Color(0.45f, 0.85f, 0.8f, 0.95f),
                        _ => new Color(1f, 0.82f, 0.35f, 0.95f),
                    },
                    JointMode = Line2D.LineJointMode.Round,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                };
                line.SetMeta("route", route.Name);
                _routeLayer.AddChild(line);
                foreach (var p in route.Points)
                    _routeLayer.AddChild(new Polygon2D
                    {
                        Polygon = Enumerable.Range(0, 10).Select(k => new Vector2(MathF.Cos(k * MathF.Tau / 10), MathF.Sin(k * MathF.Tau / 10))).ToArray(),
                        Position = WorldAt(p.Lon, p.Lat),
                        Color = new Color(0.1f, 0.07f, 0.03f, 0.9f),
                    });
            }
            _routeLayerYear = DemoYear;
        }
        _routeLayer.Visible = true;
        ScaleRouteLines();
    }

    void ScaleRouteLines()
    {
        if (_routeLayer == null || !_routeLayer.Visible || _camera == null)
            return;
        float px = 1f / Math.Max(_camera.Zoom.X, 0.02f);
        foreach (Node child in _routeLayer.GetChildren())
            if (child is Line2D line)
                line.Width = 3f * px;
            else if (child is Polygon2D dot)
                dot.Scale = Vector2.One * 4f * px;
    }
}
