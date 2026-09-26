using System;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The research panel (decision "Playable 24"): the web of technologies by
/// branch, each known, open to study, or waiting for its prerequisites or
/// its time; choose what your scholars study next.
/// </summary>
public partial class Hud
{
    PanelContainer _researchPanel = null!;
    Label _researchSummary = null!;
    TabContainer _researchTabs = null!;
    Button _researchButton = null!;

    void BuildResearchButton(HBoxContainer row)
    {
        _researchButton = new Button
        {
            Flat = true,
            Text = "Research",
            Icon = ThemeAncient.Icon("research"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Technology: what your scholars study (T)",
            ExpandIcon = false,
        };
        _researchButton.AddThemeFontSizeOverride("font_size", 20);
        _researchButton.Pressed += () => ToggleResearchPanel();
        row.AddChild(_researchButton);
    }

    void BuildResearchPanel()
    {
        _researchPanel = SidePanel("Research", () => ToggleResearchPanel(false), 900);
        var box = _researchPanel.GetNode<VBoxContainer>("Box");
        _researchSummary = ThemeAncient.Label("", fontSize: 16);
        _researchSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _researchSummary.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(_researchSummary);
        // 15 branches don't fit as tabs: a grid of branch buttons picks the page instead.
        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(grid);
        var group = new ButtonGroup();
        for (int i = 0; i < TechCatalog.Branches.Length; i++)
        {
            int page = i;
            var b = new Button { Text = TechCatalog.BranchShort[i], TooltipText = TechCatalog.BranchNames[i], ToggleMode = true, ButtonGroup = group,
                ButtonPressed = i == 0, CustomMinimumSize = new Vector2(170, 30), ClipText = true };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => _researchTabs.CurrentTab = page;
            grid.AddChild(b);
        }
        _researchTabs = new TabContainer { CustomMinimumSize = new Vector2(880, 540), TabsVisible = false };
        box.AddChild(_researchTabs);
        for (int i = 0; i < TechCatalog.Branches.Length; i++)
        {
            var scroll = new ScrollContainer { Name = TechCatalog.BranchNames[i], HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var list = new VBoxContainer { Name = "List", SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            scroll.AddChild(list);
            _researchTabs.AddChild(scroll);
        }
    }

    void ToggleResearchPanel(bool? show = null)
    {
        _researchPanel.Visible = show ?? !_researchPanel.Visible;
        if (_researchPanel.Visible)
        {
            ToggleRealmPanel(false);
            _goodsPanel.Visible = _diplomacyPanel.Visible = _goalsPanel.Visible = _guidePanel.Visible = _armiesPanel.Visible = false;
            RefreshResearchPanel();
        }
        RefreshMapView();
    }

    void RefreshResearchPanel()
    {
        var s = _map.PlayerState;
        var cat = TechCatalog.Instance;
        var current = cat[s.Researching];
        _researchButton.Text = current != null ? $"Research: {Math.Min(99, s.ResearchPoints / current.Cost * 100):0}%" : "Research";
        if (!_researchPanel.Visible)
            return;
        double perYear = cat.PointsPerYear(s, _map.CensusOf(_map.PlayerRealmId));
        _researchSummary.Text =
            $"You know {s.Techs.Count} of {cat.All.Count} technologies. Your scholars gather {perYear:0.0} points a year " +
            "(more from towns, schools and libraries). " +
            (current != null
                ? $"Studying: {current.Name}, {s.ResearchPoints:0} of {current.Cost:0} points (about {Math.Max(0, Math.Ceiling((current.Cost - s.ResearchPoints) / Math.Max(perYear, 0.1))):0} years)."
                : $"Nothing is being studied: choose a technology below ({s.ResearchPoints:0} points saved).") +
            "\nA technology opens when the world knows it (its year) and you know what it needs.";
        for (int i = 0; i < TechCatalog.Branches.Length; i++)
        {
            var list = _researchTabs.GetChild(i).GetNode<VBoxContainer>("List");
            foreach (Node child in list.GetChildren())
                child.QueueFree();
            string branch = TechCatalog.Branches[i];
            foreach (var t in cat.All.Where(t => t.Branch == branch).OrderBy(t => t.AvailableFrom).ThenBy(t => t.Cost))
            {
                bool known = s.Techs.Contains(t.Id);
                string? problem = cat.CanResearch(s, t, _map.DemoYear);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                list.AddChild(row);
                string effects = string.Join(", ", t.Effects.Select(e => DescribeEffect(e.Key, e.Value)));
                var label = ThemeAncient.Label(
                    $"{(known ? "✓ " : s.Researching == t.Id ? "▶ " : "")}{t.Name}  ·  {ThemeAncient.YearText(t.AvailableFrom)}  ·  {t.Cost:0} pts" +
                    (effects != "" ? $"  ·  {effects}" : ""), fontSize: 15);
                label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.TooltipText = t.Description + (t.Requires.Length > 0 ? "\nNeeds: " + string.Join(", ", t.Requires.Select(r => cat[r]?.Name ?? r)) : "") +
                    (t.Unlocks.Length > 0 ? "\nOpens: " + string.Join(", ", t.Unlocks) : "") + (problem != null && !known ? "\n" + problem : "");
                label.MouseFilter = MouseFilterEnum.Pass;
                label.Modulate = known ? new Color(0.75f, 1f, 0.75f) : problem == null ? Colors.White : new Color(1, 1, 1, 0.45f);
                row.AddChild(label);
                if (!known && problem == null && s.Researching != t.Id)
                {
                    var study = new Button { Text = "Study", FocusMode = FocusModeEnum.None };
                    study.AddThemeFontSizeOverride("font_size", 14);
                    string id = t.Id;
                    study.Pressed += () =>
                    {
                        _map.ChooseResearch(id);
                        RefreshResearchPanel();
                    };
                    row.AddChild(study);
                }
            }
        }
    }

    static string DescribeEffect(string key, double v)
    {
        string pct = $"{(v > 0 ? "+" : "")}{v * 100:0}%";
        return key switch
        {
            "field" => $"field crops {pct}",
            "orchard" => $"orchards {pct}",
            "herd" => $"herds {pct}",
            "mine" => $"mines {pct}",
            "crafts" => $"made goods {pct}",
            "trade" => $"trade {pct}",
            "tax" => $"taxes {pct}",
            "manpower" => $"levies {pct}",
            "integration" => $"integration speed {pct}",
            "growth" => $"health {pct}",
            "research" => $"research {pct}",
            "siege" => $"sieges {pct}",
            "unrest" => $"unrest {pct}",
            "corruption" => $"corruption {pct}",
            "interest" => $"interest {pct}",
            "admin" => $"+{v:0} provinces managed",
            "garrison" => $"garrisons +{v:0.#}",
            "reach_km" => $"reach +{v:0} km",
            "sea_cost" => $"sea travel {pct}",
            _ when key.StartsWith("might_") => $"{key[6..].Replace('_', ' ')} {pct}",
            _ => $"{key} {pct}",
        };
    }
}
