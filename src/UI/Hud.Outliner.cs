using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The outliner (decision "Next 42"): a collapsible, floating list of what
/// needs you: offers waiting, wars, sieges, armies, provinces near revolt,
/// research, debt. Clicking a line takes you there.
/// </summary>
public partial class Hud
{
    PanelContainer _outliner = null!;
    VBoxContainer _outlinerRows = null!;
    Button _outlinerToggle = null!;
    bool _outlinerOpen = true;

    void BuildOutliner()
    {
        _outliner = new PanelContainer { ThemeTypeVariation = "GlassPanel", CustomMinimumSize = new Vector2(300, 0), MouseFilter = MouseFilterEnum.Stop };
        _outliner.SetAnchorsPreset(LayoutPreset.TopRight);
        _outliner.GrowHorizontal = GrowDirection.Begin;
        _outliner.OffsetLeft = _outliner.OffsetRight = -20;
        _outliner.OffsetTop = _outliner.OffsetBottom = 560;
        AddChild(_outliner);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        _outliner.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        var title = ThemeAncient.Label("Needs you", "HeaderLabel", 19);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        _outlinerToggle = new Button { Text = "▾", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Fold or unfold the list" };
        _outlinerToggle.Pressed += () =>
        {
            _outlinerOpen = !_outlinerOpen;
            RefreshOutliner();
        };
        header.AddChild(_outlinerToggle);
        _outlinerRows = new VBoxContainer();
        _outlinerRows.AddThemeConstantOverride("separation", 2);
        box.AddChild(_outlinerRows);
        Float(_outliner, "Outliner");
    }

    void RefreshOutliner()
    {
        if (_outlinerRows == null)
            return;
        foreach (Node child in _outlinerRows.GetChildren())
            child.QueueFree();
        var items = OutlinerItems();
        _outlinerToggle.Text = (_outlinerOpen ? "▾ " : "▸ ") + items.Sum(g => g.Lines.Count);
        _outlinerRows.Visible = _outlinerOpen;
        if (!_outlinerOpen)
            return;
        if (items.Count == 0)
            _outlinerRows.AddChild(ThemeAncient.Label("Nothing waits on you.", "SubtleLabel", 15));
        foreach (var (heading, lines) in items)
        {
            _outlinerRows.AddChild(ThemeAncient.Label(heading, "SubtleLabel", 14));
            foreach (var (text, go) in lines)
            {
                var b = new Button { Text = "  " + text, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None,
                    ClipText = true, CustomMinimumSize = new Vector2(280, 0) };
                b.AddThemeFontSizeOverride("font_size", 15);
                b.Pressed += go;
                _outlinerRows.AddChild(b);
            }
        }
    }

    /// <summary>Everything that needs the player, grouped under headings, each with where clicking takes you.</summary>
    List<(string Heading, List<(string Text, Action Go)> Lines)> OutlinerItems()
    {
        var groups = new List<(string, List<(string, Action)>)>();
        var me = _map.PlayerRealmId;
        var s = _map.PlayerState;
        void Add(string heading, IEnumerable<(string, Action)> lines)
        {
            var l = lines.ToList();
            if (l.Count > 0)
                groups.Add((heading, l));
        }
        int waiting = _map.Game.Offers.Count + _map.Game.PeaceOffers.Count;
        Add("Waiting for an answer", waiting > 0
            ? new[] { ($"{waiting} offer{(waiting > 1 ? "s" : "")} in Diplomacy", (Action)(() => ToggleDiplomacyPanel(true))) }
            : Array.Empty<(string, Action)>());
        Add("Wars", _map.Game.Wars.All.Where(w => w.Attacker == me || w.Defender == me).Select(w =>
        {
            int enemy = w.Attacker == me ? w.Defender : w.Attacker;
            return ($"{_map.RealmName(enemy)}: score {w.ScoreFor(me):+0;-0}", (Action)(() => ToggleDiplomacyPanel(true)));
        }));
        Add("Sieges", _map.Game.Sieges.Where(x => x.Attacker == me || x.Owner == me).Select(x =>
            ($"{(x.Attacker == me ? "Besieging" : "Besieged:")} {x.Name} ({x.Progress:P0})", (Action)(() => FocusNode(x.Node)))));
        Add("Armies", s.Armies.Where(a => !a.IsEmpty).Select(a =>
            ($"{a.Name}: {ThemeAncient.GroupThousands(Military.Soldiers(a))} men" + (a.Resting ? ", resting" : ""), (Action)(() => FocusNode(a.Node)))));
        Add("Unrest", _map.Provinces!.Provinces.Values.Where(p => p.RealmId == me)
            .Select(p => (p, st: _map.ProvinceStateOf(p.Id)))
            .Where(x => x.st.Unrest > Loyalty.RevoltThreshold * 0.7)
            .OrderByDescending(x => x.st.Unrest).Take(5)
            .Select(x => ($"{x.p.Name}: unrest {x.st.Unrest:P0}" + (x.st.Unrest > Loyalty.RevoltThreshold ? ", may revolt" : ""),
                (Action)(() => { _map.SelectProvince(x.p.Id); _map.CenterOn(x.p.LabelCell * MapView.CellPixels, 3f); }))));
        var research = new List<(string, Action)>();
        if (string.IsNullOrEmpty(s.Researching))
            research.Add(("Nothing is being studied", () => ToggleResearchPanel(true)));
        Add("Research", research);
        var money = new List<(string, Action)>();
        if (s.Debt > 0.5)
            money.Add(($"In debt: {_map.Money(s.Debt)}", () => ToggleRealmPanel(true)));
        Add("Treasury", money);
        return groups;
    }

    void FocusNode(int node)
    {
        if (node >= 0)
            _map.CenterOn(_map.NodeCell(node), 3f);
    }
}
