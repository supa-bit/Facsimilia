using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Learning to play (decision "Next 41": both): a short tip the first time
/// each panel opens, and a guided first game - ten steps, each ticked off
/// when you do it.
/// </summary>
public partial class Hud
{
    const string TipsPath = "user://tips_seen.cfg";
    ConfigFile? _tipsSeen;
    PanelContainer? _tipCard;
    double _tutorialClock;

    static readonly (string Key, string Title, string Text)[] Tips =
    {
        ("realm", "Treasury", "Your silver, taxes and army. Heavier taxes bring silver but slow growth and stir unrest. Your coin's silver and its worth in your neighbours' coins are at the bottom."),
        ("goods", "Goods", "What your people make, need and trade. Shortages breed unrest; trade along the routes fills them."),
        ("diplomacy", "Diplomacy", "Find a reason for war (a pretext) before you strike: a war without one angers everyone. Offers from other realms wait here."),
        ("armies", "Armies", "Each army has its own units. Recruit, merge and move them; an army must be in reach to besiege."),
        ("research", "Research", "Pick a technology to study. It opens only in its time and once you know what it needs. Scholars come from towns, schools and libraries."),
        ("goals", "Goals", "Optional aims from your history. The score adds up people, land, wealth, culture, dynasty and goals."),
        ("realms", "Realms", "Every realm side by side. Click a column to sort; the graph shows the leaders over the years."),
        ("province", "Province", "A province's people, land, buildings and garrison, in tabs. Your own provinces can be taxed and built on."),
    };

    void UpdateTips(double delta)
    {
        _tutorialClock += delta;
        if (_tutorialClock < 0.5)
            return;
        _tutorialClock = 0;
        UpdateTutorial();
        if (_tipCard != null && _tipCard.Visible)
            return;
        var open = new (string Key, Control? Panel)[]
        {
            ("realm", _realmPanel), ("goods", _goodsPanel), ("diplomacy", _diplomacyPanel), ("armies", _armiesPanel),
            ("research", _researchPanel), ("goals", _goalsPanel), ("realms", _realmsPanel), ("province", _provincePanel),
        };
        _tipsSeen ??= LoadTips();
        foreach (var (key, panel) in open)
            if (panel is { Visible: true } && !_tipsSeen.HasSectionKey("seen", key))
            {
                var tip = Tips.First(t => t.Key == key);
                ShowTip(tip.Title, tip.Text, panel, key);
                return;
            }
    }

    static ConfigFile LoadTips()
    {
        var c = new ConfigFile();
        c.Load(TipsPath);
        return c;
    }

    void ShowTip(string title, string text, Control panel, string key)
    {
        if (_tipCard == null)
        {
            _tipCard = new PanelContainer { ThemeTypeVariation = "GlassPanel", MouseFilter = MouseFilterEnum.Stop, ZIndex = 40 };
            AddChild(_tipCard);
        }
        foreach (Node child in _tipCard.GetChildren())
            child.QueueFree();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _tipCard.AddChild(box);
        box.AddChild(ThemeAncient.Label("Tip: " + title, "HeaderLabel", 18));
        var body = ThemeAncient.Label(text, fontSize: 15);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.CustomMinimumSize = new Vector2(300, 0);
        box.AddChild(body);
        var ok = new Button { Text = "Got it", FocusMode = FocusModeEnum.None };
        ok.Pressed += () =>
        {
            _tipCard.Visible = false;
            _tipsSeen!.SetValue("seen", key, true);
            _tipsSeen.Save(TipsPath);
        };
        box.AddChild(ok);
        _tipCard.Visible = true;
        _tipCard.ResetSize();
        var screen = GetViewportRect().Size;
        var rect = panel.GetGlobalRect();
        float x = rect.End.X + 12 + 330 < screen.X ? rect.End.X + 12 : Math.Max(12, rect.Position.X - 342);
        _tipCard.Position = new Vector2(x, Math.Clamp(rect.Position.Y, 70, screen.Y - 220));
    }

    // --- The guided first game ------------------------------------------------------

    PanelContainer? _tutorialPanel;
    VBoxContainer _tutorialRows = null!;
    int _tutorialStep = -1;
    int _tutorialStartYear, _tutorialStartSoldiers, _tutorialStartProvinces;
    bool _sawDiplomacy, _sawCulture, _sawConquest;

