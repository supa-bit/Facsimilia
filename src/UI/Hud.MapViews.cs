using System.Collections.Generic;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The HUD's map views: the "Map view" menu (Political, Regions, and the
/// land views grouped by part) and, for a land view, a legend top-left with
/// what it shows, its colour scale, and the value under the mouse.
/// </summary>
public partial class Hud
{
    OptionButton _mapViewMenu = null!;
    readonly List<string> _mapViewKeys = new();   // menu item id -> view key
    PanelContainer _legend = null!;
    Label _legendTitle = null!, _legendText = null!, _legendMin = null!, _legendMax = null!, _legendHere = null!;
    TextureRect _legendBar = null!;

    Control BuildMapViewMenu()
    {
        _mapViewMenu = new OptionButton
        {
            TooltipText = "What the map shows  (R regions, V back to political)",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(150, 40),
            FitToLongestItem = false,
        };
        _mapViewMenu.ItemSelected += index =>
        {
            int id = _mapViewMenu.GetItemId((int)index);
            if (id >= 0 && id < _mapViewKeys.Count)
                _map.SetMapView(_mapViewKeys[id]);
        };
        return _mapViewMenu;
    }

    /// <summary>Fills the menu once the map is known (which views have data) and builds the legend.</summary>
    void ConnectMapViewSignals()
    {
        _mapViewMenu.Clear();
        _mapViewKeys.Clear();
        AddView("Political map", MapView.PoliticalView);
        if (_map.RegionsAvailable)
            AddView("Regions", MapView.RegionsView);
        if (_map.LandAvailable)
        {
            foreach (var (part, views) in MapView.LandViews)
            {
                _mapViewMenu.AddSeparator(part);
                foreach (var v in views)
                    if (_map.Land!.Has(v.Field))
                        AddView(v.Label, v.Field);
            }
        }
        BuildLegend();
        _map.MapViewChanged += _ => RefreshMapView();
        RefreshMapView();

        void AddView(string label, string key)
        {
            _mapViewMenu.AddItem(label, _mapViewKeys.Count);
            _mapViewKeys.Add(key);
        }
    }

    void BuildLegend()
    {
        _legend = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(330, 0),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _legend.SetAnchorsPreset(LayoutPreset.TopLeft);
        _legend.OffsetLeft = _legend.OffsetRight = 20;
        _legend.OffsetTop = _legend.OffsetBottom = 80;
        AddChild(_legend);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _legend.AddChild(box);
        _legendTitle = ThemeAncient.Label("", "HeaderLabel", 20);
        box.AddChild(_legendTitle);
        _legendText = ThemeAncient.Label("", "SubtleLabel", 15);
        _legendText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _legendText.CustomMinimumSize = new Vector2(300, 0);
        box.AddChild(_legendText);
        _legendBar = new TextureRect
        {
            CustomMinimumSize = new Vector2(300, 14),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        box.AddChild(_legendBar);
        var ends = new HBoxContainer();
        box.AddChild(ends);
        _legendMin = ThemeAncient.Label("", "SmallLabel", 14);
        _legendMin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ends.AddChild(_legendMin);
        _legendMax = ThemeAncient.Label("", "SmallLabel", 14, HorizontalAlignment.Right);
        ends.AddChild(_legendMax);
        _legendHere = ThemeAncient.Label("", fontSize: 17);
        box.AddChild(_legendHere);
    }

    void RefreshMapView()
    {
        int id = _mapViewKeys.IndexOf(_map.CurrentView);
        for (int i = 0; i < _mapViewMenu.ItemCount; i++)
            if (_mapViewMenu.GetItemId(i) == id && !_mapViewMenu.IsItemSeparator(i))
                _mapViewMenu.Select(i);
        var view = MapView.FindLandView(_map.CurrentView);
        _legend.Visible = view != null && _map.LandAvailable && !_realmPanel.Visible && !_diplomacyPanel.Visible;
        SetProcess(_legend.Visible);
        if (!_legend.Visible)
            return;
        var field = _map.Land!.Field(view!.Field);
        _legendTitle.Text = view.Label;
        string source = field.Derived ? "Derived from " : "From ";
        _legendText.Text = $"{field.Description}.\n{source}{string.Join("; ", field.Sources)}";
        var bar = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        var backdrop = new Color(0.55f, 0.5f, 0.42f);   // stands in for the terrain behind see-through colours
        for (int i = 0; i < 256; i++)
        {
            var c = MapView.RampColor(view.Ramp, i / 255f);
            bar.SetPixel(i, 0, backdrop.Lerp(new Color(c.R, c.G, c.B), c.A));
        }
        _legendBar.Texture = ImageTexture.CreateFromImage(bar);
        _legendMin.Text = LandLayer.Format(field, field.Min);
        _legendMax.Text = LandLayer.Format(field, field.Max);
        _legendHere.Text = "";
    }

    public override void _Process(double delta)
    {
        if (_legend == null || !_legend.Visible)
            return;
        var here = _map.LandValueAtWorld(_map.GetGlobalMousePosition());
        _legendHere.Text = here is { } h ? $"Here: {LandLayer.Format(h.Field, h.Value)}" : "Here: sea";
    }
}
