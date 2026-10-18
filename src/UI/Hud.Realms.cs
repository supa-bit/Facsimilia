using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The realms table (decision "Next 46"): every realm side by side,
/// sortable by any column, with a graph of the leaders over the years
/// (L key, or "Compare realms" in the Goals panel).
/// </summary>
public partial class Hud
{
    PanelContainer _realmsPanel = null!;
    GridContainer _realmsGrid = null!;
    RealmGraph _realmGraph = null!;
    OptionButton _graphMetric = null!;
    int _realmsSort = 8;   // by score
    bool _realmsDescending = true;

    static readonly string[] RealmColumns = { "Realm", "People", "Land km²", "Silver", "Income", "Soldiers", "Ships", "Techs", "Score" };
    static readonly string[] GraphMetrics = { "People", "Silver", "Soldiers", "Score" };

    // Two realms side by side (your answer "Comparing two realms": side by side, with graphs).
    OptionButton _compareA = null!, _compareB = null!;
    readonly List<int> _compareIds = new();
    GridContainer _compareGrid = null!;
    RealmGraph _compareGraph = null!;

    void BuildComparison(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        row.AddChild(ThemeAncient.Label("Side by side:", "SubtleLabel", 15));
        _compareA = new OptionButton { FocusMode = FocusModeEnum.None };
        _compareB = new OptionButton { FocusMode = FocusModeEnum.None };
        _compareA.ItemSelected += _ => RefreshComparison();
        _compareB.ItemSelected += _ => RefreshComparison();
        row.AddChild(_compareA);
        row.AddChild(ThemeAncient.Label("and", "SubtleLabel", 15));
        row.AddChild(_compareB);
        row.AddChild(ThemeAncient.Label("  (the graph follows the measure chosen above)", "SubtleLabel", 15));
        _compareGrid = new GridContainer { Columns = RealmColumns.Length, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _compareGrid.AddThemeConstantOverride("h_separation", 40);
        box.AddChild(_compareGrid);
        _compareGraph = new RealmGraph { CustomMinimumSize = new Vector2(950, 130) };
        box.AddChild(_compareGraph);
    }

    /// <summary>The realms to choose from: yours, then every realm still holding land, by name.</summary>
    void FillComparisonChoices()
    {
        int a = _compareA.Selected >= 0 && _compareA.Selected < _compareIds.Count ? _compareIds[_compareA.Selected] : _map.PlayerRealmId;
        int b = _compareB.Selected >= 0 && _compareB.Selected < _compareIds.Count ? _compareIds[_compareB.Selected] : 0;
        _compareIds.Clear();
        _compareA.Clear();
        _compareB.Clear();
        var census = _map.RealmCensus();
        foreach (int id in census.Keys.OrderBy(id => id == _map.PlayerRealmId ? "" : _map.RealmName(id)))
        {
            _compareIds.Add(id);
            _compareA.AddItem(_map.RealmName(id));
            _compareB.AddItem(_map.RealmName(id));
        }
        if (b == 0)   // at first: you and the realm with the highest score after you
            b = census.Keys.Where(id => id != a).OrderByDescending(id => _map.Score(id)).FirstOrDefault();
        _compareA.Selected = Math.Max(0, _compareIds.IndexOf(a));
        _compareB.Selected = Math.Max(0, _compareIds.IndexOf(b));
    }

    void RefreshComparison()
    {
        if (_compareIds.Count == 0)
            return;
        foreach (Node child in _compareGrid.GetChildren())
            child.QueueFree();
        var rows = RealmRows();
        var coin = _map.CoinOf(_map.PlayerRealmId);
        var chosen = new[] { _compareIds[Math.Max(0, _compareA.Selected)], _compareIds[Math.Max(0, _compareB.Selected)] };
        foreach (var name in RealmColumns)
            _compareGrid.AddChild(ThemeAncient.Label(name, "SubtleLabel", 15, HorizontalAlignment.Right));
        foreach (int id in chosen)
        {
            var r = rows.FirstOrDefault(x => x.Id == id);
            if (r.Values == null)
                continue;
            var insight = _map.InsightInto(id);
            _compareGrid.AddChild(ThemeAncient.Label(r.Name, fontSize: 15));
            for (int i = 0; i < r.Values.Length; i++)
            {
                double v = insight == MapView.Insight.Rough ? MapView.Rough(r.Values[i]) : r.Values[i];
                string text = insight == MapView.Insight.Unknown ? "?"
                    : (insight == MapView.Insight.Rough && v != 0 ? "~" : "") + (i is 2 or 3 ? CoinCatalog.Short(CoinCatalog.Instance.Coins(v, coin))
                    : i == 7 ? $"{v:0}" : CoinCatalog.Short(v));
                _compareGrid.AddChild(ThemeAncient.Label(text, fontSize: 15, align: HorizontalAlignment.Right));
            }
        }
        int metric = _graphMetric.Selected + 1;
        _compareGraph.Series = chosen.Where(id => _map.InsightInto(id) != MapView.Insight.Unknown).Select(id => (
            _map.ColorForRealm(id), id == chosen[0],
            _map.Game.Realm(id).History.Select(h => new Vector2(h[0], h[metric])).ToList())).ToList();
        _compareGraph.QueueRedraw();
    }

    void BuildRealmsPanel()
    {
        _realmsPanel = SidePanel("Realms", () => ToggleRealmsPanel(false), 980);
        var box = _realmsPanel.GetNode<VBoxContainer>("Box");
        var hint = ThemeAncient.Label("Click a column to sort. Silver is shown in your coin.", "SubtleLabel", 15);
        box.AddChild(hint);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(950, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _realmsGrid = new GridContainer { Columns = RealmColumns.Length, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _realmsGrid.AddThemeConstantOverride("h_separation", 18);
        _realmsGrid.AddThemeConstantOverride("v_separation", 2);
        scroll.AddChild(_realmsGrid);
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(ThemeAncient.Label("Over the years:", "SubtleLabel", 15));
        _graphMetric = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var m in GraphMetrics)
            _graphMetric.AddItem(m);
        _graphMetric.Select(3);
        _graphMetric.ItemSelected += _ => RefreshRealmsPanel();
        row.AddChild(_graphMetric);
        row.AddChild(ThemeAncient.Label("  you (thick) and the five leaders", "SubtleLabel", 15));
        _realmGraph = new RealmGraph { CustomMinimumSize = new Vector2(950, 150) };
        box.AddChild(_realmGraph);
        BuildComparison(box);
        var open = new Button { Text = "Compare realms", FocusMode = FocusModeEnum.None };
        open.Pressed += () => ToggleRealmsPanel(true);
        _goalsPanel.GetNode<VBoxContainer>("Box").AddChild(open);
        var story = new Button { Text = "Your history so far", FocusMode = FocusModeEnum.None };
        story.Pressed += () => ShowEndScreen(final: false);
        _goalsPanel.GetNode<VBoxContainer>("Box").AddChild(story);
    }

    void ToggleRealmsPanel(bool? show = null)
    {
        _realmsPanel.Visible = show ?? !_realmsPanel.Visible;
        if (_realmsPanel.Visible)
        {
            _realmsPanel.MoveToFront();
            RefreshRealmsPanel();
        }
    }

    void RefreshRealmsPanel()
    {
        if (_realmsPanel == null || !_realmsPanel.Visible)
            return;
        foreach (Node child in _realmsGrid.GetChildren())
            child.QueueFree();
        for (int i = 0; i < RealmColumns.Length; i++)
        {
            int col = i;
            var b = new Button { Text = RealmColumns[i] + (_realmsSort == i ? (_realmsDescending ? " ▾" : " ▴") : ""), Flat = true,
                FocusMode = FocusModeEnum.None, Alignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right };
            b.Pressed += () =>
            {
                _realmsDescending = _realmsSort != col || !_realmsDescending;
                _realmsSort = col;
                RefreshRealmsPanel();
            };
            _realmsGrid.AddChild(b);
        }
        var rows = RealmRows();
        rows = (_realmsSort == 0
            ? rows.OrderBy(r => r.Name)
            : rows.OrderBy(r => _map.InsightInto(r.Id) == MapView.Insight.Unknown ? double.MinValue : r.Values[_realmsSort - 1])).ToList();
        if (_realmsDescending)
            rows.Reverse();
        var coin = _map.CoinOf(_map.PlayerRealmId);
        foreach (var r in rows)
        {
            var name = new HBoxContainer();
            name.AddChild(new ColorRect { Color = r.Color, CustomMinimumSize = new Vector2(10, 16), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            var nl = ThemeAncient.Label(r.Name, fontSize: 15);
            if (r.Id == _map.PlayerRealmId)
                nl.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f));
            name.AddChild(nl);
            _realmsGrid.AddChild(name);
            var insight = _map.InsightInto(r.Id);
            if (insight != MapView.Insight.Exact)
                name.TooltipText = insight == MapView.Insight.Rough
                    ? "Known only roughly: travellers' and merchants' reports"
                    : "Unknown: your realm has no dealings with them (the diplomacy fog)";
            int newsMonths = _map.NewsMonths(r.Id);
            if (!_map.NewsReaches(r.Id))
                name.TooltipText = (name.TooltipText + "\nNo news of them reaches you.").Trim();
            else if (newsMonths > 0)
                name.TooltipText = (name.TooltipText + $"\nTheir news takes about {newsMonths} month{(newsMonths == 1 ? "" : "s")} to reach you.").Trim();
            for (int i = 0; i < r.Values.Length; i++)
            {
                double v = insight == MapView.Insight.Rough ? MapView.Rough(r.Values[i]) : r.Values[i];
                string text = insight == MapView.Insight.Unknown ? "?"
                    : (insight == MapView.Insight.Rough && v != 0 ? "~" : "") + (i is 2 or 3 ? CoinCatalog.Short(CoinCatalog.Instance.Coins(v, coin))
                    : i == 7 ? $"{v:0}" : CoinCatalog.Short(v));
                _realmsGrid.AddChild(ThemeAncient.Label(text, fontSize: 15, align: HorizontalAlignment.Right));
            }
        }
        // The graph: you, and the five leaders by the chosen measure.
        int metric = _graphMetric.Selected + 1;   // index into a history row (0 is the year)
        var leaders = _map.Game.Realms.Values.Where(s => s.History.Count > 0 && _map.InsightInto(s.RealmId) != MapView.Insight.Unknown)
            .OrderByDescending(s => s.History[^1][metric]).Take(5).Select(s => s.RealmId).ToList();
        if (!leaders.Contains(_map.PlayerRealmId))
            leaders.Add(_map.PlayerRealmId);
        _realmGraph.Series = leaders.Select(id => (
            _map.ColorForRealm(id),
            id == _map.PlayerRealmId,
            _map.Game.Realm(id).History.Select(h => new Vector2(h[0], h[metric])).ToList())).ToList();
        _realmGraph.QueueRedraw();
        FillComparisonChoices();
        RefreshComparison();
    }

    /// <summary>One row per realm still holding land: people, land, silver, income, soldiers, ships, techs, score.</summary>
    List<(int Id, string Name, Color Color, double[] Values)> RealmRows()
    {
        var census = _map.RealmCensus();
        var cat = UnitCatalog.Instance;
        var land = _map.RealmLandKm2();
        var rows = new List<(int, string, Color, double[])>();
        foreach (var (id, c) in census)
        {
            var s = _map.Game.Realm(id);
            var (tax, tribute) = Economy.Revenue(c, s.Tax);
            tax *= s.TaxReach;
            double ships = s.Armies.Sum(a => a.RoleCount(cat, UnitRoles.Warships));
            rows.Add((id, _map.RealmName(id), _map.ColorForRealm(id), new double[]
            {
                c.People, land.GetValueOrDefault(id), s.Treasury, tax + tribute, Military.Soldiers(s), ships, s.Techs.Count, _map.Score(id),
            }));
        }
        return rows;
    }
}

/// <summary>A small line graph: one line per realm over the years, the player's thicker.</summary>
public partial class RealmGraph : Control
{
    public List<(Color Color, bool Mine, List<Vector2> Points)> Series { get; set; } = new();

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.25f));
        var all = Series.SelectMany(s => s.Points).ToList();
        if (all.Count < 2)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(12, 24), "The graph fills in as the years pass (every 5 years).",
                HorizontalAlignment.Left, -1, 15, new Color(0.9f, 0.85f, 0.75f));
            return;
        }
        float x0 = all.Min(p => p.X), x1 = Math.Max(all.Max(p => p.X), x0 + 1), y1 = Math.Max(all.Max(p => p.Y), 1);
        const float pad = 10;
        Vector2 Map(Vector2 p) => new(pad + (p.X - x0) / (x1 - x0) * (size.X - 2 * pad), size.Y - pad - p.Y / y1 * (size.Y - 2 * pad));
        foreach (var (color, mine, points) in Series.OrderBy(s => s.Mine))
            if (points.Count >= 2)
                DrawPolyline(points.Select(Map).ToArray(), mine ? new Color(1f, 0.85f, 0.45f) : color, mine ? 3f : 1.6f, true);
        var font = ThemeDB.FallbackFont;
        var ink = new Color(0.9f, 0.85f, 0.75f, 0.8f);
        DrawString(font, new Vector2(pad, size.Y - 2), ThemeAncient.YearText((int)x0), HorizontalAlignment.Left, -1, 13, ink);
        DrawString(font, new Vector2(size.X - 90, size.Y - 2), ThemeAncient.YearText((int)x1), HorizontalAlignment.Left, -1, 13, ink);
    }
}