    (string Text, Func<bool> Done)[] TutorialSteps() => new (string, Func<bool>)[]
    {
        ("Open your Treasury (the coins at the top) and look at your taxes.", () => _realmPanel.Visible),
        ("Open Research (T) and choose something to study.", () => !string.IsNullOrEmpty(_map.PlayerState.Researching)),
        ("Click one of your provinces, open its Buildings tab and start a building.",
            () => _map.Provinces!.Provinces.Values.Any(p => p.RealmId == _map.PlayerRealmId && _map.ProvinceStateOf(p.Id).Building != "")),
        ("Open Armies and recruit more soldiers.", () => Military.Soldiers(_map.PlayerState) > _tutorialStartSoldiers),
        ("Choose the Culture map view (the menu at the bottom right).", () => _sawCulture),
        ("Press Advance Year (or Enter) and read the chronicle.", () => _map.DemoYear > _tutorialStartYear),
        ("Open Diplomacy and look at the reasons you have for war.", () => _sawDiplomacy),
        ("Declare war on a neighbour, with a pretext if you have one.",
            () => _map.Game.Wars.All.Any(w => w.Attacker == _map.PlayerRealmId || w.Defender == _map.PlayerRealmId)),
        ("Switch to Conquest (key 3) and paint the land you want in reach of your army.", () => _sawConquest),
        ("Advance until you win a province, or make peace in Diplomacy.",
            () => _map.Provinces!.Provinces.Values.Count(p => p.RealmId == _map.PlayerRealmId) > _tutorialStartProvinces
                  || (_tutorialStep >= 8 && !_map.Game.Wars.All.Any(w => w.Attacker == _map.PlayerRealmId || w.Defender == _map.PlayerRealmId))),
    };

    public void StartTutorial()
    {
        _tutorialStep = 0;
        _tutorialStartYear = _map.DemoYear;
        _tutorialStartSoldiers = Military.Soldiers(_map.PlayerState);
        _tutorialStartProvinces = _map.Provinces!.Provinces.Values.Count(p => p.RealmId == _map.PlayerRealmId);
        _sawDiplomacy = _sawCulture = _sawConquest = false;
        if (_tutorialPanel == null)
        {
            _tutorialPanel = SidePanel("First steps", () => { _tutorialPanel!.Visible = false; _tutorialStep = -1; }, 420);
            _tutorialPanel.SetAnchorsPreset(LayoutPreset.TopRight);
            _tutorialPanel.GrowHorizontal = GrowDirection.Begin;
            _tutorialPanel.OffsetLeft = _tutorialPanel.OffsetRight = -480;
            _tutorialRows = new VBoxContainer();
            _tutorialRows.AddThemeConstantOverride("separation", 4);
            _tutorialPanel.GetNode<VBoxContainer>("Box").AddChild(_tutorialRows);
        }
        _tutorialPanel.Visible = true;
        RenderTutorial();
    }

    void UpdateTutorial()
    {
        if (_tutorialStep < 0 || _tutorialPanel == null)
            return;
        _sawDiplomacy |= _diplomacyPanel.Visible;
        _sawCulture |= _map.CurrentView == MapView.CultureView;
        _sawConquest |= _map.Mode == MapMode.PlanConquest;
        var steps = TutorialSteps();
        bool moved = false;
        while (_tutorialStep < steps.Length && steps[_tutorialStep].Done())
        {
            _tutorialStep++;
            moved = true;
        }
        if (moved)
            RenderTutorial();
    }

    void RenderTutorial()
    {
        foreach (Node child in _tutorialRows.GetChildren())
            child.QueueFree();
        var steps = TutorialSteps();
        for (int i = 0; i < steps.Length; i++)
        {
            string mark = i < _tutorialStep ? "✓" : i == _tutorialStep ? "▸" : "·";
            var l = ThemeAncient.Label($"{mark} {steps[i].Text}", i == _tutorialStep ? "" : "SubtleLabel", i == _tutorialStep ? 16 : 14);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(390, 0);
            _tutorialRows.AddChild(l);
        }
        if (_tutorialStep >= steps.Length)
            _tutorialRows.AddChild(ThemeAncient.Label("You know the essentials. The rest is history: yours to make.", "HeaderLabel", 16));
    }
}
