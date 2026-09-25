using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// The "Regions" map overlay: the ancient geographers' 38 regions, each
/// tinted in its continent's colours (Europa blues, Libya ochres, Asia
/// reds), with a faint boundary and its name. Region names never appear on
/// the default map view (MECHANICS.md, "Geographic regions"). The region of
/// each population node comes from the population engine, which loads it
/// from data/regions/.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void RegionsOverlayChangedEventHandler(bool shown);

    public bool RegionsOverlay { get; private set; }

    readonly List<(Label Label, Vector2 World)> _regionLabels = new();

    public bool RegionsAvailable => Population != null && Population.RegionCount > 1;

    public void SetRegionsOverlay(bool show)
    {
        show &= RegionsAvailable;
        if (RegionsOverlay == show)
            return;
        RegionsOverlay = show;
        (MapSprite?.Material as ShaderMaterial)?.SetShaderParameter("region_strength", show ? 1f : 0f);
        UpdateLabels();
        EmitSignal(SignalName.RegionsOverlayChanged, show);
    }

    /// <summary>The region at a map position, or null at sea or without region data.</summary>
    public RegionInfo? RegionAtWorld(Vector2 world)
    {
        if (!RegionsAvailable)
            return null;
        var e = Population!;
        int node = e.NodeAtCell((int)(world.X / CellPixels), (int)(world.Y / CellPixels), GridWidth, GridHeight);
        int r = e.RegionOf(node);
        return r >= 1 ? e.Regions[r - 1] : null;
    }

    /// <summary>Uploads the region layer and its colours for the map shader.</summary>
    internal void BuildRegionOverlay()
    {
        if (MapSprite?.Material is not ShaderMaterial material)
            return;
        if (!RegionsAvailable)
        {
            RegionsOverlay = false;
            material.SetShaderParameter("region_strength", 0f);
            return;
        }
        var e = Population!;
        var ids = Image.CreateFromData(e.Width, e.Height, false, Image.Format.R8, e.NodeRegion);
        material.SetShaderParameter("region_texture", ImageTexture.CreateFromImage(ids));
        var palette = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        foreach (var r in e.Regions)
            palette.SetPixel(r.Id, 0, RegionColor(r));
        material.SetShaderParameter("region_palette", ImageTexture.CreateFromImage(palette));
        material.SetShaderParameter("region_strength", RegionsOverlay ? 1f : 0f);
    }

    /// <summary>The region names, shown only with the overlay on; made with the other map labels.</summary>
    void BuildRegionLabels()
    {
        foreach (var (label, _) in _regionLabels)
            label.QueueFree();
        _regionLabels.Clear();
        if (!RegionsAvailable || _labelLayer == null)
            return;
        var font = ThemeAncient.BodyFont(620);
        foreach (var r in Population!.Regions)
        {
            var label = new Label
            {
                Text = r.Name.ToUpperInvariant(),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeConstantOverride("outline_size", 6);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.96f, 0.86f, 0.95f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.035f, 0.02f, 0.85f));
            _labelLayer.AddChild(label);
            var world = new Vector2(
                (float)((r.LabelLon - LonMin) / (LonMax - LonMin) * GridWidth * CellPixels),
                (float)((LatMax - r.LabelLat) / (LatMax - LatMin) * GridHeight * CellPixels));
            _regionLabels.Add((label, world));
        }
        UpdateLabels();
    }

    /// <summary>A colour in the region's continent family, varied so neighbours differ.</summary>
    static Color RegionColor(RegionInfo r)
    {
        float hue = r.Continent switch
        {
            "Europa" => 0.58f,  // blue
            "Libya" => 0.11f,   // ochre
            _ => 0.99f,         // Asia: red
        };
        // Golden-ratio steps spread each continent's regions across a band.
        float step = (r.Id * 0.618034f) % 1f;
        hue = (hue + (step - 0.5f) * 0.16f + 1f) % 1f;
        float saturation = 0.45f + 0.25f * ((r.Id * 0.381966f) % 1f);
        float value = 0.72f + 0.2f * ((r.Id * 0.7548777f) % 1f);
        return Color.FromHsv(hue, saturation, value);
    }

    /// <summary>With the overlay on, region names replace realm and province names.</summary>
    void PlaceRegionLabels(Rect2 bounds, List<Rect2> placed, Vector2 screen, float zoom)
    {
        foreach (var (label, world) in _regionLabels)
        {
            if (!RegionsOverlay)
            {
                label.Visible = false;
                continue;
            }
            var size = label.GetMinimumSize();
            var center = (world - _camera!.Position) * zoom + screen / 2f;
            var rect = new Rect2(center - size / 2f, size);
            bool fits = bounds.Encloses(rect) && !placed.Any(other => rect.Grow(4).Intersects(other));
            label.Visible = fits;
            if (fits)
            {
                label.Position = rect.Position;
                placed.Add(rect);
            }
        }
    }
}
