using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Capitals on the map: a gold star at every realm's seat (its historical
/// capital while it holds it, else its most populous place), and its name
/// as "★ Roma", shown before the other towns when zooming in.
/// </summary>
public partial class MapView
{
    const float CapitalNamesZoom = 1.6f;   // capital names appear this many times closer than the whole-map view
    Node2D? _capitalLayer;
    List<(int RealmId, Vector2 World, string Name)> _capitals = new();

    /// <summary>Every realm's seat now, with its town's name.</summary>
    public List<(int RealmId, Vector2 World, string Name)> Capitals()
    {
        var list = new List<(int, Vector2, string)>();
        if (Population == null)
            return list;
        foreach (var realmId in Game.Realms.Keys)
        {
            int node = CapitalNode(realmId);
            if (node < 0 || Population.NodeOwner[node] != realmId)
                continue;
            var (lon, lat) = NodeLonLat(node);
            var spec = RealCivs.FirstOrDefault(c => CivRealmIds.TryGetValue(c.Key, out int id) && id == realmId);
            bool historic = spec?.CapitalLonLat is { } ll && DisasterCatalog.Km(lon, lat, ll.X, ll.Y) < 60;
            string name = (historic && spec!.CapitalName != "" ? spec.CapitalName : null)
                ?? PlaceCatalog.Instance.Nearest(lon, lat, DemoYear, 40)
                ?? ProvinceAtNode(node)?.Name ?? RealmName(realmId);
            list.Add((realmId, NodeCell(node), name));
        }
        return list;
    }

    /// <summary>Redraws the capital stars (after conquests or loading).</summary>
    public void RefreshCapitals()
    {
        if (MapSprite == null || Population == null)
            return;
        if (_capitalLayer == null)
        {
            _capitalLayer = new Node2D { ZIndex = 4 };
            AddChild(_capitalLayer);
        }
        foreach (Node child in _capitalLayer.GetChildren())
            child.QueueFree();
        _capitals = Capitals();
        var star = Enumerable.Range(0, 10).Select(k =>
        {
            float r = k % 2 == 0 ? 1f : 0.45f, a = -MathF.PI / 2 + k * MathF.PI / 5;
            return new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
        }).ToArray();
        foreach (var (realmId, world, name) in _capitals)
        {
            var mark = new Node2D { Position = world };
            mark.AddChild(new Polygon2D { Polygon = star.Select(p => p * 1.35f).ToArray(), Color = new Color(0.12f, 0.08f, 0.03f, 0.9f) });
            mark.AddChild(new Polygon2D { Polygon = star, Color = new Color(1f, 0.84f, 0.3f) });
            _capitalLayer.AddChild(mark);
        }
        ScaleCapitals();
        _towns = null;   // capital names lead the town labels
    }

    void ScaleCapitals()
    {
        if (_capitalLayer == null || _camera == null)
            return;
        float s = 7f / Math.Max(_camera.Zoom.X, 0.02f);
        foreach (Node child in _capitalLayer.GetChildren())
            if (child is Node2D mark)
                mark.Scale = Vector2.One * s;
    }
}
