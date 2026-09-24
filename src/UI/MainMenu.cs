using System;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The boot scene (project.godot run/main_scene). "New Game" goes to Main.tscn,
/// whose GameRoot runs the intro and realm selection; "Continue" sets
/// SaveSystem.ContinueRequested so GameRoot loads the save instead.
/// </summary>
public partial class MainMenu : Control
{
    static readonly PackedScene SettingsPanelScene = GD.Load<PackedScene>("res://scenes/SettingsPanel.tscn");

    SettingsPanel? _settingsPanel;

    public override void _Ready()
    {
        Theme = ThemeAncient.Build();
        ThemeAncient.FullRect(this);
        AddChild(ThemeAncient.Backdrop(0.45f));

        // Darker on the left, where the menu sits.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(ThemeAncient.Ink, 0.92f));
        gradient.SetColor(1, new Color(ThemeAncient.Ink, 0f));
        AddChild(new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            AnchorBottom = 1f,
            AnchorRight = 0.55f,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var margin = ThemeAncient.FullRect(new MarginContainer());
        margin.AddThemeConstantOverride("margin_left", 140);
        margin.AddThemeConstantOverride("margin_bottom", 60);
        AddChild(margin);

        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);
        column.AddChild(ThemeAncient.Label("FACSIMILIA", "TitleLabel"));
        column.AddChild(ThemeAncient.Label("The known world, 300 BC. History is yours to rewrite.", "SubtleLabel", 22));
        var rule = ThemeAncient.Ornament(440);
        rule.Alignment = BoxContainer.AlignmentMode.Begin;
        column.AddChild(rule);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        var buttons = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        buttons.AddThemeConstantOverride("separation", 12);
        column.AddChild(buttons);
        var newGame = MenuButton("New Game", "new_game", () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn"));
        buttons.AddChild(newGame);
        var continueButton = MenuButton("Continue", "continue", () =>
        {
            SaveSystem.ContinueRequested = true;
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        });
        continueButton.Disabled = !SaveSystem.HasSave();
        if (continueButton.Disabled)
            continueButton.TooltipText = "No saved game yet.";
        buttons.AddChild(continueButton);
        buttons.AddChild(MenuButton("Settings", "settings", OpenSettings));
        buttons.AddChild(MenuButton("Quit to Desktop", "quit", () => GetTree().Quit()));

        var footer = ThemeAncient.Label(
            "Map data: Natural Earth, historical-basemaps · Population: HYDE 3.2.1 (PBL / Utrecht University)", "SmallLabel");
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        footer.GrowVertical = GrowDirection.Begin;
        footer.Position = new Vector2(140, -44);
        AddChild(footer);

        newGame.GrabFocus();
    }

    static Button MenuButton(string text, string icon, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            Icon = ThemeAncient.Icon(icon),
            ThemeTypeVariation = "BigButton",
            Alignment = HorizontalAlignment.Left,
        };
        button.Pressed += onPressed;
        return button;
    }

    void OpenSettings()
    {
        if (_settingsPanel != null)
            return;
        _settingsPanel = SettingsPanelScene.Instantiate<SettingsPanel>();
        _settingsPanel.Theme = Theme;
        AddChild(_settingsPanel);
        _settingsPanel.Closed += () =>
        {
            _settingsPanel.QueueFree();
            _settingsPanel = null;
        };
    }
}
