using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The armies panel (decision "Playable 9": named armies based in
/// provinces): each army with where it stands, its strength and weariness,
/// its sieges, and its units; raise units into it, send some home, move it
/// within your land, choose it for conquest, or found a new army.
/// </summary>
public partial class Hud
{
    PanelContainer _armiesPanel = null!;
    VBoxContainer _armyRows = null!;
    Label _armiesSummary = null!;

    void BuildArmiesButton(HBoxContainer row)
    {
        var button = new Button
        {
            Flat = true,
            Text = "Armies",
            Icon = ThemeAncient.Icon("army"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Your armies and their units",
            ExpandIcon = false,
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += () => ToggleArmiesPanel();
        row.AddChild(button);
    }

    void BuildArmiesPanel()
    {
        _armiesPanel = SidePanel("Armies", () => ToggleArmiesPanel(false), 660);
        var box = _armiesPanel.GetNode<VBoxContainer>("Box");
        _armiesSummary = ThemeAncient.Label("", fontSize: 16);
        _armiesSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _armiesSummary.CustomMinimumSize = new Vector2(620, 0);
        box.AddChild(_armiesSummary);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640, 600), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _armyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _armyRows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_armyRows);
        var add = new Button { Text = "Found a new army at your capital", FocusMode = FocusModeEnum.None };
        add.Pressed += () =>
        {
            var s = _map.PlayerState;
            s.NewArmy(_map.NextArmyName(_map.PlayerRealmId), _map.CapitalNode(_map.PlayerRealmId));
            _map.RefreshArmyMarkers();
            RefreshArmiesPanel();
        };
        box.AddChild(add);
        var mercs = new Button { Text = "Mercenary companies", FocusMode = FocusModeEnum.None,
            TooltipText = "Companies waiting at their hiring grounds, and those in anyone's pay." };
        mercs.Pressed += () =>
        {
            _mercMode = !_mercMode;
            mercs.Text = _mercMode ? "Back to your armies" : "Mercenary companies";
            RefreshArmiesPanel();
        };
        box.AddChild(mercs);
        var reports = new Button { Text = "Battle reports", FocusMode = FocusModeEnum.None };
        reports.Pressed += ShowBattles;
        box.AddChild(reports);
    }

    void ToggleArmiesPanel(bool? show = null)
    {
        _armiesPanel.Visible = show ?? !_armiesPanel.Visible;
        if (_armiesPanel.Visible)
        {
            ToggleRealmPanel(false);
            _goodsPanel.Visible = false;
            _diplomacyPanel.Visible = false;
            _goalsPanel.Visible = false;
            _guidePanel.Visible = false;
            _researchPanel.Visible = false;
            RefreshArmiesPanel();
        }
        RefreshMapView();
    }

    void RefreshArmiesPanel()
    {
        if (!_armiesPanel.Visible)
            return;
        foreach (Node child in _armyRows.GetChildren())
            child.QueueFree();
        if (_mercMode)
        {
            RefreshMercenaries();
            return;
        }
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        var cultures = _map.CulturesOf(_map.PlayerRealmId);
        var cat = UnitCatalog.Instance;
        _armiesSummary.Text =
            $"Might: land {Military.Might(s, Domain.Land):0.0}, sea {Military.Might(s, Domain.Naval):0.0}.  " +
            $"Under arms: {ThemeAncient.GroupThousands(Military.Soldiers(s))} men.  " +
            $"Men who can be called up: {ThemeAncient.GroupThousands((long)s.Manpower)}.\n" +
            "The army marked for conquest is the one Conquest mode (key 3) paints for: its reach is where it can march this year. " +
            "Armies march month by month along the route you plot; winter, mountains, marsh and forest slow them. Beyond your land they live on supply lines near your borders, on what they forage, then on their baggage (up to 4 months)." +
            (_map.MovingArmyId != 0 ? "\nClick where the army should march; hold Shift to add stops on the way." : "");
        AddTraditionsRow(s);
        if (s.VeteransWaiting >= 1)
            AddVeteransRow(s);
        var selected = _map.SelectedArmy;
        foreach (var army in s.Armies.ToList())
        {
            var frame = new PanelContainer { ThemeTypeVariation = "GlassPanel" };
            _armyRows.AddChild(frame);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 3);
            frame.AddChild(box);
            var sieges = _map.Game.Sieges.Where(x => x.Attacker == _map.PlayerRealmId && x.ArmyId == army.Id).ToList();
            var head = ThemeAncient.Label(
                $"{(army == selected ? "▶ " : "")}{army.Name}: at {_map.PlaceOfNode(army.Node)}", "HeaderLabel", 18);
            box.AddChild(head);
            box.AddChild(ThemeAncient.Label(
                $"Might: land {Military.Might(army, Domain.Land):0.0}, sea {Military.Might(army, Domain.Naval):0.0};  " +
                $"{ThemeAncient.GroupThousands(Military.Soldiers(army))} men;  upkeep {Military.Upkeep(army) * s.UpkeepShare:0} a year" +
                (army.Fatigue > 0.05 ? $";  weary {army.Fatigue:P0}" : ""), fontSize: 15));
            if (army.Experience > 0.02)
                box.AddChild(ThemeAncient.Label($"Veterans: {army.Experience:P0} (fights {Battle.VeteranBonus * army.Experience:P0} better)", fontSize: 15));
            AddGeneralRow(box, s, army);
            if (army.GeneralId != 0 && army.CompanyId == 0 && army.GeneralLoyalty >= 0.05)
            {
                var loyal = ThemeAncient.Label($"Loyal to its general: {army.GeneralLoyalty:P0} (fights {ArmyLoyalty.MoraleBonus * army.GeneralLoyalty:P0} better)" +
                    (army.GeneralLoyalty >= ArmyLoyalty.MutinyFrom ? ".  His soldiers now, more than yours." : ""), fontSize: 15);
                loyal.TooltipText = "An army grows loyal to the general who leads it: fully in five years, sooner with victories and loot. " +
                    "A loyal army fights better, but if you owe it pay, leave your veterans without land or the rival faction is strong, " +
                    "it may follow him against you, as Sulla's legions did. Changing its general starts the loyalty over.";
                loyal.MouseFilter = MouseFilterEnum.Pass;
                if (army.GeneralLoyalty >= ArmyLoyalty.MutinyFrom)
                    loyal.Modulate = new Color(1f, 0.8f, 0.6f);
                box.AddChild(loyal);
            }
            string supply = army.Supply >= 3.5 ? "full" : army.Supply <= 0.01 ? "empty: the army is starving" : $"{army.Supply:0.0} months of food";
            box.AddChild(ThemeAncient.Label((army.Marching
                ? $"Marching to {_map.PlaceOfNode(army.Route[^1])}: about {Math.Ceiling(_map.RouteMonths(s, army)):0} month(s) more.  "
                : "Standing.  ") + $"Baggage: {supply}.", fontSize: 15));
            foreach (var siege in sieges)
            {
                var siegeRow = new HBoxContainer();
                siegeRow.AddThemeConstantOverride("separation", 8);
                box.AddChild(siegeRow);
                siegeRow.AddChild(ThemeAncient.Label($"Besieging {siege.Name}: {Math.Min(siege.Progress, 0.99):P0} done.  When it falls:", fontSize: 15));
                var sack = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = Sacking.Help[(int)siege.Choice] };
                for (int i = 0; i < Sacking.Names.Length; i++)
                    sack.AddItem(Sacking.Names[i], i);
                sack.Select((int)siege.Choice);
                var thisSiege = siege;
                sack.ItemSelected += index =>
                {
                    thisSiege.Choice = (SackChoice)(int)index;
                    sack.TooltipText = Sacking.Help[(int)index];
                };
                siegeRow.AddChild(sack);
            }

            var buttons = new HBoxContainer();
            buttons.AddThemeConstantOverride("separation", 6);
            box.AddChild(buttons);
            var pick = new Button { Text = army == selected ? "Chosen for conquest" : "Choose for conquest", Disabled = army == selected, FocusMode = FocusModeEnum.None };
            int id = army.Id;
            pick.Pressed += () =>
            {
                _map.SelectedArmyId = id;
                _map.ClearConquestPlan();
                RefreshArmiesPanel();
            };
            buttons.AddChild(pick);
            var move = new Button
            {
                Text = _map.MovingArmyId == army.Id ? "Click the map..." : "Move",
                FocusMode = FocusModeEnum.None,
                Disabled = sieges.Count > 0,
                TooltipText = sieges.Count > 0 ? "Lift the siege first." : "Then click where it should march (Shift-click to add stops).",
            };
            move.Pressed += () =>
            {
                _map.MovingArmyId = _map.MovingArmyId == id ? 0 : id;
                RefreshArmiesPanel();
            };
            buttons.AddChild(move);
            if (army.Marching)
            {
                var halt = new Button { Text = "Halt", FocusMode = FocusModeEnum.None, TooltipText = "Stop where it stands." };
                halt.Pressed += () =>
                {
                    _map.HaltArmy(army);
                    RefreshArmiesPanel();
                };
                buttons.AddChild(halt);
            }
            if (sieges.Count > 0)
            {
                var lift = new Button { Text = "Lift siege", FocusMode = FocusModeEnum.None };
                lift.Pressed += () =>
                {
                    _map.LiftSieges(army);
                    RefreshArmiesPanel();
                };
                buttons.AddChild(lift);
            }
            var raise = new MenuButton { Text = "Raise units ▾", FocusMode = FocusModeEnum.None, Flat = false };
            var popup = raise.GetPopup();
            var options = cat.Available(cultures).OrderBy(u => u.Role).ThenByDescending(u => u.Might).ToList();
            foreach (var u in options)
            {
                var (problem, cost) = Military.CanRecruit(s, c, cultures, u, s.ElephantSource);
                popup.AddItem($"{u.Name} ({UnitRoles.Names[u.Role].ToLowerInvariant()}): {_map.Money(cost)}", u.Index);
                int item = popup.ItemCount - 1;
                popup.SetItemDisabled(item, problem != null);
                popup.SetItemTooltip(item, (problem ?? $"Might {u.Might:0.00}, {u.Men:N0} men, upkeep {u.Upkeep:0} a year.") +
                    "\n" + u.Description);
            }
            popup.IdPressed += unitIndex =>
            {
                Military.Recruit(s, _map.CensusOf(_map.PlayerRealmId), cultures, cat[(int)unitIndex], army, s.ElephantSource, _map.RecruitOrigin(_map.PlayerRealmId, army));
                RefreshArmiesPanel();
                RefreshTreasury();
            };
            buttons.AddChild(raise);

            // The roster (decision "Seeing an army's units"): each unit's men, wounded, experience and home.
            foreach (var reg in army.Regiments.OrderBy(r => cat[r.Type].Role).ThenBy(r => r.Type).ToList())
            {
                var u = cat[reg.Type];
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", 4);
                box.AddChild(line);
                string home = _map.ProvinceName(reg.Origin);
                var name = ThemeAncient.Label(
                    $"   {u.Name}: {reg.Men:N0} men" + (reg.Wounded > 0 ? $" (+{reg.Wounded:N0} wounded)" : "") +
                    $"  ·  {(reg.Xp >= 0.02 ? $"veterans {reg.Xp:P0}" : "raw")}" + (home != "" ? $"  ·  from {home}" : ""), fontSize: 15);
                name.TooltipText = $"{UnitRoles.Names[u.Role]}. Might {u.Might:0.00} for every {u.Men:N0} men.\n{u.Description}\n" +
                    "A unit holds 100 to 5,000 men. An army keeps an even number of units: splitting or merging also splits or merges another to keep it even.";
                name.MouseFilter = MouseFilterEnum.Pass;
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                line.AddChild(name);
                var split = new Button { Text = "Split", FocusMode = FocusModeEnum.None, Disabled = reg.Men < 2 * Regiment.MinMen,
                    TooltipText = "Halve this unit." };
                split.AddThemeFontSizeOverride("font_size", 13);
                split.Pressed += () =>
                {
                    army.Split(reg);
                    army.Even();
                    RefreshArmiesPanel();
                };
                line.AddChild(split);
                var partner = army.Regiments.Where(r => r != reg && r.Type == reg.Type && r.Men + reg.Men <= Regiment.MaxMen).OrderBy(r => r.Men).FirstOrDefault();
                var merge = new Button { Text = "Merge", FocusMode = FocusModeEnum.None, Disabled = partner == null,
                    TooltipText = partner == null ? "No other unit of this kind small enough to join it (5,000 men at most)." : $"Join the {partner.Men:N0}-man unit of the same kind; experience averaged by men." };
                merge.AddThemeFontSizeOverride("font_size", 13);
                merge.Pressed += () =>
                {
                    if (partner != null && army.Merge(reg, partner))
                        army.Even();
                    RefreshArmiesPanel();
                };
                line.AddChild(merge);
                var send = new Button { Text = "−", FocusMode = FocusModeEnum.None, TooltipText = "Send this unit home: its men return to the pool." };
                send.Pressed += () =>
                {
                    Military.Disband(s, army, reg, _map.DemoYear);
                    RefreshArmiesPanel();
                };
                line.AddChild(send);
                if (reg.Xp >= Guards.FamousXp && !cat[reg.Type].Guard && cat[reg.Type].Domain == Domain.Land)
                {
                    string? cannot = _map.CanMakeGuard(army, reg);
                    var guard = new Button
                    {
                        Text = "Guard", FocusMode = FocusModeEnum.None, Disabled = cannot != null,
                        TooltipText = cannot ?? "Make these famous veterans your guard, as Alexander's hypaspists became the Silver Shields: " +
                            "they fight as your people's guard unit, and an army with your guard in it is half as likely to turn on you.",
                    };
                    guard.Pressed += () =>
                    {
                        if (_map.MakeGuard(army, reg))
                            RefreshArmiesPanel();
                    };
                    line.AddChild(guard);
                }
            }
        }
    }

    /// <summary>Veterans waiting for land: settle them in a province, or pay them off if they offered.</summary>
    void AddVeteransRow(RealmState s)
    {
        var frame = new PanelContainer { ThemeTypeVariation = "GlassPanel" };
        _armyRows.AddChild(frame);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        frame.AddChild(box);
        int years = Math.Max(0, _map.DemoYear - s.VeteransSince);
        box.AddChild(ThemeAncient.Label($"{s.VeteransWaiting:N0} veterans await land" + (years > 0 ? $" ({years} year(s) now)" : "") + ".", "HeaderLabel", 17));
        box.AddChild(ThemeAncient.Label("Settled on conquered land they hold it for you; on your own people's land, farms are taken from their owners. " +
            "Left waiting they stir unrest and may follow a rival.", fontSize: 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);
        var where = new OptionButton { FocusMode = FocusModeEnum.None };
        var mine = _map.Provinces?.Provinces.Values.Where(p => p.RealmId == _map.PlayerRealmId)
            .OrderBy(p => _map.ProvinceStateOf(p.Id).Integration).ThenBy(p => p.Name).ToList() ?? new();
        foreach (var p in mine)
        {
            var ps = _map.ProvinceStateOf(p.Id);
            where.AddItem(ps.Integration < 1 ? $"{p.Name} (conquered, {ps.Integration:P0} settled)" : p.Name, p.Id);
        }
        row.AddChild(where);
        var settle = new Button { Text = "Give them land there", FocusMode = FocusModeEnum.None, Disabled = mine.Count == 0 };
        settle.Pressed += () =>
        {
            int id = where.GetItemId(where.Selected);
            if (_map.SettleVeterans(id))
                RefreshArmiesPanel();
        };
        row.AddChild(settle);
        if (s.VeteransOfferSilver)
        {
            double cost = ArmyLoyalty.PayOffCost(s.VeteransWaiting);
            var pay = new Button
            {
                Text = $"Pay them off instead ({cost:N0} talents)",
                Disabled = s.Treasury < cost,
                TooltipText = "They have offered to take silver instead of land.",
                FocusMode = FocusModeEnum.None,
            };
            pay.Pressed += () =>
            {
                if (_map.PayOffVeterans())
                {
                    RefreshArmiesPanel();
                    RefreshTreasury();
                }
            };
            row.AddChild(pay);
        }
    }

    /// <summary>The realm's army traditions, what they do, and how near the next earned one is.</summary>
    void AddTraditionsRow(RealmState s)
    {
        var cat = TraditionCatalog.Instance;
        var held = s.Traditions.Keys.Select(id => cat[id]).Where(t => t != null).ToList();
        string names = held.Count > 0 ? string.Join(", ", held.Select(t => t!.Name)) : "none yet";
        int best = Array.IndexOf(s.ArmWins, s.ArmWins.Max());
        string next = s.ArmWins[best] > 0
            ? $"  Toward {cat.EarnedFor(UnitRoles.Keys[best])?.Name.ToLowerInvariant()}: {s.ArmWins[best]} of {TraditionCatalog.VictoriesToEarn} victories."
            : "";
        var label = ThemeAncient.Label($"Traditions: {names}.  Guards: {Guards.Count(s)} of {Guards.Max}.{next}", fontSize: 15);
        label.TooltipText = (held.Count > 0
            ? string.Join("\n", held.Select(t => $"{t!.Name}: {t.Text} ({DescribeTradition(t)}; last used {ThemeAncient.YearText(s.Traditions[t.Id])})")) + "\n\n"
            : "") +
            $"Traditions come from your people's history and from your wars: {TraditionCatalog.VictoriesToEarn} victories won chiefly by one arm earn its tradition, " +
            $"{TraditionCatalog.CitiesToEarn} cities taken earn siegecraft. A tradition unused for {TraditionCatalog.FadeYears} years is forgotten, " +
            "and a crushing defeat breaks the tradition of the arm that was routed.";
        label.MouseFilter = MouseFilterEnum.Pass;
        _armyRows.AddChild(label);
    }

    static string DescribeTradition(TraditionDef t) => string.Join(", ", t.Effects.Select(e =>
        e.Key.StartsWith("might_") ? $"{UnitRoles.Names[Array.IndexOf(UnitRoles.Keys, e.Key[6..])].ToLowerInvariant()} +{e.Value:P0}"
        : e.Key.StartsWith("skill_") ? $"new generals +{e.Value:0} {e.Key[6..]}"
        : e.Key == "siege" ? $"sieges +{e.Value:P0} faster" : e.Key));
}
