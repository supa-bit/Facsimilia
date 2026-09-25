using System;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The HUD's province parts: the map mode buttons (Inspect / Plan conquest /
/// Edit provinces) and the panel describing the selected province, where the
/// player can rename their own provinces, redraw their borders, or found a
/// new one.
/// </summary>
public partial class Hud
{
    PanelContainer _provincePanel = null!;
    ColorRect _provinceBanner = null!;
    Label _provinceTitle = null!;
    LineEdit _provinceName = null!;
    Label _provinceRealm = null!, _provinceArea = null!, _provincePeople = null!, _provinceHint = null!;
    Button _editBorders = null!, _newProvince = null!;
    readonly Button[] _modeButtons = new Button[3];

    static readonly (MapMode Mode, string Text, string Tip)[] Modes =
    {
        (MapMode.Inspect, "Inspect", "Click a province to see it; drag to move the map  (1)"),
        (MapMode.EditProvinces, "Provinces", "Redraw your provinces with the brush  (2)"),
        (MapMode.PlanConquest, "Conquest", "Sketch land you'd like to take (not yet playable)  (3)"),
    };

    HBoxContainer BuildModeRow()
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        row.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        for (int i = 0; i < Modes.Length; i++)
        {
            var (mode, text, tip) = Modes[i];
            var b = new Button
            {
                Text = text,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = mode == MapMode.Inspect,
                TooltipText = tip,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(96, 40),
            };
            b.Pressed += () => _map.SetMode(mode);
            row.AddChild(b);
            _modeButtons[i] = b;
        }
        return row;
    }

    void BuildProvincePanel()
    {
        _provincePanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(340, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        // Pinned 20px in from the top-right corner, below the top bar, growing leftwards.
        _provincePanel.SetAnchorsPreset(LayoutPreset.TopRight);
        _provincePanel.GrowHorizontal = GrowDirection.Begin;
        _provincePanel.OffsetLeft = _provincePanel.OffsetRight = -20;
        _provincePanel.OffsetTop = _provincePanel.OffsetBottom = 80;
        AddChild(_provincePanel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _provincePanel.AddChild(box);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        box.AddChild(header);
        _provinceBanner = new ColorRect { CustomMinimumSize = new Vector2(8, 30) };
        header.AddChild(_provinceBanner);
        _provinceTitle = ThemeAncient.Label("", "HeaderLabel", 22);
        _provinceTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _provinceTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(_provinceTitle);
        _provinceName = new LineEdit
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 40,
            TooltipText = "Rename your province (Enter to confirm)",
        };
        _provinceName.TextSubmitted += text => CommitRename(text);
        _provinceName.FocusExited += () => CommitRename(_provinceName.Text);
        header.AddChild(_provinceName);
        header.AddChild(IconButton(null, "×", "Close  (right-click the map)", () => _map.SelectProvince(0)));

        _provinceRealm = ThemeAncient.Label("", fontSize: 18);
        box.AddChild(_provinceRealm);
        _provinceArea = ThemeAncient.Label("", fontSize: 18);
        box.AddChild(_provinceArea);
        _provincePeople = ThemeAncient.Label("", fontSize: 18);
        box.AddChild(_provincePeople);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        box.AddChild(buttons);
        _editBorders = new Button { Text = "Redraw Borders", FocusMode = FocusModeEnum.None };
        _editBorders.Pressed += () => _map.SetMode(_map.Mode == MapMode.EditProvinces ? MapMode.Inspect : MapMode.EditProvinces);
        buttons.AddChild(_editBorders);
        _newProvince = new Button { Text = "New Province", FocusMode = FocusModeEnum.None,
            TooltipText = "Found a new province, then paint your land into it" };
        _newProvince.Pressed += () => _map.CreatePlayerProvince();
        buttons.AddChild(_newProvince);

        _provinceHint = ThemeAncient.Label("", "SubtleLabel", 15);
        _provinceHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _provinceHint.CustomMinimumSize = new Vector2(310, 0);
        box.AddChild(_provinceHint);
    }

    void ConnectProvinceSignals()
    {
        _map.ProvinceSelected += _ => RefreshProvincePanel();
        _map.ProvincesChanged += RefreshProvincePanel;
        _map.MapModeChanged += mode =>
        {
            for (int i = 0; i < Modes.Length; i++)
                _modeButtons[i].SetPressedNoSignal(Modes[i].Mode == (MapMode)mode);
            RefreshProvincePanel();
        };
    }

    void CommitRename(string text)
    {
        var p = _map.SelectedProvince;
        if (p == null || text.Trim() == p.Name)
            return;
        if (!_map.RenameProvince(p.Id, text))
            _provinceName.Text = p.Name;  // empty or too long: put the old name back
        _provinceName.ReleaseFocus();
    }

    void RefreshProvincePanel()
    {
        var p = _map.SelectedProvince;
        bool editing = _map.Mode == MapMode.EditProvinces;
        _provincePanel.Visible = p != null || editing;
        if (!_provincePanel.Visible)
            return;
        bool mine = p != null && p.RealmId == _map.PlayerRealmId;
        if (p == null)
        {
            _provinceBanner.Color = _map.GetPlayerRealm().Color;
            _provinceTitle.Text = "Your Provinces";
            _provinceTitle.Visible = true;
            _provinceName.Visible = false;
            _provinceRealm.Text = "";
            _provinceArea.Text = "";
            _provincePeople.Text = "";
        }
        else
        {
            var realm = _map.GetRealm(p.RealmId);
            _provinceBanner.Color = realm.Color;
            _provinceTitle.Text = p.Name;
            _provinceTitle.Visible = !mine;
            _provinceName.Visible = mine;
            if (!_provinceName.HasFocus())
                _provinceName.Text = p.Name;
            _provinceRealm.Text = mine ? $"Province of {realm.Name} (yours)" : $"Province of {realm.Name}";
            _provinceArea.Text = $"Area: {ThemeAncient.GroupThousands((long)Math.Round(p.AreaKm2 / 100) * 100)} km²";
            _provincePeople.Text = _map.Population == null ? ""
                : $"People: {ThemeAncient.GroupThousands((long)Math.Round(_map.ProvincePopulation(p.Id) / 100) * 100)}";
        }
        _provinceRealm.Visible = _provinceRealm.Text != "";
        _provinceArea.Visible = _provinceArea.Text != "";
        _provincePeople.Visible = _provincePeople.Text != "";
        _editBorders.Visible = mine;
        _editBorders.Text = editing ? "Done Redrawing" : "Redraw Borders";
        _newProvince.Visible = mine || editing;
        _provinceHint.Text = editing
            ? (_map.ProvinceBrushProblem() ?? $"Left-drag: add your land to {p!.Name}. Right-drag: take land out of any province (unorganized).")
            : "";
        _provinceHint.Visible = _provinceHint.Text != "";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode is Key.Enter or Key.KpEnter)
            EmitSignal(SignalName.AdvanceRequested);
        else if (key.Keycode is >= Key.Key1 and <= Key.Key3)
            _map.SetMode(Modes[key.Keycode - Key.Key1].Mode);
        else
            return;
        GetViewport().SetInputAsHandled();
    }
}
