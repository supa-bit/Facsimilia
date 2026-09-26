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
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        var cultures = _map.CulturesOf(_map.PlayerRealmId);
        var cat = UnitCatalog.Instance;
        _armiesSummary.Text =
            $"Might: land {Military.Might(s, Domain.Land):0.0}, sea {Military.Might(s, Domain.Naval):0.0}.  " +
            $"Under arms: {ThemeAncient.GroupThousands(Military.Soldiers(s))} men.  " +
            $"Men who can be called up: {ThemeAncient.GroupThousands((long)s.Manpower)}.\n" +
            "The army marked for conquest is the one Conquest mode (key 3) paints for: its reach is where it can march this year. " +
            (_map.MovingArmyId != 0 ? "\nClick a place in your land to move the army there." : "");
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
            foreach (var siege in sieges)
                box.AddChild(ThemeAncient.Label($"Besieging {siege.Name}: {Math.Min(siege.Progress, 0.99):P0} done", fontSize: 15));

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
                TooltipText = sieges.Count > 0 ? "Lift the siege first." : "Then click a place in your land.",
            };
            move.Pressed += () =>
            {
                _map.MovingArmyId = _map.MovingArmyId == id ? 0 : id;
                RefreshArmiesPanel();
            };
            buttons.AddChild(move);
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
                popup.AddItem($"{u.Name} ({UnitRoles.Names[u.Role].ToLowerInvariant()}): {cost:0} talents" +
                    (Military.NeedsMercenaries(s, u) ? ", hired" : ""), u.Index);
                int item = popup.ItemCount - 1;
                popup.SetItemDisabled(item, problem != null);
                popup.SetItemTooltip(item, (problem ?? $"Might {u.Might:0.00}, {u.Men:N0} men, upkeep {u.Upkeep:0} a year.") +
                    "\n" + u.Description);
            }
            popup.IdPressed += unitIndex =>
            {
                Military.Recruit(s, _map.CensusOf(_map.PlayerRealmId), cultures, cat[(int)unitIndex], army, s.ElephantSource);
                RefreshArmiesPanel();
                RefreshTreasury();
            };
            buttons.AddChild(raise);

            for (int i = 0; i < army.Units.Length; i++)
            {
                if (army.Units[i] == 0)
                    continue;
                var u = cat[i];
                var line = new HBoxContainer();
                box.AddChild(line);
                var name = ThemeAncient.Label($"   {army.Units[i]} × {u.Name}", fontSize: 15);
                name.TooltipText = $"{UnitRoles.Names[u.Role]}. Might {u.Might:0.00} each, {u.Men:N0} men.\n{u.Description}";
                name.MouseFilter = MouseFilterEnum.Pass;
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                line.AddChild(name);
                var home = new Button { Text = "−", FocusMode = FocusModeEnum.None, TooltipText = "Send one home: its men return to the pool." };
                home.Pressed += () =>
                {
                    Military.Disband(s, army, u);
                    RefreshArmiesPanel();
                };
                line.AddChild(home);
            }
        }
    }
}
