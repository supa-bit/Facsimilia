using System.Collections.Generic;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// In-game overlay opened by GameRoot on Escape. Only emits action signals:
/// it holds no map reference and does no saving itself (GameRoot owns the map
/// and the async save), the same split as the HUD.
/// </summary>
public partial class PauseMenu : Control
{
    [Signal] public delegate void ResumeRequestedEventHandler();
    [Signal] public delegate void SaveAndReturnToMenuRequestedEventHandler();
    [Signal] public delegate void SaveAndQuitRequestedEventHandler();

    static readonly PackedScene SettingsPanelScene = GD.Load<PackedScene>("res://scenes/SettingsPanel.tscn");

    readonly List<Button> _buttons = new();
    Label _status = null!;
    CenterContainer _center = null!;
    SettingsPanel? _settingsPanel;

    public override void _Ready()
    {
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(ThemeAncient.FullRect(new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.7f) }));
        _center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(_center);
        var panel = new PanelContainer();
        _center.AddChild(panel);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);
        vbox.AddChild(ThemeAncient.Label("Paused", "HeaderLabel", align: HorizontalAlignment.Center));
        vbox.AddChild(ThemeAncient.Ornament(300));

        AddButton(vbox, "Resume", "end_turn", () => EmitSignal(SignalName.ResumeRequested)).GrabFocus();
        AddButton(vbox, "Settings", "settings", OpenSettings);
        AddButton(vbox, "Save and Return to Main Menu", "save", () => EmitSignal(SignalName.SaveAndReturnToMenuRequested));
        AddButton(vbox, "Save and Quit to Desktop", "quit", () => EmitSignal(SignalName.SaveAndQuitRequested));

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(380, 0) };
        vbox.AddChild(_status);
    }

    Button AddButton(VBoxContainer box, string text, string icon, System.Action onPressed)
    {
        var button = new Button { Text = text, Icon = ThemeAncient.Icon(icon) };
        button.Pressed += onPressed;
        box.AddChild(button);
        _buttons.Add(button);
        return button;
    }

    public void OpenSettings()
    {
        if (_settingsPanel != null)
            return;
        _settingsPanel = SettingsPanelScene.Instantiate<SettingsPanel>();
        _settingsPanel.Theme = Theme;
        AddChild(_settingsPanel);
        _center.Visible = false;  // one panel at a time
        _settingsPanel.Closed += () =>
        {
            _settingsPanel.QueueFree();
            _settingsPanel = null;
            _center.Visible = true;
        };
    }

    /// <summary>Called by GameRoot while a save is in flight, so it can't be triggered twice.</summary>
    public void SetBusy(bool busy)
    {
        foreach (var b in _buttons)
            b.Disabled = busy;
    }

    public void ShowMessage(string text) => _status.Text = text;
}
