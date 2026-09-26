using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The diplomacy panel: offers other realms have made you; every other
/// realm with what it thinks of you and why, your treaties, and the war if
/// there is one (score and exhaustion on both sides); declare war on a
/// pretext, make peace on terms, propose or break alliances, demand or
/// release vassals.
/// </summary>
public partial class Hud
{
    PanelContainer _diplomacyPanel = null!;
    VBoxContainer _diplomacyRows = null!;
    int _peaceWith;                                   // the realm whose peace terms are open, or 0
    readonly PeaceTerms _terms = new();

    void BuildDiplomacyButton(HBoxContainer row)
    {
        var button = new Button
        {
            Flat = true,
            Text = "Diplomacy",
            Icon = ThemeAncient.Icon("scales"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "War, peace and treaties with other realms",
            ExpandIcon = false,
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += () => ToggleDiplomacyPanel();
        row.AddChild(button);
    }

    void BuildDiplomacyPanel()
    {
        _diplomacyPanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(760, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _diplomacyPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        _diplomacyPanel.OffsetLeft = _diplomacyPanel.OffsetRight = 20;
        _diplomacyPanel.OffsetTop = _diplomacyPanel.OffsetBottom = 80;
        AddChild(_diplomacyPanel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _diplomacyPanel.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        var title = ThemeAncient.Label("Diplomacy", "HeaderLabel", 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(IconButton(null, "×", "Close", () => ToggleDiplomacyPanel(false)));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740, 640), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _diplomacyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _diplomacyRows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_diplomacyRows);
    }

    public void ShowDiplomacyPanel() => ToggleDiplomacyPanel(true);

    void ToggleDiplomacyPanel(bool? show = null)
    {
        _diplomacyPanel.Visible = show ?? !_diplomacyPanel.Visible;
        if (_diplomacyPanel.Visible)
        {
            ToggleRealmPanel(false);
            _goodsPanel.Visible = false;
            _goalsPanel.Visible = false;
            _guidePanel.Visible = false;
            _armiesPanel.Visible = false;
            _researchPanel.Visible = false;
            RefreshDiplomacyPanel();
        }
        RefreshMapView();
    }

    void Log(string? text, ChronicleKind kind = ChronicleKind.Peace)
    {
        if (!string.IsNullOrEmpty(text))
            LogEvents(new List<ChronicleEvent> { new(kind, _map.PlayerRealmId, text) });
    }

    void AfterDiplomacy()
    {
        RefreshDiplomacyPanel();
        RefreshConquestPanel();
        RefreshTreasury();
        _map.RefreshArmyMarkers();
        if (_map.TerritoryChanged)
            _ = _map.RedrawTerritory();
    }

    Label Small(string text, float width = 0)
    {
        var l = ThemeAncient.Label(text, "SubtleLabel", 14);
        if (width > 0)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(width, 0);
        }
        return l;
    }

    void RefreshDiplomacyPanel()
    {
        if (!_diplomacyPanel.Visible)
            return;
        foreach (Node child in _diplomacyRows.GetChildren())
            child.QueueFree();
        int me = _map.PlayerRealmId;
        var census = _map.RealmCensus();
        var mine = _map.PlayerState;

        _diplomacyRows.AddChild(Small(
            $"Your aggression: {mine.Aggression:0} (others fear conquerors; it fades by {Pretexts.AggressionDecay:0} a year" +
            (mine.Aggression >= Pretexts.CoalitionAggression ? "; at this level your neighbours band together against you" : "") + ").", 720));

        // Offers.
        foreach (var offer in _map.Game.Offers.ToList())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _diplomacyRows.AddChild(row);
            string detail = offer.Text + (offer.Silver > 0 ? $": {_map.Money(offer.Silver)}" : "") +
                (offer.Provinces.Count > 0 ? $", ceding {string.Join(", ", offer.Provinces.Select(p => _map.Provinces!.Provinces.TryGetValue(p, out var pr) ? pr.Name : "?"))}" : "");
            var label = ThemeAncient.Label(detail, fontSize: 16);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.Modulate = new Color(1f, 0.92f, 0.7f);
            row.AddChild(label);
            var o = offer;
            var yes = new Button { Text = "Accept", FocusMode = FocusModeEnum.None };
            yes.Pressed += () => { Log(_map.AnswerOffer(o, true)); AfterDiplomacy(); };
            row.AddChild(yes);
            var no = new Button { Text = "Decline", FocusMode = FocusModeEnum.None };
            no.Pressed += () => { Log(_map.AnswerOffer(o, false)); AfterDiplomacy(); };
            row.AddChild(no);
        }

        foreach (var realm in _map.Registry.Realms.Values.Where(r => r.Id != me && census.ContainsKey(r.Id))
                     .OrderByDescending(r => _map.Game.Wars.AtWar(me, r.Id)).ThenBy(r => r.Name))
            AddRealmRow(realm, me);
    }

    void AddRealmRow(Realm realm, int me)
    {
        int id = realm.Id;
        var state = _map.Game.Realm(id);
        var game = _map.Game;
        var war = game.Wars.Between(me, id);
        int? truce = game.Wars.TruceUntil(me, id, _map.DemoYear);
        var frame = new VBoxContainer();
        frame.AddThemeConstantOverride("separation", 2);
        _diplomacyRows.AddChild(frame);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        frame.AddChild(row);
        row.AddChild(new ColorRect { Color = realm.Color, CustomMinimumSize = new Vector2(8, 26) });
        var name = ThemeAncient.Label(realm.Name, fontSize: 17);
        name.CustomMinimumSize = new Vector2(190, 0);
        row.AddChild(name);

        var tags = new List<string>();
        if (game.Treaties.Allied(me, id)) tags.Add("your ally");
        if (game.Treaties.OverlordOf(id) == me) tags.Add("your vassal");
        if (game.Treaties.OverlordOf(me) == id) tags.Add("your overlord");
        if (game.Treaties.CoalitionAgainst(me).Contains(id)) tags.Add("in a coalition against you");
        if (_map.Rivals(me, id)) tags.Add("old rival");
        var (opinion, reasons) = _map.Opinion(id, me);
        string status = war != null
            ? (war.Supports is { } sup
                ? $"At war (as {(sup.Attacker == me || sup.Defender == me ? "the main foe" : "an ally")})"
                : $"At war since {ThemeAncient.YearText(war.Since)}: score {war.ScoreFor(me):+0;-0;0}, " +
                  $"their exhaustion {war.ExhaustionFor(id):0}, yours {war.ExhaustionFor(me):0}")
            : truce is int until ? $"Truce until {ThemeAncient.YearText(until)}"
            : "At peace";
        var statusLabel = Small($"{status}\nOpinion of you {opinion:+0;-0;0}" + (tags.Count > 0 ? "  ·  " + string.Join(", ", tags) : "") +
            $"  ·  army about {ThemeAncient.GroupThousands((long)Math.Round(Military.Soldiers(state) / 1000.0) * 1000)} men");
        statusLabel.TooltipText = reasons.Count > 0 ? string.Join("\n", reasons) : "No strong feelings either way.";
        statusLabel.MouseFilter = MouseFilterEnum.Pass;
        statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (war != null)
            statusLabel.Modulate = new Color(1f, 0.75f, 0.65f);
        row.AddChild(statusLabel);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        frame.AddChild(buttons);
        if (war == null)
        {
            string? cannot = Diplomacy.CanDeclare(game, me, id, _map.DemoYear);
            var declare = new MenuButton { Text = "Declare war ▾", FocusMode = FocusModeEnum.None, Flat = false, Disabled = cannot != null, TooltipText = cannot ?? "Choose your pretext" };
            if (cannot == null)
            {
                var pretexts = _map.PretextsAgainst(me, id);
                var popup = declare.GetPopup();
                for (int i = 0; i < pretexts.Count; i++)
                {
                    var p = pretexts[i];
                    popup.AddItem($"{p.Name} (aggression +{p.AggressionOnDeclare:0}, +{p.AggressionPerProvince:0} a province)", i);
                    popup.SetItemTooltip(i, p.Description);
                }
                popup.IdPressed += i => DeclareWarOn(id, pretexts[(int)i].Id);
            }
            buttons.AddChild(declare);
            if (game.Treaties.Allied(me, id))
                AddButton(buttons, "Break alliance", "They will not forget it (+5 aggression).", () => Log(_map.BreakAlliance(id)));
            else if (game.Treaties.OverlordOf(id) != me && game.Treaties.OverlordOf(me) != id)
                AddButton(buttons, "Propose alliance", $"Each defends the other. They accept at an opinion of {MapView.AllianceOpinion:+0} or more.",
                    () => Log(_map.ProposeAlliance(id)));
            if (game.Treaties.OverlordOf(id) == me)
                AddButton(buttons, "Release vassal", "They go their own way.", () => Log(_map.ReleaseVassal(id)));
            else if (game.Treaties.OverlordOf(id) == 0 && game.Treaties.OverlordOf(me) != id)
                AddButton(buttons, "Demand submission", "A far weaker realm that thinks well of you may become your vassal without a war.",
                    () => Log(_map.DemandSubmission(id)));
        }
        else if (war.Supports == null)
        {
            AddButton(buttons, _peaceWith == id ? "Close peace terms" : "Make peace...", "Choose terms: they accept what the war score and their weariness will bear.",
                () =>
                {
                    _peaceWith = _peaceWith == id ? 0 : id;
                    _terms.Provinces.Clear();
                    _terms.Tribute = _terms.Vassal = false;
                });
            if (_peaceWith == id)
                AddPeaceTerms(frame, war, id, me);
        }
    }

    void AddButton(HBoxContainer row, string text, string tip, Action act)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None };
        b.Pressed += () => { act(); AfterDiplomacy(); };
        row.AddChild(b);
    }

    /// <summary>A small check box without the big button frame.</summary>
    static CheckBox Check(string text, bool on)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
        c.AddThemeFontSizeOverride("font_size", 15);
        foreach (var style in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            c.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        return c;
    }

    void AddPeaceTerms(VBoxContainer frame, War war, int enemy, int me)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        frame.AddChild(box);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        box.AddChild(grid);
        // Provinces worth asking for: those you besiege, your own lost ones they hold, then their largest.
        var provinces = _map.Provinces!.Provinces.Values.Where(p => p.RealmId == enemy).ToList();
        var besieged = _map.Game.Sieges.Where(s => s.Attacker == me && s.Owner == enemy).Select(s => s.ProvinceId).ToHashSet();
        var lost = _map.Game.Lost.TryGetValue(me, out var l) ? l.Keys.ToHashSet() : new HashSet<int>();
        var offerable = provinces.OrderByDescending(p => besieged.Contains(p.Id)).ThenByDescending(p => lost.Contains(p.Id))
            .ThenByDescending(p => _map.ProvincePopulation(p.Id)).Take(12).ToList();
        foreach (var p in offerable)
        {
            var one = new PeaceTerms();
            one.Provinces.Add(p.Id);
            var check = Check($"Cede {p.Name} ({_map.TermsCost(war, me, one):0})" +
                (besieged.Contains(p.Id) ? ", besieged" : lost.Contains(p.Id) ? ", once yours" : ""), _terms.Provinces.Contains(p.Id));
            check.CustomMinimumSize = new Vector2(340, 0);
            int pid = p.Id;
            check.Toggled += on =>
            {
                if (on) _terms.Provinces.Add(pid); else _terms.Provinces.Remove(pid);
                RefreshDiplomacyPanel();
            };
            grid.AddChild(check);
        }
        var tribute = Check($"Tribute in silver ({PeaceTerms.TributeCost:0})", _terms.Tribute);
        tribute.Toggled += on => { _terms.Tribute = on; RefreshDiplomacyPanel(); };
        box.AddChild(tribute);
        if (Pretexts.ById(war.CasusBelli).AllowsVassal || war.ScoreFor(me) >= PeaceTerms.VassalCost)
        {
            var vassal = Check($"They become your vassal ({PeaceTerms.VassalCost:0})", _terms.Vassal);
            vassal.Toggled += on => { _terms.Vassal = on; RefreshDiplomacyPanel(); };
            box.AddChild(vassal);
        }
        box.AddChild(Small("Numbers are the war score each term costs.", 700));
        double cost = _map.TermsCost(war, me, _terms);
        double willing = Diplomacy.WillingToPay(war, me, _map.DemoYear);
        bool accept = Diplomacy.AcceptsTerms(war, me, _map.DemoYear, cost);
        box.AddChild(Small(_terms.IsWhitePeace
            ? $"Peace keeping what each holds: {(accept ? "they would accept" : "they refuse while they are winning or fresh")}."
            : $"These terms cost {cost:0} war score; they will give up to {willing:0} (their losses, plus half their exhaustion): " +
              (accept ? "they would accept." : "they refuse."), 700));
        var send = new Button { Text = "Offer these terms", FocusMode = FocusModeEnum.None };
        send.Pressed += () =>
        {
            var terms = new PeaceTerms { Tribute = _terms.Tribute, Vassal = _terms.Vassal };
            terms.Provinces.AddRange(_terms.Provinces);
            var (ok, text) = _map.OfferPeace(enemy, terms);
            Log(text);
            if (ok)
                _peaceWith = 0;
            AfterDiplomacy();
        };
        box.AddChild(send);
    }
}
