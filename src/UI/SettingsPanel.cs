using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Reusable settings overlay, added as a child wherever it's needed (main
/// menu, or over the pause menu in-game). Emits Closed instead of freeing
/// itself, so the owner decides what happens next.
/// </summary>
public partial class SettingsPanel : Control
{
    [Signal] public delegate void ClosedEventHandler();

    public override void _Ready()
    {
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        var veil = ThemeAncient.FullRect(new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.7f) });
        AddChild(veil);
        var center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);
        vbox.AddChild(ThemeAncient.Label("Settings", "HeaderLabel", align: HorizontalAlignment.Center));
        vbox.AddChild(ThemeAncient.Ornament(300));

        var store = SettingsStore.Instance;
        var fullscreen = new CheckBox { ButtonPressed = store.Fullscreen };
        fullscreen.Toggled += pressed => store.SetFullscreen(pressed);
        vbox.AddChild(Row("Fullscreen", fullscreen));

        var volume = new HSlider
        {
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0, MaxValue = 1, Step = 0.01, Value = store.MasterVolume,
        };
        volume.ValueChanged += value => store.SetMasterVolume((float)value);
        vbox.AddChild(Row("Master Volume", volume));

        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        vbox.AddChild(back);
        back.GrabFocus();
    }

    static HBoxContainer Row(string text, Control control)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(label);
        row.AddChild(control);
        return row;
    }
}
