using System;
using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The boot scene (project.godot run/main_scene). "New Game" goes to Main.tscn,
/// whose GameRoot runs the intro and realm selection; "Continue" (the most
/// recent save) and "Load Game" (any slot) set SaveSystem.PendingLoadSlot so
/// GameRoot loads that save instead.
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
        SaveSystem.MigrateLegacySave();
        var latest = SaveSystem.MostRecent();
        var continueButton = MenuButton("Continue", "continue", () => LoadSlot(latest!.Slot));
        continueButton.Disabled = latest == null;
        continueButton.TooltipText = latest == null
            ? "No saved game yet."
            : $"Your most recent save: {SaveSystem.SlotName(latest.Slot)}, saved {latest.SavedText()}.";
        buttons.AddChild(continueButton);
        if (latest != null)
        {
            var summary = ThemeAncient.Label($"{latest.RealmName} · {ThemeAncient.YearText(latest.Year)} · {SaveSystem.SlotName(latest.Slot)}",
                "SubtleLabel", 17);
            buttons.AddChild(summary);
        }
        var loadButton = MenuButton("Load Game", "log", OpenLoadPanel);
        loadButton.Disabled = latest == null;
        buttons.AddChild(loadButton);
        buttons.AddChild(MenuButton("Settings", "settings", OpenSettings));
        buttons.AddChild(MenuButton("Quit to Desktop", "quit", () => GetTree().Quit()));

        var footer = ThemeAncient.Label(
            "Map data: Natural Earth · Population: HYDE 3.2.1 (PBL / Utrecht University)\n" +
            "Land: CHELSA climate (Karger et al.), SoilGrids (ISRIC, CC BY 4.0), ETOPO 2022 (NOAA), " +
            "KK10 land use (Kaplan et al., CC BY 3.0), USGS MRDS · Places: Pleiades gazetteer (CC BY 3.0)", "SmallLabel");
        footer.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        footer.GrowVertical = GrowDirection.Begin;
        footer.Position = new Vector2(140, -62);
        AddChild(footer);

        var stamp = ThemeAncient.Label(BuildStamp(), fontSize: 18, align: HorizontalAlignment.Right);
        stamp.AddThemeColorOverride("font_color", new Color(0.93f, 0.84f, 0.62f));
        stamp.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        stamp.AddThemeConstantOverride("outline_size", 4);
        // Pinned to the bottom-right corner, growing up and left to fit its text.
        stamp.SetAnchorsPreset(LayoutPreset.BottomRight);
        stamp.GrowHorizontal = GrowDirection.Begin;
        stamp.GrowVertical = GrowDirection.Begin;
        stamp.OffsetLeft = stamp.OffsetRight = -24;
        stamp.OffsetTop = stamp.OffsetBottom = -34;
        stamp.TooltipText = "The version you're running. If this doesn't match the latest change, pull again or rebuild.";
        stamp.MouseFilter = MouseFilterEnum.Pass;
        AddChild(stamp);

        newGame.GrabFocus();
    }

    /// <summary>"Build a8f0a45 · compiled 2026-09-24 18:02 UTC", from attributes Facsimilia.csproj stamps in.</summary>
    public static string BuildStamp()
    {
        string commit = BuildCommit();
        return $"Build {(commit == "" ? "(unknown commit)" : commit)} · compiled {Meta("BuiltUtc")} UTC";
    }

    /// <summary>The commit this code was compiled from, or "" if unknown.</summary>
    public static string BuildCommit() => Meta("Commit");

    static string Meta(string key) => typeof(MainMenu).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value ?? "";

    void LoadSlot(string slot)
    {
        SaveSystem.PendingLoadSlot = slot;
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    void OpenLoadPanel()
    {
        var panel = new SaveSlotsPanel(SaveSlotsPanel.Mode.Load) { Theme = Theme };
        AddChild(panel);
        panel.SlotChosen += LoadSlot;
        panel.Closed += () =>
        {
            panel.QueueFree();
            GetTree().ReloadCurrentScene();  // saves may have been deleted: refresh Continue
        };
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
