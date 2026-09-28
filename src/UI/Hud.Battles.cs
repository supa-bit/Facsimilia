using System;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Battle reports (decision "Next 6"): every battle your realm fought, newest
/// first: where, on what ground, who led each side, men on the field and
/// lost, the odds, and how the day went. Opens by itself after a battle of yours.
/// </summary>
public partial class Hud
{
    PanelContainer _battlesPanel = null!;
    VBoxContainer _battleRows = null!;
    int _battlesSeen = -1;   // set on the first year played, so loading a game does not open old reports

    void BuildBattlesPanel()
    {
        _battlesPanel = SidePanel("Battle reports", () => _battlesPanel.Visible = false, 620);
        _battlesPanel.OffsetLeft = _battlesPanel.OffsetRight = 700;
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(600, 560), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _battlesPanel.GetNode<VBoxContainer>("Box").AddChild(scroll);
        _battleRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _battleRows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_battleRows);
    }

    public void ShowBattles()
    {
        _battlesPanel.Visible = true;
        _battlesPanel.MoveToFront();
        RefreshBattles();
    }

    /// <summary>After a year: opens the reports when the player fought a new battle.</summary>
    void CheckNewBattles()
    {
        var mine = _map.Game.Battles.Count(IsMine);
        if (_battlesSeen < 0)
            _battlesSeen = _map.Game.Battles.Count(IsMine) - (_map.Game.Battles.Any(b => IsMine(b) && b.Year == _map.DemoYear - 1) ? 1 : 0);
        if (mine > _battlesSeen)
            ShowBattles();
        _battlesSeen = mine;
    }

    bool IsMine(BattleReport b) => b.AttackerRealm == _map.PlayerRealmId || b.DefenderRealm == _map.PlayerRealmId;

    void RefreshBattles()
    {
        if (!_battlesPanel.Visible)
            return;
        foreach (Node c in _battleRows.GetChildren())
            c.QueueFree();
        var list = _map.Game.Battles.Where(IsMine).Reverse().Take(30).ToList();
        if (list.Count == 0)
            _battleRows.AddChild(ThemeAncient.Label("Your armies have fought no battles yet.", fontSize: 16));
        foreach (var b in list)
        {
            bool won = (b.AttackerRealm == _map.PlayerRealmId) == b.AttackerWon;
            var frame = new PanelContainer { ThemeTypeVariation = "GlassPanel" };
            _battleRows.AddChild(frame);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 3);
            frame.AddChild(box);
            box.AddChild(ThemeAncient.Label($"{b.Title}: {(won ? "victory" : "defeat")}", "HeaderLabel", 18));
            string Side(string realm, string army, string gen, int men, int lost, double strength) =>
                $"{realm}: {army}" + (gen != "" ? $", led by {gen}" : "") +
                $"; {ThemeAncient.GroupThousands(men)} men, {ThemeAncient.GroupThousands(lost)} lost (strength {strength:0.0})";
            var text = ThemeAncient.Label(
                $"Ground: {b.Ground}.  Chance of the attacker: {b.Chance:P0}.\n" +
                Side(b.Attacker, b.AttackerArmy, b.AttackerGeneral, b.AttackerMen, b.AttackerLost, b.AttackerStrength) + "\n" +
                Side(b.Defender, b.DefenderArmy, b.DefenderGeneral, b.DefenderMen, b.DefenderLost, b.DefenderStrength) + "\n" +
                string.Join("\n", b.Phases.Select(p => "   " + p)), fontSize: 15);
            text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            text.CustomMinimumSize = new Vector2(570, 0);
            box.AddChild(text);
        }
    }

    /// <summary>The general line and the menu to change him, in an army's box.</summary>
    void AddGeneralRow(VBoxContainer box, RealmState s, Army army)
    {
        var g = s.GeneralOf(army);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);
        bool fleet = army.Count > 0 && Enumerable.Range(0, army.Units.Length).All(i => army.Units[i] == 0 || UnitCatalog.Instance[i].Domain == Domain.Naval);
        string title = fleet ? "Admiral" : army.CompanyId != 0 ? "Captain" : "General";
        var label = ThemeAncient.Label(g == null ? $"No {title.ToLowerInvariant()}: the {(fleet ? "fleet" : "army")} fights worse." :
            $"{title}: {g.Name} ({g.Origin}, age {_map.DemoYear - g.BornYear}; won {g.Victories} of {g.Battles})", fontSize: 15);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.MouseFilter = MouseFilterEnum.Pass;
        if (g != null)
            label.TooltipText = g.Summary() + "\n" + string.Join("\n", g.Perks.Select(p => PerkCatalog.Instance[p] is { } k ? $"{k.Name}: {k.Description}" : p))
                + "\n\n" + string.Join("\n", Enumerable.Range(0, Skills.Count).Select(i => $"{Skills.Names[i]}: {Skills.Help[i]}"));
        row.AddChild(label);
        var change = new MenuButton { Text = "Change general ▾", FocusMode = FocusModeEnum.None, Flat = false };
        var popup = change.GetPopup();
        var options = s.Generals.Where(x => x != g).ToList();
        foreach (var o in options)
            popup.AddItem($"{o.Name} ({o.Origin}{(o.ArmyId != 0 ? $", leads {s.ArmyById(o.ArmyId)?.Name}" : "")}): {o.Summary()}", o.Id);
        var family = _map.FamilyCommanders(_map.PlayerRealmId).Where(c => !s.Generals.Any(x => x.CharacterId == c.Id)).ToList();
        foreach (var c in family)
            popup.AddItem($"{c.Name} of your family, age {c.AgeIn(_map.DemoYear)} (skills unknown until he leads)", 100000 + c.Id);
        popup.AddItem($"Appoint a new commander ({_map.Money(AppointCost)})", -1);
        popup.SetItemDisabled(popup.ItemCount - 1, s.Treasury < AppointCost);
        popup.IdPressed += id =>
        {
            General? pick;
            if (id == -1)
            {
                s.Treasury -= AppointCost;
                pick = _map.AppointGeneral(s);
            }
            else if (id >= 100000)
                pick = _map.FamilyGeneral(s, (int)id - 100000);
            else
                pick = s.Generals.FirstOrDefault(x => x.Id == id);
            if (pick != null)
                _map.Assign(s, pick, army);
            RefreshArmiesPanel();
            RefreshTreasury();
        };
        row.AddChild(change);
    }

    const double AppointCost = 20;

    /// <summary>Opens the battle map for the player's battle and waits until it is fought.</summary>
    public async System.Threading.Tasks.Task FightBattle(Facsimilia.World.MapView.PendingBattle p)
    {
        var view = new TacticalView(p, _map.ColorForRealm(p.Att.RealmId), _map.ColorForRealm(p.Def.RealmId));
        AddChild(view);
        await ToSignal(view, TacticalView.SignalName.Finished);
        view.QueueFree();
    }
}
