using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// In-game HUD: a top bar (realm, ruler, heir, population, date), the
/// chronicle of recent events bottom-left, and the turn button with map zoom
/// controls bottom-right.
/// </summary>
public partial class Hud : Control
{
    [Signal] public delegate void AdvanceRequestedEventHandler();

    const int ChronicleLines = 8;

    MapView _map = null!;
    Label _realmLabel = null!, _rulerLabel = null!, _heirLabel = null!, _populationLabel = null!, _dateLabel = null!;
    ColorRect _realmBanner = null!;
    Control _populationItem = null!;
    VBoxContainer _chronicle = null!;
    readonly List<string> _entries = new();

    public override void _Ready()
    {
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildTopBar();
        BuildChronicle();
        BuildTurnControls();
    }

    void BuildTopBar()
    {
        var bar = new PanelContainer { ThemeTypeVariation = "BarPanel", MouseFilter = MouseFilterEnum.Stop };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 34);
        bar.AddChild(row);

        var realmBox = new HBoxContainer { TooltipText = "Your realm", MouseFilter = MouseFilterEnum.Pass };
        realmBox.AddThemeConstantOverride("separation", 12);
        row.AddChild(realmBox);
        _realmBanner = new ColorRect { CustomMinimumSize = new Vector2(10, 34), MouseFilter = MouseFilterEnum.Ignore };
        realmBox.AddChild(_realmBanner);
        _realmLabel = ThemeAncient.Label("", "HeaderLabel", 26);
        _realmLabel.MouseFilter = MouseFilterEnum.Ignore;
        realmBox.AddChild(_realmLabel);

        _rulerLabel = Stat(row, "ruler", "Ruler");
        _heirLabel = Stat(row, "heir", "Heir apparent");
        _populationLabel = Stat(row, "population", "People living in your realm's territory (HYDE historical estimate, then simulated)");
        _populationItem = _populationLabel.GetParent<Control>();

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _dateLabel = ThemeAncient.Label("", "DateLabel");
        _dateLabel.TooltipText = "Current year";
        _dateLabel.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(_dateLabel);
    }

    /// <summary>An icon + value pair in the top bar; returns the value label.</summary>
    static Label Stat(HBoxContainer row, string icon, string tip)
    {
        var box = new HBoxContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 8);
        row.AddChild(box);
        var iconRect = ThemeAncient.IconRect(icon, 26);
        iconRect.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        box.AddChild(iconRect);
        var value = ThemeAncient.Label("", fontSize: 20);
        value.MouseFilter = MouseFilterEnum.Ignore;
        box.AddChild(value);
        return value;
    }

    void BuildChronicle()
    {
        var panel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(430, 0),
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        panel.GrowVertical = GrowDirection.Begin;
        panel.Position = new Vector2(20, -20);
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(header);
        header.AddChild(ThemeAncient.IconRect("log", 24));
        header.AddChild(ThemeAncient.Label("Chronicle", "HeaderLabel", 20));
        var rule = new HSeparator();
        rule.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(rule);
        _chronicle = new VBoxContainer();
        _chronicle.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_chronicle);
    }

    void BuildTurnControls()
    {
        var box = new VBoxContainer
        {
            GrowHorizontal = GrowDirection.Begin,
            GrowVertical = GrowDirection.Begin,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        box.Position = new Vector2(-24, -24);
        box.AddThemeConstantOverride("separation", 12);
        AddChild(box);

        var zoomRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        zoomRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(zoomRow);
        zoomRow.AddChild(IconButton(null, "+", "Zoom in  (mouse wheel, +)", () => _map.ZoomIn()));
        zoomRow.AddChild(IconButton(null, "−", "Zoom out  (mouse wheel, -)", () => _map.ZoomOut()));
        zoomRow.AddChild(IconButton("world", "", "Whole map  (Home)", () => _map.ZoomToFit()));

        var advance = new Button
        {
            Text = "Advance Year",
            Icon = ThemeAncient.Icon("end_turn"),
            ThemeTypeVariation = "BigButton",
            CustomMinimumSize = new Vector2(300, 0),
            TooltipText = "End this year's turn  (Enter)",
        };
        advance.Pressed += () => EmitSignal(SignalName.AdvanceRequested);
        box.AddChild(advance);
        box.AddChild(ThemeAncient.Label("Scroll to zoom · Middle-drag or WASD to pan · Esc for menu", "SmallLabel", 14,
            HorizontalAlignment.Right));
    }

    static Button IconButton(string? icon, string glyph, string tip, Action onPressed)
    {
        var b = new Button
        {
            ThemeTypeVariation = "IconButton",
            CustomMinimumSize = new Vector2(46, 46),
            TooltipText = tip,
            FocusMode = FocusModeEnum.None,
        };
        if (icon != null)
        {
            b.Icon = ThemeAncient.Icon(icon);
            b.IconAlignment = HorizontalAlignment.Center;
        }
        else
        {
            b.Text = glyph;
        }
        b.Pressed += onPressed;
        return b;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is Key.Enter or Key.KpEnter)
        {
            EmitSignal(SignalName.AdvanceRequested);
            GetViewport().SetInputAsHandled();
        }
    }

    public void Setup(MapView map)
    {
        _map = map;
        _entries.Clear();
        _entries.Add($"{ThemeAncient.YearText(map.DemoYear)} — The reign of {map.GetPlayerRealm().Name} begins.");
        RenderChronicle();
    }

    public void Refresh()
    {
        var realm = _map.GetPlayerRealm();
        _realmLabel.Text = realm.Name;
        _realmBanner.Color = realm.Color;
        _dateLabel.Text = ThemeAncient.YearText(_map.DemoYear);
        if (_map.Registry.Characters.TryGetValue(realm.RulerId, out var ruler) && ruler.IsAlive)
        {
            _rulerLabel.Text = $"{ruler.Name}, {ruler.AgeIn(_map.DemoYear)}";
            _heirLabel.Text = _map.Registry.ResolveHeir(realm)?.Name ?? "None";
        }
        else
        {
            _rulerLabel.Text = "Interregnum";
            _heirLabel.Text = "—";
        }
        _populationItem.Visible = _map.Population != null;
        if (_map.Population != null)
            _populationLabel.Text = ThemeAncient.GroupThousands((long)_map.PlayerPopulation());
    }

    /// <summary>
    /// Adds the year's news: successions anywhere, and births, marriages and
    /// deaths in the player's own court.
    /// </summary>
    public void LogEvents(IReadOnlyList<ChronicleEvent> events)
    {
        string when = ThemeAncient.YearText(_map.DemoYear);
        var news = events.Where(e => e.IsNewsFor(_map.PlayerRealmId)).ToList();
        if (news.Count == 0)
            _entries.Insert(0, $"{when} — A quiet year.");
        else
            foreach (var e in news)
                _entries.Insert(0, $"{when} — {e.Text}");
        if (_entries.Count > ChronicleLines)
            _entries.RemoveRange(ChronicleLines, _entries.Count - ChronicleLines);
        RenderChronicle();
    }

    void RenderChronicle()
    {
        foreach (var child in _chronicle.GetChildren())
            child.QueueFree();
        for (int i = 0; i < _entries.Count; i++)
        {
            var line = ThemeAncient.Label(_entries[i], fontSize: 17);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(398, 0);
            line.Modulate = new Color(1, 1, 1, 1f - 0.09f * i);  // newest brightest, older ones fade
            _chronicle.AddChild(line);
        }
    }
}
