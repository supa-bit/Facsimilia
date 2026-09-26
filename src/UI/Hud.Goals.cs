using System;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The goals panel (your score, and history's ambitions for your realm with
/// how near you are to each) and the first-game guide.
/// </summary>
public partial class Hud
{
    const string GuideSeenPath = "user://guide_seen";
    PanelContainer _goalsPanel = null!, _guidePanel = null!;
    VBoxContainer _goalRows = null!;
    Label _scoreLabel = null!;
    Button _goalsButton = null!;

    void BuildGoalsButton(HBoxContainer row)
    {
        _goalsButton = new Button
        {
            Flat = true,
            Text = "Goals",
            Icon = ThemeAncient.Icon("laurels"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "Your score and your realm's goals",
            ExpandIcon = false,
        };
        _goalsButton.AddThemeFontSizeOverride("font_size", 20);
        _goalsButton.Pressed += () => ToggleGoalsPanel();
        row.AddChild(_goalsButton);
    }

    PanelContainer SidePanel(string title, Action close, float width)
    {
        var panel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        panel.SetAnchorsPreset(LayoutPreset.TopLeft);
        panel.OffsetLeft = panel.OffsetRight = 20;
        panel.OffsetTop = panel.OffsetBottom = 80;
        AddChild(panel);
        var box = new VBoxContainer { Name = "Box" };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        var label = ThemeAncient.Label(title, "HeaderLabel", 22);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(label);
        header.AddChild(IconButton(null, "×", "Close", close));
        return panel;
    }

    void BuildGoalsPanel()
    {
        _goalsPanel = SidePanel("Goals", () => ToggleGoalsPanel(false), 620);
        var box = _goalsPanel.GetNode<VBoxContainer>("Box");
        _scoreLabel = ThemeAncient.Label("", fontSize: 18);
        _scoreLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _scoreLabel.CustomMinimumSize = new Vector2(580, 0);
        box.AddChild(_scoreLabel);
        _goalRows = new VBoxContainer();
        _goalRows.AddThemeConstantOverride("separation", 4);
        box.AddChild(_goalRows);
        var guide = new Button { Text = "How to play", FocusMode = FocusModeEnum.None };
        guide.Pressed += () => ShowGuide(true);
        box.AddChild(guide);

        _guidePanel = SidePanel("How to play", () => ShowGuide(false), 640);
        var gbox = _guidePanel.GetNode<VBoxContainer>("Box");
        var text = ThemeAncient.Label(GuideText, fontSize: 17);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(600, 0);
        gbox.AddChild(text);
        var ok = new Button { Text = "Begin", FocusMode = FocusModeEnum.None };
        ok.Pressed += () => ShowGuide(false);
        gbox.AddChild(ok);
    }

    const string GuideText =
        "You rule a realm of 300 BC. There is no set ending: play as long as you like, and chase the goals if you want a score.\n\n" +
        "1. Each turn is 1, 5, 10 or 25 years (the box by Advance Year). Press Advance Year (or Enter) to let time pass.\n" +
        "2. Treasury (the coins at the top): set taxes and raise or disband your army. Heavier taxes bring silver but slow growth and stir unrest.\n" +
        "3. Goods (G): what your people make, need and trade, by family. Shortages breed unrest.\n" +
        "4. Diplomacy: declare war on a neighbour (you need a war before attacking), offer peace, take tribute.\n" +
        "5. Conquest (key 3): paint the land you want within your army's reach. The odds show before the turn ends; battles are fought when you advance.\n" +
        "6. Provinces (key 2) and Inspect (key 1): click a province to see its people, loyalty, harvest and land. Newly conquered peoples take years to settle.\n" +
        "7. Map views (V for political, R for regions): terrain, soil, climate, crops and resources.\n" +
        "8. The chronicle (bottom left) tells you what happened each turn.\n\n" +
        "The other realms follow history's campaigns, so expect Rome and Carthage to clash, and the Successor kings to fight over Syria and Macedon.";

    void ShowGuide(bool show)
    {
        _guidePanel.Visible = show;
        if (show)
        {
            _goalsPanel.Visible = false;
            ToggleRealmPanel(false);
            _goodsPanel.Visible = false;
            _diplomacyPanel.Visible = false;
            _armiesPanel.Visible = false;
        }
        else if (!FileAccess.FileExists(GuideSeenPath))
            using (var f = FileAccess.Open(GuideSeenPath, FileAccess.ModeFlags.Write))
                f?.StoreString("seen");
        RefreshMapView();
    }

    /// <summary>The guide opens by itself the first time anyone plays.</summary>
    void ShowGuideFirstTime()
    {
        if (!FileAccess.FileExists(GuideSeenPath))
            ShowGuide(true);
    }

    void ToggleGoalsPanel(bool? show = null)
    {
        _goalsPanel.Visible = show ?? !_goalsPanel.Visible;
        if (_goalsPanel.Visible)
        {
            ToggleRealmPanel(false);
            _goodsPanel.Visible = false;
            _diplomacyPanel.Visible = false;
            _guidePanel.Visible = false;
            _armiesPanel.Visible = false;
            RefreshGoalsPanel();
        }
        RefreshMapView();
    }

    void RefreshGoalsPanel()
    {
        _goalsButton.Text = $"Score {_map.Score(_map.PlayerRealmId):0}";
        if (!_goalsPanel.Visible)
            return;
        foreach (Node child in _goalRows.GetChildren())
            child.QueueFree();
        var cat = _map.GoalsCatalog();
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        var goals = _map.GoalsOf(_map.PlayerRealmId);
        int points = goals.Where(g => s.GoalsDone.ContainsKey(g.Id)).Sum(g => g.Points);
        _scoreLabel.Text = $"Score: {_map.Score(_map.PlayerRealmId):0}" +
            (cat == null ? "" : $"  (people {c.People / cat.PeoplePerPoint:0}, provinces {c.Provinces * cat.ProvincePoints:0}, " +
                $"silver {Math.Max(0, s.Treasury - s.Debt) / cat.TalentsPerPoint:0}, goals {points})") +
            "\nGoals are optional: history's ambitions for your realm. Reaching one adds its points.";
        foreach (var g in goals)
        {
            bool done = s.GoalsDone.TryGetValue(g.Id, out int year);
            var (progress, detail) = _map.GoalProgress(_map.PlayerRealmId, g);
            var label = ThemeAncient.Label(
                (done ? $"✓ {g.Text} (+{g.Points}, reached {ThemeAncient.YearText(year)})"
                      : $"○ {g.Text} (+{g.Points}): {progress:P0} - {detail}"), fontSize: 17);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(580, 0);
            if (done)
                label.Modulate = new Color(0.75f, 1f, 0.75f);
            _goalRows.AddChild(label);
        }
    }
}
