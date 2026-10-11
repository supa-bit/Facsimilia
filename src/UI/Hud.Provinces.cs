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
        Float(_provincePanel, "Province");

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
        _map.ArmiesChanged += RefreshOutliner;
        _map.ArmiesChanged += () =>
        {
            RefreshArmiesPanel();
            RefreshConquestPanel();
            _map.ShowReach(_map.Mode == MapMode.PlanConquest);
        };
        _map.ArmyMoveRefused += why => ShowStatus(why, fadeAfter: 3);
        _map.RoadPlanned += ShowRoadBar;
        _map.MapModeChanged += mode =>
        {
            for (int i = 0; i < Modes.Length; i++)
                _modeButtons[i].SetPressedNoSignal(Modes[i].Mode == (MapMode)mode);
            RefreshProvincePanel();
            RefreshConquestPanel();
        };
    }

    // --- Drawing roads -------------------------------------------------------------------

    PanelContainer? _roadBar;
    Label? _roadText;
    Button? _roadBuild;

    /// <summary>The bar shown while you draw a road: what it will cost, with Build and Cancel.</summary>
    void ShowRoadBar(string text)
    {
        if (_roadBar == null)
        {
            _roadBar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
            _roadBar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
            _roadBar.GrowHorizontal = GrowDirection.Both;
            _roadBar.GrowVertical = GrowDirection.Begin;
            _roadBar.Position = new Vector2(_roadBar.Position.X, _roadBar.Position.Y - 120);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            _roadBar.AddChild(row);
            _roadText = ThemeAncient.Label("", fontSize: 16);
            _roadText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _roadText.CustomMinimumSize = new Vector2(640, 0);
            row.AddChild(_roadText);
            _roadBuild = new Button { Text = "Build", FocusMode = FocusModeEnum.None };
            _roadBuild.Pressed += () =>
            {
                string? why = _map.OrderPlannedRoad();
                if (why != null)
                    ShowStatus(why, fadeAfter: 3);
                RefreshProvincePanel();
                RefreshTreasury();
            };
            row.AddChild(_roadBuild);
            var cancel = new Button { Text = "Cancel", FocusMode = FocusModeEnum.None };
            cancel.Pressed += () => _map.StopDrawingRoad();
            row.AddChild(cancel);
            AddChild(_roadBar);
        }
        _roadBar.Visible = text != "";
        _roadText!.Text = text;
        _roadBuild!.Visible = _map.RoadPreview != null;
        _roadBuild.Disabled = _map.RoadPreview?.Problem != null;
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
                : $"People: {ThemeAncient.GroupThousands((long)Math.Round(_map.ProvincePopulation(p.Id) / 100) * 100)}" +
                  (_map.TownsIn(p.Id) is { Count: > 0 } towns ? $"\nTowns: {string.Join(", ", towns)}" : "");
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
        var built = ps.Buildings.Where(kv => kv.Value > 0).Select(kv => cat[kv.Key]?.LevelName(kv.Value) ?? kv.Key).OrderBy(x => x).ToList();
        Line(buildings, built.Count > 0 ? "Built: " + string.Join(", ", built) : "Nothing built yet.");
        int slots = _map.BuildSlots(p.Id);
        bool sieged = _map.Game.Sieges.Any(x => x.ProvinceId == p.Id);
        for (int i = 0; i < ps.Works.Count; i++)
        {
            var w = ps.Works[i];
            var def = cat[w.Id];
            int level = ps.Level(w.Id) + ps.Works.Take(i + 1).Count(x => x.Id == w.Id);
            Line(buildings, $"{(i < slots ? "Building" : "Waiting")}: {def?.LevelName(level) ?? w.Id}, {w.Left} year{(w.Left == 1 ? "" : "s")}" +
                (i < slots ? " left" : "") + (sieged && i < slots ? " (halted by the siege)" : ""));
        }
        if (ps.NeglectYears > 0)
            Line(buildings, $"Neglected for {ps.NeglectYears} year(s): unpaid or unused buildings decay after {MapView.NeglectYearsToDecay}.");
        // Wonders: those standing here, those under way, and those you may begin.
        foreach (var w in _map.WondersIn(p.Id))
            Line(buildings, $"{(w.IsWonder ? "Wonder" : "Great work")}: {w.Name}" + (_map.Game.Wonders[w.Id].Saved ? " (saved from its fall)" : ""));
        foreach (var work in _map.Game.WonderWorks.Where(x => x.Province == p.Id))
            Line(buildings, $"Building the {WonderCatalog.Instance[work.Id]?.Name}: {work.Left} year{(work.Left == 1 ? "" : "s")} left" +
                (work.Realm != _map.PlayerRealmId ? $" ({_map.RealmName(work.Realm)})" : "") + (sieged ? " (halted by the siege)" : ""));
        if (mine)
        {
            var open = WonderCatalog.Instance.All.Where(w => _map.WonderOpen(w) && w.Start <= _map.DemoYear).OrderBy(w => w.IsWonder ? 0 : 1).ToList();
            if (open.Count > 0)
            {
                Line(buildings, "Wonders, canals and great works open to build (each once in the world; whoever finishes first has it):");
                var grid = new GridContainer { Columns = 2 };
                grid.AddThemeConstantOverride("h_separation", 6);
                grid.AddThemeConstantOverride("v_separation", 4);
                buildings.AddChild(grid);
                foreach (var w in open)
                {
                    string? problem = _map.CanBuildWonder(p, w);
                    if (problem == "Needs a coast." || problem == "Already under way.")
                        continue;
                    var rivals = _map.Game.WonderWorks.Where(x => x.Id == w.Id).Select(x => $"{_map.RealmName(x.Realm)} has {x.Left} year(s) to go").ToList();
                    var effects = string.Join(", ", w.Effects.Select(e => DescribeEffect(e.Key, e.Value)));
                    var button = new Button
                    {
                        Text = $"{w.Name} ({w.Cost:0})",
                        Disabled = problem != null,
                        FocusMode = FocusModeEnum.None,
                        TooltipText = $"{w.Description}\n{_map.Money(w.Cost)}, {w.Years} years, {w.Workers:N0} workers, needs {string.Join(", ", w.Goods)}." +
                            $"\nEffects: {effects}; +{w.Culture:0} culture." +
                            (w.Fall is int f ? $"\nIn history it fell in {ThemeAncient.YearText(f)} ({w.FallWhy}); yours can be saved for half its price (you are asked when the day comes)." : "") +
                            (rivals.Count > 0 ? "\nUnder way: " + string.Join("; ", rivals) + "." : "") +
                            (problem != null ? "\n" + problem : ""),
                    };
                    button.AddThemeFontSizeOverride("font_size", 14);
                    var def = w;
                    button.Pressed += () =>
                    {
                        _map.StartWonder(p, def);
                        RefreshProvincePanel();
                        RefreshTreasury();
                    };
                    grid.AddChild(button);
                    // Hurried: a quarter faster at triple the silver and workers (your answer "Hurrying a wonder").
                    var fast = MapView.WonderPrice(w, 1);
                    string? fastProblem = _map.CanBuildWonder(p, w, null, 1);
                    var hurry = new Button
                    {
                        Text = $"Hurried: {fast.Years} years ({fast.Silver:0})",
                        Disabled = fastProblem != null,
                        FocusMode = FocusModeEnum.None,
                        TooltipText = $"Build the {w.Name} a quarter faster: {_map.Money(fast.Silver)} and {fast.Workers:N0} workers, {fast.Years} years instead of {w.Years}." +
                            (fastProblem != null ? "\n" + fastProblem : ""),
                    };
                    hurry.AddThemeFontSizeOverride("font_size", 14);
                    hurry.Pressed += () =>
                    {
                        _map.StartWonder(p, def, null, 1);
                        RefreshProvincePanel();
                        RefreshTreasury();
                    };
                    grid.AddChild(hurry);
                }
            }
        }

        // Roads through this province, and drawing new ones.
        var roadsHere = _map.Game.Roads.Where(r => r.Nodes.Any(n => _map.ProvinceOfNodePublic(n) == p.Id)).ToList();
        foreach (var road in roadsHere)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            buildings.AddChild(row);
            var rk = RoadCatalog.Instance[road.Kind];
            var label = ThemeAncient.Label($"{(rk?.Water == true ? "Canal" : rk?.Wall == true ? (road.Manned || !road.Built ? "Wall" : "Wall, unmanned") : "Road")}: {road.Name} ({road.Km:0} km" + (road.Built ? "" : $", {road.Left} year(s) to build") +
                (road.Broken ? ", broken" : "") + (road.Realm != _map.PlayerRealmId ? $", {_map.RealmName(road.Realm)}" : "") + ")", fontSize: 14);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            if (road.Realm != _map.PlayerRealmId || !road.Built)
                continue;
            var r = road;
            var act = new Button
            {
                Text = road.Broken ? $"Repair ({_map.RepairCost(road):0})" : "Break",
                FocusMode = FocusModeEnum.None,
                TooltipText = road.Broken ? "Mend it in a year." : "Break your own road to slow an invader (everyone marches faster on a road, enemies too).",
            };
            act.AddThemeFontSizeOverride("font_size", 13);
            act.Pressed += () =>
            {
                string? why = r.Broken ? _map.RepairRoad(r) : _map.BreakRoad(r);
                if (why != null)
                    ShowStatus(why, fadeAfter: 3);
                RefreshProvincePanel();
                RefreshTreasury();
            };
            row.AddChild(act);
        }
        if (mine)
        {
            var roadRow = new HBoxContainer();
            roadRow.AddThemeConstantOverride("separation", 6);
            buildings.AddChild(roadRow);
            roadRow.AddChild(ThemeAncient.Label("Draw a road:", fontSize: 14));
            foreach (var kind in RoadCatalog.Instance.Open(_map.PlayerState))
            {
                var k = kind;
                var draw = new Button
                {
                    Text = kind.Name,
                    FocusMode = FocusModeEnum.None,
                    TooltipText = $"{kind.Description}\n{_map.Money(kind.Cost)} a km (more over rough ground, {_map.Money(RoadCatalog.Instance.BridgeCost)} a bridge), {kind.Workers:0} workers a km; " +
                        $"marching {(1 - kind.Pace):P0} faster, trade +{kind.Trade:P0} in the provinces it crosses.\nClick one of your towns, then another.",
                };
                draw.AddThemeFontSizeOverride("font_size", 13);
                draw.Pressed += () => _map.StartDrawingRoad(k);
                roadRow.AddChild(draw);
            }
            var wallRow = new HBoxContainer();
            wallRow.AddThemeConstantOverride("separation", 6);
            buildings.AddChild(wallRow);
            wallRow.AddChild(ThemeAncient.Label("Draw a frontier wall:", fontSize: 14));
            foreach (var kind in RoadCatalog.Instance.Walls)
            {
                var k = kind;
                var wall = new Button
                {
                    Text = kind.Name,
                    FocusMode = FocusModeEnum.None,
                    TooltipText = $"{kind.Description}\n{_map.Money(kind.Cost)} a km (more over rough ground), {kind.Workers:0} workers a km while it is built, then {kind.Garrison:0} men a km to man it " +
                        $"(unmanned it decays). Armies of other realms lose {kind.CrossKm:0} km of their march crossing it; provinces along it are better held." +
                        "\nClick your land where it starts, then along your border.",
                };
                wall.AddThemeFontSizeOverride("font_size", 13);
                wall.Pressed += () => _map.StartDrawingRoad(k);
                wallRow.AddChild(wall);
            }
            var canals = RoadCatalog.Instance.OpenCanals(_map.PlayerState).ToList();
            if (canals.Count > 0)
            {
                var canalRow = new HBoxContainer();
                canalRow.AddThemeConstantOverride("separation", 6);
                buildings.AddChild(canalRow);
                canalRow.AddChild(ThemeAncient.Label("Dig a canal:", fontSize: 14));
                foreach (var kind in canals)
                {
                    var k = kind;
                    var dig = new Button
                    {
                        Text = kind.Name,
                        FocusMode = FocusModeEnum.None,
                        TooltipText = $"{kind.Description}\n{_map.Money(kind.Cost)} a km, {kind.Workers:0} workers a km; trade +{kind.Trade:P0} in the provinces it crosses; your ships sail through it." +
                            "\nClick your land by the sea or a river, then by another water.",
                    };
                    dig.AddThemeFontSizeOverride("font_size", 13);
                    dig.Pressed += () => _map.StartDrawingRoad(k);
                    canalRow.AddChild(dig);
                }
            }
        }

        if (mine)
        {
            Line(buildings, $"Builds {slots} at a time (a town of 50,000 builds two, of 200,000 three); up to {MapView.MaxQueue} in the queue.");
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 4);
            buildings.AddChild(grid);
            foreach (var b in cat.All)
            {
                string? problem = _map.CanBuild(p, b);
                // Only what this land allows and the realm knows: the rest would only clutter the list.
                if (problem != null && !problem.StartsWith("Not enough silver") && !problem.StartsWith("The queue is full"))
                    continue;
                int next = MapView.NextLevel(ps, b);
                var button = new Button
                {
                    Text = $"{b.LevelName(next)} ({b.CostOf(next):0})",
                    Disabled = problem != null,
                    FocusMode = FocusModeEnum.None,
                    TooltipText = $"{b.Description}\n{_map.Money(b.CostOf(next))}, {b.Years} years, upkeep {_map.Money(b.Upkeep)} a year for each level." +
                        (b.Max > 1 ? $"\nLevels: {string.Join(" → ", Enumerable.Range(1, b.Max).Select(b.LevelName))}." : "") +
                        (problem != null ? "\n" + problem : ""),
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
        else if (key.Keycode == Key.Space)
            SetPlaying(!_playing);
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
        else if (key.Keycode == Key.L)
            ToggleRealmsPanel();
        else
            return;
        GetViewport().SetInputAsHandled();
    }
}
