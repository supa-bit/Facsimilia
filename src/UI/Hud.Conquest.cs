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
            "Paint land to take. Touching an enemy province targets all of it. Dimmed land is out of your chosen army's " +
            "reach this year (choose the army in the Armies panel); with warships it reaches farther along coasts and " +
            "across the sea. When you end the turn the army marches out and lays siege. Right-click clears.",
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
        var army = _map.SelectedArmy;
        _conquestRows.AddChild(ThemeAncient.Label(army == null ? "You have no army." :
            $"Army: {army.Name}, at {_map.PlaceOfNode(army.Node)} (land Might {Military.Might(army, Domain.Land):0.0})", fontSize: 17));
        var sieges = _map.Game.Sieges.Where(x => x.Attacker == _map.PlayerRealmId).ToList();
        foreach (var siege in sieges)
        {
            string by = _map.PlayerState.ArmyById(siege.ArmyId)?.Name ?? "?";
            _conquestRows.AddChild(ThemeAncient.Label($"Under siege by {by}: {siege.Name}, {Math.Min(siege.Progress, 0.99):P0} done",
                "SubtleLabel", 15));
        }
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
                    var best = cannot == null ? _map.PretextsAgainst(_map.PlayerRealmId, owner)[0] : null;
                    var declare = new Button
                    {
                        Text = $"Declare war on {_map.RealmName(owner)}" + (best != null ? $" ({best.Name.ToLowerInvariant()})" : ""),
                        Disabled = cannot != null,
                        TooltipText = cannot ?? $"{best!.Description}\nOther pretexts: in the Diplomacy panel.",
                        FocusMode = FocusModeEnum.None,
                    };
                    declare.Pressed += () => DeclareWarOn(owner);
                    row.AddChild(declare);
                }
                continue;
            }
            var line = ThemeAncient.Label(
                $"{people} people.  Defenders {t.DefenderMight:0.0} against your {t.AttackerMight:0.0}: " +
                $"{t.Chance:P0} to win in battle; a siege of about {(double.IsInfinity(t.Years) ? "many" : t.Years.ToString("0"))} " +
                $"year{(t.Years == 1 ? "" : "s")}" + (t.BySea ? " (by sea)" : ""), "SubtleLabel", 15);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(390, 0);
            row.AddChild(line);
        }
        int ready = targets.Count(t => t.Problem == null);
        _conquestSummary.Text = ready == 0
            ? "Nothing can be attacked yet."
            : $"{ready} target{(ready == 1 ? "" : "s")}: the army divides its strength between its sieges. It marches out when you end the turn; " +
              "each year the sieges advance, and the enemy may march to relieve them.";
    }

    void DeclareWarOn(int enemy, string pretext = "")
    {
        var events = _map.DeclareWar(enemy, pretext);
        if (events == null)
            return;
        LogEvents(events);
        RefreshConquestPanel();
        RefreshDiplomacyPanel();
        _map.RefreshArmyMarkers();
    }
}
