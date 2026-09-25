using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The conquest plan panel (MECHANICS.md, "Annexation UX"): while painting in
/// Conquest mode, every target the plan touches, with both sides' Might and
/// the chance of winning - hidden until you paint over it - and a way to
/// declare war where war is needed. Fights are decided when the turn ends.
/// </summary>
public partial class Hud
{
    PanelContainer _conquestPanel = null!;
    VBoxContainer _conquestRows = null!;
    Label _conquestSummary = null!;

    void BuildConquestPanel()
    {
        _conquestPanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(420, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _conquestPanel.SetAnchorsPreset(LayoutPreset.TopRight);
        _conquestPanel.GrowHorizontal = GrowDirection.Begin;
        _conquestPanel.OffsetLeft = _conquestPanel.OffsetRight = -20;
        _conquestPanel.OffsetTop = _conquestPanel.OffsetBottom = 80;
        AddChild(_conquestPanel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _conquestPanel.AddChild(box);
        box.AddChild(ThemeAncient.Label("Conquest plan", "HeaderLabel", 22));
        var hint = ThemeAncient.Label(
            "Paint land to take. Touching an enemy province targets all of it. Dimmed land is out of your army's " +
            "reach this year; with warships you reach farther along coasts and across the sea. Right-click clears.",
            "SubtleLabel", 15);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(390, 0);
        box.AddChild(hint);
        _conquestRows = new VBoxContainer();
        _conquestRows.AddThemeConstantOverride("separation", 6);
        box.AddChild(_conquestRows);
        _conquestSummary = ThemeAncient.Label("", fontSize: 16);
        _conquestSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _conquestSummary.CustomMinimumSize = new Vector2(390, 0);
        box.AddChild(_conquestSummary);
    }

    void RefreshConquestPanel()
    {
        bool planning = _map.Mode == MapMode.PlanConquest;
        _conquestPanel.Visible = planning;
        if (!planning)
            return;
        foreach (Node child in _conquestRows.GetChildren())
            child.QueueFree();
        var targets = _map.PlayerConquestTargets();
        if (targets.Count == 0)
        {
            _conquestSummary.Text = "Nothing painted yet.";
            return;
        }
        var declared = new HashSet<int>();
        foreach (var t in targets.OrderByDescending(t => t.People))
        {
            var row = new VBoxContainer();
            _conquestRows.AddChild(row);
            row.AddChild(ThemeAncient.Label(t.Name, fontSize: 17));
            string people = ThemeAncient.GroupThousands((long)Math.Round(t.People / 100) * 100);
            if (t.Problem != null)
            {
                var problem = ThemeAncient.Label($"{t.Problem}.", "SubtleLabel", 15);
                problem.Modulate = new Color(1f, 0.7f, 0.6f);
                row.AddChild(problem);
                if (t.Owner > 0 && !_map.Game.Wars.AtWar(_map.PlayerRealmId, t.Owner) && declared.Add(t.Owner))
                {
                    int owner = t.Owner;
                    string? cannot = Diplomacy.CanDeclare(_map.Game, _map.PlayerRealmId, owner, _map.DemoYear);
                    bool pretext = cannot == null && _map.HasPretext(_map.PlayerRealmId, owner);
                    var declare = new Button
                    {
                        Text = $"Declare war on {_map.RealmName(owner)}",
                        Disabled = cannot != null,
                        TooltipText = cannot ?? (pretext
                            ? "You share a border: a quarrel is easily found."
                            : "No just cause: other realms will remember it."),
                        FocusMode = FocusModeEnum.None,
                    };
                    declare.Pressed += () => DeclareWarOn(owner);
                    row.AddChild(declare);
                }
                continue;
            }
            row.AddChild(ThemeAncient.Label(
                $"{people} people.  Their Might {t.DefenderMight:0.0} against your {t.AttackerMight:0.0}: " +
                $"{t.Chance:P0} chance" + (t.BySea ? " (by sea)" : ""), "SubtleLabel", 15));
        }
        int ready = targets.Count(t => t.Problem == null);
        _conquestSummary.Text = ready == 0
            ? "Nothing can be attacked yet."
            : $"{ready} target{(ready == 1 ? "" : "s")}: your army is split between them. The fights are decided when you end the turn.";
    }

    void DeclareWarOn(int enemy)
    {
        string? text = _map.DeclareWar(enemy);
        if (text == null)
            return;
        LogEvents(new List<ChronicleEvent> { new(ChronicleKind.War, _map.PlayerRealmId, text) });
        RefreshConquestPanel();
        RefreshDiplomacyPanel();
    }
}
