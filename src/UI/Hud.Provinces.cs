using Facsimilia.Game;
using System;
using System.Linq;
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
    Label _provinceRegion = null!;
    TabContainer _provinceTabs = null!;

    static readonly (MapMode Mode, string Text, string Tip)[] Modes =
    {
        (MapMode.Inspect, "Inspect", "Click a province to see it; drag to move the map  (1)"),
        (MapMode.EditProvinces, "Provinces", "Redraw your provinces with the brush  (2)"),
        (MapMode.PlanConquest, "Conquest", "Paint land to attack; fights are decided when the turn ends  (3)"),
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
        row.AddChild(BuildMapViewMenu());
        return row;
    }

    void BuildProvincePanel()
    {
        _provincePanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(440, 0),
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
        _provinceRegion = ThemeAncient.Label("", "SubtleLabel", 16);
        box.AddChild(_provinceRegion);
        _provinceTabs = new TabContainer { CustomMinimumSize = new Vector2(420, 0) };
        box.AddChild(_provinceTabs);
        foreach (var name in new[] { "People", "Land", "Buildings", "Military" })
        {
            var page = new VBoxContainer { Name = name };
            page.AddThemeConstantOverride("separation", 3);
            _provinceTabs.AddChild(page);
        }

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
        ConnectMapViewSignals();
        _map.ConquestPlanChanged += RefreshConquestPanel;
        _map.ArmiesChanged += () =>
        {
            RefreshArmiesPanel();
            RefreshConquestPanel();
            _map.ShowReach(_map.Mode == MapMode.PlanConquest);
        };
        _map.ArmyMoveRefused += why => ShowStatus(why, fadeAfter: 3);
        _map.MapModeChanged += mode =>
        {
            for (int i = 0; i < Modes.Length; i++)
                _modeButtons[i].SetPressedNoSignal(Modes[i].Mode == (MapMode)mode);
            RefreshProvincePanel();
            RefreshConquestPanel();
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
        _provincePanel.Visible = (p != null || editing) && _map.Mode != MapMode.PlanConquest;
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
            _provinceRegion.Text = "";
        }
        if (p != null)
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
            var region = _map.RegionAtWorld(p.LabelCell * MapView.CellPixels);
            _provinceRegion.Text = region == null ? "" : $"Region: {region.Name}, {region.Continent}";
            FillProvinceTabs(p, region, mine);
        }
        _provinceTabs.Visible = p != null;
        _provinceRealm.Visible = _provinceRealm.Text != "";
        _provinceArea.Visible = _provinceArea.Text != "";
        _provincePeople.Visible = _provincePeople.Text != "";
        _provinceRegion.Visible = _provinceRegion.Text != "";
        _editBorders.Visible = mine;
        _editBorders.Text = editing ? "Done Redrawing" : "Redraw Borders";
        _newProvince.Visible = mine || editing;
        _provinceHint.Text = editing
            ? (_map.ProvinceBrushProblem() ?? $"Left-drag: add your land to {p!.Name}. Right-drag: take land out of any province (unorganized).")
            : "";
        _provinceHint.Visible = _provinceHint.Text != "";
    }

    static void Clear(Node n)
    {
        foreach (Node c in n.GetChildren())
            c.QueueFree();
    }

    Label Line(Node page, string text, int size = 15)
    {
        var l = ThemeAncient.Label(text, "SubtleLabel", size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(400, 0);
        page.AddChild(l);
        return l;
    }

    /// <summary>The province panel's detail tabs (decision "Playable 31": essentials plus detail tabs).</summary>
    void FillProvinceTabs(Province p, RegionInfo? region, bool mine)
    {
        var people = _provinceTabs.GetNode<VBoxContainer>("People");
        var land = _provinceTabs.GetNode<VBoxContainer>("Land");
        var buildings = _provinceTabs.GetNode<VBoxContainer>("Buildings");
        var military = _provinceTabs.GetNode<VBoxContainer>("Military");
        foreach (var page in new Node[] { people, land, buildings, military })
            Clear(page);
        var ps = _map.ProvinceStateOf(p.Id);

        // People.
        if (ps.Culture != "")
            Line(people, $"People: {_map.CultureName(ps.Culture)}; worship: {_map.ReligionName(ps.Religion)}");
        if (region != null)
        {
            var (dep, ens) = (_map.LabourHistory()?.Shares(region.Name, _map.DemoYear)) ?? (0, 0);
            Line(people, $"Who works (in {region.Name}): {1 - dep - ens:P0} free, {dep:P0} dependent, {ens:P0} enslaved");
        }
        if (p.RealmId > 0)
        {
            Line(people, $"Integration: {ps.Integration:P0} (pays {Loyalty.Yield(ps.Integration):P0} of full tax)");
            var unrest = Line(people, $"Unrest: {ps.Unrest:P0}" + (ps.Unrest > Loyalty.RevoltThreshold
                ? (mine ? "  - may revolt! Lower taxes, build a temple, or wait for integration." : "  - restless") : ""));
            if (ps.Unrest > Loyalty.RevoltThreshold)
                unrest.Modulate = new Color(1f, 0.7f, 0.6f);
        }
        if (mine)
        {
            var taxRow = new HBoxContainer();
            people.AddChild(taxRow);
            taxRow.AddChild(ThemeAncient.Label("Taxes here:", fontSize: 15));
            var tax = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "A rate for this province only; the realm's rate is set in the Treasury panel." };
            tax.AddItem($"The realm's ({TaxNames[(int)_map.PlayerState.Tax]})", -1);
            for (int i = 0; i < 4; i++)
                tax.AddItem(TaxNames[i], i);
            tax.Select(ps.Tax is { } t ? (int)t + 1 : 0);
            int pid = p.Id;
            tax.ItemSelected += index =>
            {
                int id = tax.GetItemId((int)index);
                _map.ProvinceStateOf(pid).Tax = id < 0 ? null : (TaxRate)id;
                _map.InvalidateCensus();
                RefreshProvincePanel();
                RefreshTreasury();
            };
            taxRow.AddChild(tax);
        }

        // Land.
        if (region != null && _map.Game.Nature.ContainsKey(region.Id))
        {
            var n = _map.NatureOf(region.Id);
            string year = n.Harvest < Nature.FamineHarvest ? "famine" : n.Harvest < 0.93 ? "poor"
                : n.Harvest > Nature.BumperHarvest ? "bumper" : n.Harvest > 1.07 ? "good" : "ordinary";
            Line(land, $"This year's harvest in {region.Name}: {year} ({n.Harvest:P0})");
            Line(land, $"Soil {n.Soil:P0}, forests {n.Forest:P0}, pasture {n.Pasture:P0}, fish {n.Fish:P0}");
        }
        var resources = _map.ProvinceResources(p.Id);
        Line(land, resources.Count > 0 ? "Resources: " + string.Join(", ", resources) : "No notable resources.");

        // Buildings.
        var cat = BuildingCatalog.Instance;
        var built = ps.Buildings.Where(kv => kv.Value > 0).Select(kv => (cat[kv.Key]?.Name ?? kv.Key) + (kv.Value > 1 ? $" ×{kv.Value}" : "")).ToList();
        Line(buildings, built.Count > 0 ? "Built: " + string.Join(", ", built) : "Nothing built yet.");
        if (ps.Building != "")
            Line(buildings, $"Building: {cat[ps.Building]?.Name}, {ps.BuildingYearsLeft} year{(ps.BuildingYearsLeft == 1 ? "" : "s")} left" +
                (_map.Game.Sieges.Any(x => x.ProvinceId == p.Id) ? " (halted by the siege)" : ""));
        if (mine)
        {
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 4);
            buildings.AddChild(grid);
            foreach (var b in cat.All)
            {
                string? problem = _map.CanBuild(p, b);
                if (problem is "Already built." or "Built as far as it goes.")
                    continue;
                var button = new Button
                {
                    Text = $"{b.Name} ({b.Cost:0})",
                    Disabled = problem != null,
                    FocusMode = FocusModeEnum.None,
                    TooltipText = $"{b.Description}\n{_map.Money(b.Cost)}, {b.Years} years, upkeep {_map.Money(b.Upkeep)} a year." + (problem != null ? "\n" + problem : ""),
                };
                button.AddThemeFontSizeOverride("font_size", 14);
                var def = b;
                button.Pressed += () =>
                {
                    _map.StartBuilding(p, def);
                    RefreshProvincePanel();
                    RefreshTreasury();
                };
                grid.AddChild(button);
            }
        }

        // Military.
        foreach (var siege in _map.Game.Sieges.Where(x => x.ProvinceId == p.Id))
            Line(military, $"Under siege by {_map.RealmName(siege.Attacker)}: {Math.Min(siege.Progress, 0.99):P0} done");
        foreach (var (realmId, army) in _map.ArmiesInProvince(p.Id))
            Line(military, $"{army.Name} of {_map.RealmName(realmId)}: {Military.Might(army, Domain.Land):0.0} land Might");
        Line(military, $"Walls and garrison hold with about {_map.GarrisonOf(p):0.0} Might.");
        if (military.GetChildCount() == 1)
            Line(military, "No armies here, and no sieges.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode is Key.Enter or Key.KpEnter)
            EmitSignal(SignalName.AdvanceRequested);
        else if (key.Keycode is >= Key.Key1 and <= Key.Key3)
            _map.SetMode(Modes[key.Keycode - Key.Key1].Mode);
        else if (key.Keycode == Key.R && _map.RegionsAvailable)
            _map.SetMapView(_map.CurrentView == MapView.RegionsView ? MapView.PoliticalView : MapView.RegionsView);
        else if (key.Keycode == Key.V)
            _map.SetMapView(MapView.PoliticalView);
        else if (key.Keycode == Key.G)
            ToggleGoodsPanel();
        else if (key.Keycode == Key.T)
            ToggleResearchPanel();
        else
            return;
        GetViewport().SetInputAsHandled();
    }
}
