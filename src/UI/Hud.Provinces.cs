using Facsimilia.Game;
using System;
using System.Collections.Generic;
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
    Button _splitProvince = null!;
    OptionButton _joinProvince = null!;
    int _joinedFor;

    static readonly (MapMode Mode, string Text, string Tip)[] Modes =
    {
        (MapMode.Inspect, "Inspect", "Click a province to see it; drag to move the map"),
        (MapMode.EditProvinces, "Provinces", "Redraw your provinces with the brush"),
        (MapMode.PlanConquest, "Conquest", "Paint land to attack; fights are decided when the turn ends"),
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
                TooltipText = tip + KeyBindings.Hint(new[] { "mode_inspect", "mode_provinces", "mode_conquest" }[i]),
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(96, 40),
            };
            b.Pressed += () => _map.SetMode(mode);
            row.AddChild(b);
            _modeButtons[i] = b;
        }
        row.AddChild(BuildMapViewMenu());
        row.AddChild(BuildFavouriteStar());
        var favourites = BuildFavouritesBar();
        row.AddChild(favourites);
        row.MoveChild(favourites, 0);
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
        header.AddChild(IconButton(null, "📌", "Pin this province to the outliner (or unpin it)", () => TogglePin($"province:{_map.SelectedProvinceId}")));
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
        foreach (var name in new[] { "People", "Land", "Buildings", "Military", "History" })
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
        _splitProvince = new Button { Text = "Split", FocusMode = FocusModeEnum.None,
            TooltipText = $"Split this province in two across its longest side. Both are restless for a while (+{MapView.RedrawUnrest:P0} unrest)." };
        _splitProvince.Pressed += () =>
        {
            var sel = _map.SelectedProvince;
            if (sel == null)
                return;
            if (_map.CanSplit(sel) is { } why)
                ShowStatus(why, fadeAfter: 3);
            else
                _map.SplitProvince(sel.Id);
            _joinedFor = 0;
            RefreshProvincePanel();
        };
        buttons.AddChild(_splitProvince);
        _joinProvince = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = $"Join a bordering province of yours into this one. Both are restless for a while (+{MapView.RedrawUnrest:P0} unrest)." };
        _joinProvince.ItemSelected += index =>
        {
            var sel = _map.SelectedProvince;
            int other = _joinProvince.GetItemId((int)index);
            if (sel != null && other > 0 && _map.JoinProvince(sel.Id, other) is { } why)
                ShowStatus(why, fadeAfter: 3);
            _joinedFor = 0;
            RefreshProvincePanel();
        };
        buttons.AddChild(_joinProvince);

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
                  (_map.ProvinceSeat(p.Id) is { } seat ? $"\nSeat: {seat}" : "") +
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
        _splitProvince.Visible = mine && !editing;
        _joinProvince.Visible = mine && !editing;
        if (mine && !editing && p != null && _joinedFor != p.Id)
        {
            _joinedFor = p.Id;
            _joinProvince.Clear();
            _joinProvince.AddItem("Join with...", 0);
            foreach (int n in _map.Neighbours(p.Id))
                if (_map.Provinces!.Provinces.TryGetValue(n, out var np) && np.RealmId == _map.PlayerRealmId)
                    _joinProvince.AddItem(np.Name, n);
            _joinProvince.Select(0);
        }
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
        var history = _provinceTabs.GetNode<VBoxContainer>("History");
        foreach (var page in new Node[] { people, land, buildings, military, history })
            Clear(page);
        var ps = _map.ProvinceStateOf(p.Id);

        // Graphs (the Ledger: "Province graphs"): its people, unrest and integration over the years.
        if (ps.Records.Count >= 2)
        {
            foreach (var (title, color, pick) in new (string, Color, Func<(int Year, float People, float Unrest, float Integration), float>)[]
                     {
                         ("People", new Color(0.95f, 0.8f, 0.45f), r => r.People),
                         ("Unrest", new Color(0.9f, 0.35f, 0.3f), r => r.Unrest),
                         ("Integration", new Color(0.45f, 0.8f, 0.55f), r => r.Integration),
                     })
            {
                var last = pick(ps.Records[^1]);
                Line(history, title == "People" ? $"{title}: {last:N0}" : $"{title}: {last:P0}", 14);
                var g = new RealmGraph { CustomMinimumSize = new Vector2(400, 70) };
                g.Series = new() { (color, false, ps.Records.Select(r => new Vector2(r.Year, pick(r))).ToList()) };
                history.AddChild(g);
            }
        }
        // History: what happened here, newest first.
        if (ps.History.Count == 0)
            Line(history, $"Nothing recorded yet. Held by {_map.RealmName(p.RealmId)}.");
        foreach (var (year, text) in ps.History.AsEnumerable().Reverse().Take(80))
            Line(history, $"{ThemeAncient.YearText(year)}: {text}", 14);

        // Focus (the Ledger: "A province's focus"): turning its work slowly.
        var focus = MapView.FocusById(ps.Focus);
        Line(land, focus != null
            ? $"Focus: {focus.Name}, {ps.FocusLevel:P0} taken hold ({focus.Help} At full, other work yields {MapView.FocusCost:P0} less.)"
            : "No focus: its people work as the land suits them.");
        if (mine)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            land.AddChild(row);
            foreach (var f in MapView.Focuses.Prepend(new ProvinceFocus("", "None", Array.Empty<string>(), "", 0, "No focus.")))
            {
                var fid = f.Id;
                var b = new Button { Text = f.Name, ToggleMode = true, ButtonPressed = ps.Focus == f.Id, FocusMode = FocusModeEnum.None,
                    TooltipText = f.Help + (f.Id != "" ? $" It takes hold over ten years; changing it starts again." : "") };
                b.AddThemeFontSizeOverride("font_size", 13);
                b.Pressed += () => { _map.SetFocus(p.Id, fid); RefreshProvincePanel(); };
                row.AddChild(b);
            }
        }

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
        if (mine)
        {
            var me = _map.PlayerState;
            if (me.MovingCapitalYears > 0 && me.MovingCapitalTo == p.Id)
                Line(land, $"Your new capital is being built at {me.MovingCapitalName}: the court arrives in {me.MovingCapitalYears} year(s).");
            else if (_map.CapitalProvince(_map.PlayerRealmId) != p.Id && _map.SeatPlace(p.Id) is { } seat)
            {
                string? cannot = _map.CanMoveCapital(p.Id);
                var move = new Button
                {
                    Text = $"Make {seat.Name.TrimStart('★', ' ')} your capital ({_map.CapitalMoveCost():0} talents, {CapitalMove.Years} years)",
                    Disabled = cannot != null,
                    TooltipText = cannot ?? "The court, treasury and offices move here (Constantine took six years over Constantinople). " +
                        "When it is done, your reach centres on the new capital; the old capital resents it and the court's old families grumble.",
                    FocusMode = FocusModeEnum.None,
                };
                int cid = p.Id;
                move.Pressed += () =>
                {
                    _map.StartCapitalMove(cid);
                    RefreshProvincePanel();
                    RefreshTreasury();
                };
                land.AddChild(move);
            }
        }
        if (_map.RuinIn(p.Id) is { } ruin)
        {
            Line(land, $"The ruins of {ruin.Name}, razed by {_map.RealmName(ruin.By)} in {ThemeAncient.YearText(ruin.Year)}.");
            if (mine)
            {
                string? cannot = _map.CanResettle(p.Id);
                var resettle = new Button
                {
                    Text = $"Resettle {ruin.Name} ({Sacking.ResettleCost:0} talents, {Sacking.ResettleMen:N0} settlers)",
                    Disabled = cannot != null,
                    TooltipText = cannot ?? "Send settlers to build on the ruins, as Caesar refounded Carthage. Its people's grudge eases.",
                    FocusMode = FocusModeEnum.None,
                };
                int rid = p.Id;
                resettle.Pressed += () =>
                {
                    _map.Resettle(rid);
                    RefreshProvincePanel();
                    RefreshTreasury();
                };
                land.AddChild(resettle);
            }
        }
        if (_map.Reserves(p.Id) is { Men: > 0 } reserve)
        {
            Line(land, $"{reserve.Men:N0} veterans farm here who could march again (hardened {reserve.Xp:P0}).");
            if (mine)
            {
                string? cannot = _map.CanCallReserves(p.Id);
                var call = new Button
                {
                    Text = $"Call them back to arms ({reserve.Men:N0} men)",
                    Disabled = cannot != null,
                    TooltipText = cannot ?? "Rome's evocati: they march out as a new army of hardened heavy infantry. The colony resents losing them " +
                        "from the fields (more unrest), and when they are discharged again they will want land again.",
                    FocusMode = FocusModeEnum.None,
                };
                int vid = p.Id;
                call.Pressed += () =>
                {
                    if (_map.CallReserves(vid) != null)
                    {
                        _map.RefreshArmyMarkers();
                        RefreshProvincePanel();
                        RefreshArmiesPanel();
                    }
                };
                land.AddChild(call);
            }
        }
        if (region != null && _map.Population is { } popEngine)
        {
            Line(land, GrowthBreakdown(popEngine, region.Id, region.Name));
            double ravaged = _map.RavagedOf(region.Id);
            if (ravaged >= Security.Recovered)
                Line(land, $"Ravaged by war: {ravaged:P0}. Burned fields and fled farmers; it fades by about an eighth a year.");
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
        switch (KeyBindings.ActionFor(key.Keycode))
        {
            case "advance": EmitSignal(SignalName.AdvanceRequested); break;
            case "play": SetPlaying(!_playing); break;
            case "mode_inspect": _map.SetMode(Modes[0].Mode); break;
            case "mode_provinces": _map.SetMode(Modes[1].Mode); break;
            case "mode_conquest": _map.SetMode(Modes[2].Mode); break;
            case "panel_goods": ToggleGoodsPanel(); break;
            case "panel_research": ToggleResearchPanel(); break;
            case "panel_realms": ToggleRealmsPanel(); break;
            case "panel_chronicle":
                if (_bookPanel is { Visible: true })
                    _bookPanel.Visible = false;
                else
                    ShowChronicleBook();
                break;
            case { } id when KeyBindings.ViewOf.TryGetValue(id, out var view)
                             && (view != MapView.RegionsView || _map.RegionsAvailable):
                _map.SetMapView(view);
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Why a region grows faster or slower than history had it (your answer "Seeing why a region grows").</summary>
    static string GrowthBreakdown(PopulationEngine pop, int region, string name)
    {
        (GrowthFactor F, string Words)[] factors =
        {
            (GrowthFactor.Food, "food"), (GrowthFactor.Climate, "this year's weather"), (GrowthFactor.Health, "health"),
            (GrowthFactor.Burden, "taxes and burdens"), (GrowthFactor.Security, "war"),
        };
        var helps = new List<string>();
        var slows = new List<string>();
        foreach (var (f, words) in factors)
        {
            float score = pop.GetFactor(region, f);
            if (Math.Abs(score) < 0.05f)
                continue;
            string what = f == GrowthFactor.Security && score > 0 ? "long peace" : words;
            string strength = Math.Abs(score) >= 0.6f ? "strongly" : Math.Abs(score) >= 0.25f ? "" : "slightly";
            (score > 0 ? helps : slows).Add(strength.Length > 0 ? $"{what} ({strength})" : what);
        }
        if (helps.Count == 0 && slows.Count == 0)
            return $"Growth in {name}: as history had it.";
        return $"Growth in {name}: " + string.Join("; ", new[]
        {
            helps.Count > 0 ? "helped by " + string.Join(", ", helps) : "",
            slows.Count > 0 ? "slowed by " + string.Join(", ", slows) : "",
        }.Where(x => x.Length > 0)) + ".";
    }
}
