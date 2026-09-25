using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The diplomacy panel: every other realm, whether you are at war, at
/// peace or under truce, the size of its army, and the buttons to declare
/// war, offer peace, or demand tribute from a beaten enemy.
/// </summary>
public partial class Hud
{
    PanelContainer _diplomacyPanel = null!;
    VBoxContainer _diplomacyRows = null!;

    void BuildDiplomacyButton(HBoxContainer row)
    {
        var button = new Button
        {
            Flat = true,
            Text = "Diplomacy",
            Icon = ThemeAncient.Icon("scales"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "War and peace with other realms",
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
            CustomMinimumSize = new Vector2(560, 0),
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
        _diplomacyRows = new VBoxContainer();
        _diplomacyRows.AddThemeConstantOverride("separation", 6);
        box.AddChild(_diplomacyRows);
    }

    public void ShowDiplomacyPanel() => ToggleDiplomacyPanel(true);

    void ToggleDiplomacyPanel(bool? show = null)
    {
        _diplomacyPanel.Visible = show ?? !_diplomacyPanel.Visible;
        if (_diplomacyPanel.Visible)
        {
            ToggleRealmPanel(false);
            RefreshDiplomacyPanel();
        }
        RefreshMapView();
    }

    void RefreshDiplomacyPanel()
    {
        if (!_diplomacyPanel.Visible)
            return;
        foreach (Node child in _diplomacyRows.GetChildren())
            child.QueueFree();
        int me = _map.PlayerRealmId;
        var census = _map.RealmCensus();
        foreach (var realm in _map.Registry.Realms.Values.Where(r => r.Id != me && census.ContainsKey(r.Id))
                     .OrderBy(r => r.Name))
        {
            var state = _map.Game.Realm(realm.Id);
            var war = _map.Game.Wars.Between(me, realm.Id);
            int? truce = _map.Game.Wars.TruceUntil(me, realm.Id, _map.DemoYear);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _diplomacyRows.AddChild(row);
            row.AddChild(new ColorRect { Color = realm.Color, CustomMinimumSize = new Vector2(8, 26) });
            var name = ThemeAncient.Label(realm.Name, fontSize: 17);
            name.CustomMinimumSize = new Vector2(200, 0);
            row.AddChild(name);
            string status = war != null
                ? $"At war since {ThemeAncient.YearText(war.Since)}, score {war.ScoreFor(me):+0;-0;0}"
                : truce is int until ? $"Truce until {ThemeAncient.YearText(until)}"
                : "At peace";
            var statusLabel = ThemeAncient.Label(
                $"{status}\nArmy about {ThemeAncient.GroupThousands((long)Math.Round(Military.Soldiers(state) / 1000.0) * 1000)} men",
                "SubtleLabel", 14);
            statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (war != null)
                statusLabel.Modulate = new Color(1f, 0.75f, 0.65f);
            row.AddChild(statusLabel);
            int id = realm.Id;
            if (war == null)
            {
                string? cannot = Diplomacy.CanDeclare(_map.Game, me, id, _map.DemoYear);
                var declare = new Button { Text = "Declare war", Disabled = cannot != null, TooltipText = cannot ?? "", FocusMode = FocusModeEnum.None };
                declare.Pressed += () => DeclareWarOn(id);
                row.AddChild(declare);
            }
            else if (_map.Game.PeaceOffers.Contains(id))
            {
                var accept = new Button { Text = "Accept their offer", FocusMode = FocusModeEnum.None,
                    TooltipText = "They sue for peace: each keeps what it holds." };
                accept.Pressed += () =>
                {
                    string? text = _map.AcceptPeaceOffer(id);
                    if (text != null)
                        LogEvents(new List<ChronicleEvent> { new(ChronicleKind.Peace, _map.PlayerRealmId, text) });
                    RefreshDiplomacyPanel();
                    RefreshConquestPanel();
                };
                row.AddChild(accept);
                var tribute = new Button
                {
                    Text = "Demand tribute", FocusMode = FocusModeEnum.None,
                    Disabled = war.ScoreFor(me) < Diplomacy.TributeScore,
                    TooltipText = $"Peace, and they pay silver. Needs a war score of +{Diplomacy.TributeScore:0}.",
                };
                tribute.Pressed += () => OfferPeaceTo(id, true);
                row.AddChild(tribute);
            }
            else
            {
                var peace = new Button { Text = "Offer peace", FocusMode = FocusModeEnum.None,
                    TooltipText = "Each keeps what it holds. They accept if they are losing or tired of the war." };
                peace.Pressed += () => OfferPeaceTo(id, false);
                row.AddChild(peace);
                var tribute = new Button
                {
                    Text = "Demand tribute",
                    FocusMode = FocusModeEnum.None,
                    Disabled = war.ScoreFor(me) < Diplomacy.TributeScore,
                    TooltipText = $"Peace, and they pay silver. Needs a war score of +{Diplomacy.TributeScore:0}.",
                };
                tribute.Pressed += () => OfferPeaceTo(id, true);
                row.AddChild(tribute);
            }
        }
    }

    void OfferPeaceTo(int enemy, bool tribute)
    {
        var (_, text) = _map.OfferPeace(enemy, tribute);
        if (text != "")
            LogEvents(new List<ChronicleEvent> { new(ChronicleKind.Peace, _map.PlayerRealmId, text) });
        RefreshDiplomacyPanel();
        RefreshConquestPanel();
        RefreshTreasury();
    }
}
