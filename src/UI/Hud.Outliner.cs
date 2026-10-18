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
    /// <summary>The groups you have folded (your answer "Outliner contents": each group collapsible).</summary>
    readonly HashSet<string> _outlinerFolded = new();
    /// <summary>Room kept free below the outliner (the map and time buttons) and above it (the top bar and panels); its tallest; a line's height.</summary>
    const float OutlinerBottomRoom = 250, OutlinerTopRoom = 380, OutlinerMaxHeight = 420, OutlinerRowHeight = 30;

    void BuildOutliner()
    {
        _outliner = new PanelContainer { ThemeTypeVariation = "GlassPanel", CustomMinimumSize = new Vector2(300, 0), MouseFilter = MouseFilterEnum.Stop };
        // Pinned just above the map and time buttons, growing upward, so it fits any screen and interface size.
        _outliner.SetAnchorsPreset(LayoutPreset.BottomRight);
        _outliner.GrowHorizontal = GrowDirection.Begin;
        _outliner.GrowVertical = GrowDirection.Begin;
        _outliner.OffsetLeft = _outliner.OffsetRight = -20;
        _outliner.OffsetTop = _outliner.OffsetBottom = -OutlinerBottomRoom;
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
        // Only as many lines as fit above the buttons at the bottom of the screen (a larger interface size leaves less room).
        float top = OutlinerTopRoom;
        if (_provincePanel is { Visible: true })   // never under the province panel
            top = Math.Max(top, _provincePanel.GetGlobalRect().End.Y + 12);
        float room = Math.Min(OutlinerMaxHeight, GetViewportRect().Size.Y - OutlinerBottomRoom - top);
        int maxRows = Math.Max(3, (int)(room / OutlinerRowHeight)), rows = 0, total = items.Sum(g => g.Lines.Count + 1);
        foreach (var (heading, lines) in items)
        {
            if (rows + 2 > maxRows)
                break;
            bool folded = _outlinerFolded.Contains(heading);
            var head = new Button { Text = (folded ? "▸ " : "▾ ") + heading + (folded ? $" ({lines.Count})" : ""), Flat = true,
                Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, TooltipText = folded ? "Unfold" : "Fold" };
            head.AddThemeFontSizeOverride("font_size", 14);
            head.AddThemeColorOverride("font_color", ThemeAncient.TextDim);
            string h = heading;
            head.Pressed += () =>
            {
                if (!_outlinerFolded.Remove(h))
                    _outlinerFolded.Add(h);
                RefreshOutliner();
            };
            _outlinerRows.AddChild(head);
            rows++;
            if (folded)
                continue;
            foreach (var (text, go) in lines)
            {
                if (++rows > maxRows - 1 && rows < total)
                {
                    _outlinerRows.AddChild(ThemeAncient.Label($"  …and {total - rows + 1} more", "SubtleLabel", 14));
                    return;
                }
                var b = new Button { Text = "  " + text, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None,
                    ClipText = true, CustomMinimumSize = new Vector2(280, 0) };
                b.AddThemeFontSizeOverride("font_size", 15);
                b.Pressed += go;
                if (heading == "Pinned")
                {
                    string pin = _map.Game.Pins.FirstOrDefault(x => PinnedLine(x).Text == text) ?? "";
                    b.TooltipText = "Right-click to unpin";
                    b.GuiInput += e =>
                    {
                        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                            TogglePin(pin);
                    };
                }
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
        Add("Pinned", _map.Game.Pins.Select(PinnedLine).Where(x => x.Text != ""));
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
        bool IsFleet(Army a) => Military.RawMight(a, Domain.Land) <= 0;
        Add("Armies", s.Armies.Where(a => !a.IsEmpty && !IsFleet(a)).Select(a =>
            ($"{a.Name}: {ThemeAncient.GroupThousands(Military.Soldiers(a))} men" + (a.Resting ? ", resting" : a.Marching ? ", marching" : ""), (Action)(() => FocusNode(a.Node)))));
        Add("Fleets", s.Armies.Where(a => !a.IsEmpty && IsFleet(a)).Select(a =>
            ($"{a.Name}: {a.RoleCount(UnitCatalog.Instance, UnitRoles.Warships):0} warships" + (a.Marching ? ", sailing" : ""), (Action)(() => FocusNode(a.Node)))));
        Add("Unrest", _map.Provinces!.Provinces.Values.Where(p => p.RealmId == me)
            .Select(p => (p, st: _map.ProvinceStateOf(p.Id)))
            .Where(x => x.st.Unrest > Loyalty.RevoltThreshold * 0.7)
            .OrderByDescending(x => x.st.Unrest).Take(5)
            .Select(x => ($"{x.p.Name}: unrest {x.st.Unrest:P0}" + (x.st.Unrest > Loyalty.RevoltThreshold ? ", may revolt" : ""),
                (Action)(() => { _map.SelectProvince(x.p.Id); _map.CenterOn(x.p.LabelCell * MapView.CellPixels, 3f); }))));
        var research = new List<(string, Action)>();
        if (!s.Studying)
            research.Add(("Nothing is being studied", () => ToggleResearchPanel(true)));
        else
            foreach (var id in s.BranchResearching.Where(x => x != ""))
                research.Add(($"Studying {TechCatalog.Instance.All.FirstOrDefault(t => t.Id == id)?.Name ?? id}", () => ToggleResearchPanel(true)));
        Add("Research", research);
        Add("Building", _map.Provinces!.Provinces.Values.Where(p => p.RealmId == me)
            .Select(p => (p, st: _map.ProvinceStateOf(p.Id))).Where(x => x.st.Works.Count > 0)
            .Select(x => ($"{x.p.Name}: {BuildingCatalog.Instance[x.st.Works[0].Id]?.Name ?? x.st.Works[0].Id}, {x.st.Works[0].Left} yr" +
                          (x.st.Works.Count > 1 ? $" (+{x.st.Works.Count - 1} queued)" : ""),
                (Action)(() => { _map.SelectProvince(x.p.Id); _map.CenterOn(x.p.LabelCell * MapView.CellPixels, 3f); }))));
        var family = new List<(string, Action)>();
        var realm = _map.GetPlayerRealm();
        if (_map.Registry.Characters.TryGetValue(realm.RulerId, out var ruler))
            family.Add(($"{ruler.Name}, your ruler, aged {_map.DemoYear - ruler.BirthYear}", () => ShowPerson(ruler.Id)));
        if (_map.Registry.ResolveHeir(realm) is { } heir)
            family.Add(($"{heir.Name}, the heir, aged {_map.DemoYear - heir.BirthYear}", () => ShowPerson(heir.Id)));
        else
            family.Add(("No heir: the house may end", () => { if (ruler != null) ShowPerson(ruler.Id); }));
        Add("Family", family);
        var money = new List<(string, Action)>();
        if (s.Debt > 0.5)
            money.Add(($"In debt: {_map.Money(s.Debt)}", () => ToggleRealmPanel(true)));
        Add("Treasury", money);
        return groups;
    }

    /// <summary>Pins something to the outliner, or unpins it (your answer "Pinning": any province, character or realm).</summary>
    public void TogglePin(string pin)
    {
        if (pin == "")
            return;
        if (!_map.Game.Pins.Remove(pin))
            _map.Game.Pins.Add(pin);
        RefreshOutliner();
    }

    public bool IsPinned(string pin) => _map.Game.Pins.Contains(pin);

    /// <summary>A pinned thing's line in the outliner, and where clicking it takes you ("" when it is gone).</summary>
    (string Text, Action Go) PinnedLine(string pin)
    {
        int colon = pin.IndexOf(':');
        if (colon < 0 || !int.TryParse(pin[(colon + 1)..], out int id))
            return ("", () => { });
        switch (pin[..colon])
        {
            case "province" when _map.Provinces!.Provinces.TryGetValue(id, out var p):
                var st = _map.ProvinceStateOf(id);
                return ($"{p.Name}: {_map.RealmName(p.RealmId)}, unrest {st.Unrest:P0}",
                    () => { _map.SelectProvince(id); _map.CenterOn(p.LabelCell * MapView.CellPixels, 3f); });
            case "realm" when _map.RealmCensus().ContainsKey(id):
                var (opinion, _) = _map.Opinion(id, _map.PlayerRealmId);
                string stand = id == _map.PlayerRealmId ? "yours" : _map.Game.Wars.AtWar(id, _map.PlayerRealmId) ? "at war with you" : $"opinion {opinion:+0;-0;0}";
                return ($"{_map.RealmName(id)}: {stand}", () => ToggleDiplomacyPanel(true));
            case "character" when _map.Registry.Characters.TryGetValue(id, out var c):
                return ($"{c.Name}" + (c.IsAlive ? $", aged {_map.DemoYear - c.BirthYear}" : ", dead"), () => ShowPerson(id));
            default:
                return ("", () => { });
        }
    }

    void FocusNode(int node)
    {
        if (node >= 0)
            _map.CenterOn(_map.NodeCell(node), 3f);
    }
}
