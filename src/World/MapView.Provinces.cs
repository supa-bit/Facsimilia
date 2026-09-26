using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.World;

/// <summary>What a left click or drag on the map does.</summary>
public enum MapMode
{
    Inspect,        // click selects a province, drag pans
    PlanConquest,   // the proposal brush (conquest by painting, build step 7)
    EditProvinces,  // the province brush: redraw your own provinces
}

/// <summary>
/// Provinces on the map: their borders (a second texture the map shader
/// reads), their names when zoomed in, selecting one with a click, and the
/// province brush.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void ProvinceSelectedEventHandler(int provinceId);
    [Signal] public delegate void MapModeChangedEventHandler(int mode);

    const float ProvinceNamesZoom = 2.2f;  // province names appear this many times closer than the whole-map view
    const float ClickSlop = 5f;            // screen pixels a click may move and still count as a click

    public int SelectedProvinceId { get; private set; }
    public MapMode Mode { get; private set; } = MapMode.Inspect;

    Image? _provinceImage;
    ImageTexture? _provinceTexture;
    readonly List<(Label Label, int ProvinceId)> _provinceLabels = new();
    Vector2 _leftDownAt;
    bool _leftDown, _leftDragged;

    public Province? SelectedProvince =>
        Provinces != null && Provinces.Provinces.TryGetValue(SelectedProvinceId, out var p) ? p : null;

    public void SetMode(MapMode mode)
    {
        if (Mode == mode)
            return;
        Mode = mode;   // a conquest plan stays painted until the turn ends or it's cleared
        ShowReach(mode == MapMode.PlanConquest);
        EmitSignal(SignalName.MapModeChanged, (int)mode);
    }

    /// <summary>Selects a province (0 for none): highlighted on the map, details in the HUD.</summary>
    public void SelectProvince(int provinceId)
    {
        if (Provinces == null || (provinceId != 0 && !Provinces.Provinces.ContainsKey(provinceId)))
            provinceId = 0;
        SelectedProvinceId = provinceId;
        (MapSprite?.Material as ShaderMaterial)?.SetShaderParameter("selected_province", provinceId);
        EmitSignal(SignalName.ProvinceSelected, provinceId);
    }

    /// <summary>The province id at a map position (0 for none).</summary>
    public int ProvinceIdAtWorld(Vector2 world) => ProvinceAtWorld(world)?.Id ?? 0;

    /// <summary>Moves the camera to a map position, zoomed to this many times the whole-map view.</summary>
    public void CenterOn(Vector2 world, float timesFit)
    {
        if (_camera == null)
            return;
        float z = Math.Clamp(_fitZoom * timesFit, _fitZoom, MaxZoom);
        _camera.Zoom = new Vector2(z, z);
        ScaleArmyMarkers();
        _camera.Position = world;
        ClampCamera();
    }

    public Province? ProvinceAtWorld(Vector2 world) =>
        Provinces?.ProvinceAt((int)(world.X / CellPixels), (int)(world.Y / CellPixels));

    /// <summary>People living in a province now, from the population engine (0 without HYDE data).</summary>
    Dictionary<int, double>? _provincePeople;
    int _provincePeopleYear = int.MinValue;

    /// <summary>People living in a province (counted for every province at once, once a year or after borders change).</summary>
    public double ProvincePopulation(int provinceId)
    {
        if (Population == null || Provinces == null)
            return 0;
        if (_provincePeople == null || _provincePeopleYear != DemoYear)
        {
            _provincePeople = new Dictionary<int, double>();
            foreach (int node in Population.LandNodes)
            {
                int nx = node % Population.Width, ny = node / Population.Width;
                int cx = (int)((nx + 0.5) * GridWidth / Population.Width);
                int cy = (int)((ny + 0.5) * GridHeight / Population.Height);
                int prov = Provinces.Cells[cy * GridWidth + cx];
                if (prov != ProvinceMap.None)
                    _provincePeople[prov] = _provincePeople.GetValueOrDefault(prov) + Population.Pop[node];
            }
            _provincePeopleYear = DemoYear;
        }
        return _provincePeople.GetValueOrDefault(provinceId);
    }

    /// <summary>Borders or people changed within the year: count again.</summary>
    internal void InvalidateProvincePeople()
    {
        _provincePeopleYear = int.MinValue;
        _nodeProvinceYear = int.MinValue;
    }

    /// <summary>Uploads the province layer for the map shader; call after building the map image.</summary>
    internal void BuildProvinceTexture()
    {
        if (Provinces == null || MapSprite == null)
            return;
        _provinceImage = Image.CreateFromData(GridWidth, GridHeight, false, Image.Format.Rg8, Provinces.CellBytes());
        _provinceTexture = ImageTexture.CreateFromImage(_provinceImage);
        var material = (ShaderMaterial)MapSprite.Material;
        material.SetShaderParameter("province_texture", _provinceTexture);
        material.SetShaderParameter("selected_province", SelectedProvinceId);
        UpdateProvinceStrength();
    }

    /// <summary>Province borders are faint on the whole-map view and full strength once zoomed in.</summary>
    void UpdateProvinceStrength()
    {
        if (MapSprite?.Material is not ShaderMaterial material || _camera == null || _fitZoom <= 0)
            return;
        float closer = _camera.Zoom.X / _fitZoom;
        material.SetShaderParameter("province_strength", Math.Clamp(0.45f + (closer - 1f) * 0.3f, 0.45f, 1f));
    }

    // --- Province names ---------------------------------------------------------------

    void BuildProvinceLabels()
    {
        foreach (var (label, _) in _provinceLabels)
            label.QueueFree();
        _provinceLabels.Clear();
        if (Provinces == null || _labelLayer == null)
            return;
        var font = ThemeAncient.BodyFont(560);
        foreach (var p in Provinces.Provinces.Values.OrderByDescending(p => p.CellCount))
        {
            var label = new Label { Text = p.Name, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", (int)Math.Clamp(12.0 + Math.Sqrt(p.CellCount) / 70.0, 13.0, 19.0));
            label.AddThemeColorOverride("font_color", new Color(0.98f, 0.94f, 0.84f, 0.92f));
            label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.035f, 0.02f, 0.8f));
            label.AddThemeConstantOverride("outline_size", 5);
            _labelLayer.AddChild(label);
            _provinceLabels.Add((label, p.Id));
        }
        UpdateLabels();
    }

    /// <summary>Places province names after the realm names, skipping any that would overlap one already placed.</summary>
    void PlaceProvinceLabels(Rect2 bounds, List<Rect2> placed, Vector2 screen, float zoom)
    {
        bool show = Provinces != null && _fitZoom > 0 && zoom >= _fitZoom * ProvinceNamesZoom;
        foreach (var (label, id) in _provinceLabels)
        {
            if (!show || !Provinces!.Provinces.TryGetValue(id, out var p))
            {
                label.Visible = false;
                continue;
            }
            var center = (p.LabelCell * CellPixels - _camera!.Position) * zoom + screen / 2f;
            if (!bounds.HasPoint(center))
            {
                label.Visible = false;
                continue;
            }
            var size = label.GetMinimumSize();
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

    // --- Mouse ------------------------------------------------------------------------

    /// <summary>Left button: what it does depends on the map mode. Returns true if handled.</summary>
    bool HandleLeftButton(InputEventMouseButton mb)
    {
        if (mb.Pressed && MovingArmyId != 0)
        {
            // Moving an army: this click chooses where it goes.
            var army = PlayerState.ArmyById(MovingArmyId);
            MovingArmyId = 0;
            if (army != null && Population != null)
            {
                var world = GetGlobalMousePosition();
                int node = Population.NodeAtCell((int)(world.X / CellPixels), (int)(world.Y / CellPixels), GridWidth, GridHeight);
                string? problem = CanMoveArmy(army, node);
                if (problem == null)
                    MoveArmy(army, node);
                else
                    EmitSignal(SignalName.ArmyMoveRefused, problem);
            }
            EmitSignal(SignalName.ArmiesChanged);
            return true;
        }
        if (mb.Pressed)
        {
            _leftDown = true;
            _leftDragged = false;
            _leftDownAt = mb.Position;
            if (Mode == MapMode.PlanConquest)
                PaintAt(GetGlobalMousePosition());
            else if (Mode == MapMode.EditProvinces)
                ProvinceBrushAt(GetGlobalMousePosition(), erase: false);
            return true;
        }
        if (!_leftDown)
            return false;
        _leftDown = false;
        if (Mode == MapMode.Inspect && !_leftDragged)
            SelectProvince(ProvinceAtWorld(GetGlobalMousePosition())?.Id ?? 0);
        else if (Mode == MapMode.EditProvinces)
            FinishProvinceStroke();
        else if (Mode == MapMode.PlanConquest)
            EmitSignal(SignalName.ConquestPlanChanged);
        return true;
    }

    /// <summary>Mouse movement with the left button held.</summary>
    void HandleLeftDrag(InputEventMouseMotion motion)
    {
        if (!_leftDragged && motion.Position.DistanceTo(_leftDownAt) < ClickSlop && Mode == MapMode.Inspect)
            return;
        _leftDragged = true;
        switch (Mode)
        {
            case MapMode.Inspect when _camera != null:
                _camera.Position -= motion.Relative / _camera.Zoom.X;
                ClampCamera();
                break;
            case MapMode.PlanConquest:
                PaintAt(GetGlobalMousePosition());
                break;
            case MapMode.EditProvinces:
                ProvinceBrushAt(GetGlobalMousePosition(), erase: false);
                break;
        }
    }
}
