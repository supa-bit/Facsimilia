using System;
using System.Collections.Generic;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// The province brush (map mode Edit Provinces): the same round brush as the
/// conquest brush, painting the province layer instead of ownership
/// (MECHANICS.md: the brush stays authoritative). Left-drag adds your land
/// under the brush to the selected province, right-drag takes it out of any
/// province (leaving it unorganized). Only the player's own land is touched,
/// and only into the player's own provinces.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void ProvincesChangedEventHandler();

    const float ProvinceBrushScreenPx = 22f;  // the brush is this big on screen at any zoom

    /// <summary>Brush radius in cells: a constant size on screen, so it covers more land when zoomed out.</summary>
    public int ProvinceBrushRadius =>
        _camera == null ? 14 : Math.Clamp((int)Math.Round(ProvinceBrushScreenPx / _camera.Zoom.X / CellPixels), 3, 150);
    BrushCursor? _brushCursor;

    /// <summary>A thin circle under the mouse showing what the brush will cover, in the painting modes.</summary>
    sealed partial class BrushCursor : Node2D
    {
        public float Radius;
        public Color Tint = new(1f, 0.9f, 0.6f, 0.9f);

        public override void _Draw()
        {
            float px = 1f / Math.Max(GetViewport().GetCamera2D()?.Zoom.X ?? 1f, 0.0001f);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.6f), 3f * px);
            DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 48, Tint, 1.5f * px);
        }
    }

    void UpdateBrushCursor()
    {
        if (_brushCursor == null)
        {
            if (MapSprite == null)
                return;
            _brushCursor = new BrushCursor { ZIndex = 1 };
            AddChild(_brushCursor);
        }
        bool painting = Mode is MapMode.EditProvinces or MapMode.PlanConquest;
        _brushCursor.Visible = painting;
        if (!painting)
            return;
        _brushCursor.Radius = (Mode == MapMode.EditProvinces ? ProvinceBrushRadius : ConquestRadius) * CellPixels;
        _brushCursor.Position = GetGlobalMousePosition();
        _brushCursor.QueueRedraw();
    }
    bool _rightDown;
    bool _provinceStrokeChanged;

    /// <summary>Why the province brush can't paint right now, or null if it can.</summary>
    public string? ProvinceBrushProblem()
    {
        if (Provinces == null)
            return "No provinces yet.";
        var target = SelectedProvince;
        if (target == null)
            return "Select one of your provinces first (switch to Inspect and click it), or make a new one.";
        if (target.RealmId != PlayerRealmId)
            return $"{target.Name} isn't yours. Select one of your own provinces.";
        return null;
    }

    void ProvinceBrushAt(Vector2 worldPos, bool erase)
    {
        if (Provinces == null || _provinceImage == null)
            return;
        int target = erase ? ProvinceMap.None : SelectedProvinceId;
        if (!erase && ProvinceBrushProblem() != null)
            return;
        int cx = (int)(worldPos.X / CellPixels), cy = (int)(worldPos.Y / CellPixels);
        int r = ProvinceBrushRadius;
        bool changed = false;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (dx * dx + dy * dy > r * r || !Grid.InBounds(x, y))
                    continue;
                int cell = y * GridWidth + x;
                if (Grid.Cells[cell] != PlayerRealmId || !Provinces.Assign(cell, target))
                    continue;
                _provinceImage.SetPixel(x, y, new Color((target & 0xFF) / 255f, (target >> 8) / 255f, 0f));
                changed = true;
            }
        }
        if (changed)
        {
            _provinceTexture!.Update(_provinceImage);
            _provinceStrokeChanged = true;
        }
    }

    /// <summary>After a stroke: re-place names, drop emptied provinces, tell the HUD.</summary>
    void FinishProvinceStroke()
    {
        if (!_provinceStrokeChanged || Provinces == null)
            return;
        _provinceStrokeChanged = false;
        Provinces.Recount();
        BuildProvinceLabels();
        if (SelectedProvince == null)
            SelectProvince(0);
        InvalidateProvincePeople();
        EmitSignal(SignalName.ProvincesChanged);
    }

    /// <summary>A new, empty province of the player's realm, selected so the brush paints into it.</summary>
    public Province? CreatePlayerProvince()
    {
        if (Provinces == null)
            return null;
        var taken = new HashSet<string>();
        foreach (var p in Provinces.Provinces.Values)
            taken.Add(p.Name);
        string name = "New Province";
        for (int n = 2; taken.Contains(name); n++)
            name = $"New Province {n}";
        var province = Provinces.Create(name, PlayerRealmId);
        SelectProvince(province.Id);
        SetMode(MapMode.EditProvinces);
        InvalidateProvincePeople();
        EmitSignal(SignalName.ProvincesChanged);
        return province;
    }

    public bool RenameProvince(int provinceId, string name)
    {
        name = name.Trim();
        if (Provinces == null || name.Length == 0 || name.Length > 40
            || !Provinces.Provinces.TryGetValue(provinceId, out var province) || province.RealmId != PlayerRealmId)
            return false;
        province.Name = name;
        foreach (var (label, id) in _provinceLabels)
            if (id == provinceId)
                label.Text = name;
        UpdateLabels();
        InvalidateProvincePeople();
        EmitSignal(SignalName.ProvincesChanged);
        return true;
    }

    /// <summary>Right button: deselect, clear the conquest plan, or erase with the province brush.</summary>
    void HandleRightButton(InputEventMouseButton mb)
    {
        _rightDown = mb.Pressed;
        if (!mb.Pressed)
        {
            if (Mode == MapMode.EditProvinces)
                FinishProvinceStroke();
            return;
        }
        switch (Mode)
        {
            case MapMode.Inspect:
                SelectProvince(0);
                break;
            case MapMode.PlanConquest:
                ClearProposal();
                break;
            case MapMode.EditProvinces:
                ProvinceBrushAt(GetGlobalMousePosition(), erase: true);
                break;
        }
    }
}
