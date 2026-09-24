using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Reusable settings overlay, added as a child wherever it's needed (main
/// menu, or over the pause menu in-game), with a tab per group: Display,
/// Audio, Gameplay. Emits Closed instead of freeing itself, so the owner
/// decides what happens next. Every change is applied and saved at once
/// through SettingsStore.
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

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        vbox.AddThemeConstantOverride("separation", 14);
        panel.AddChild(vbox);
        vbox.AddChild(ThemeAncient.Label("Settings", "HeaderLabel", align: HorizontalAlignment.Center));
        vbox.AddChild(ThemeAncient.Ornament(300));

        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 170) };
        vbox.AddChild(tabs);
        var store = SettingsStore.Instance;

        var display = Tab(tabs, "Display");
        var fullscreen = new CheckBox { ButtonPressed = store.Fullscreen };
        fullscreen.Toggled += pressed => store.SetFullscreen(pressed);
        display.AddChild(Row("Fullscreen", fullscreen, "Also F11."));

        var audio = Tab(tabs, "Audio");
        var volume = new HSlider
        {
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0, MaxValue = 1, Step = 0.01, Value = store.MasterVolume,
        };
        volume.ValueChanged += value => store.SetMasterVolume((float)value);
        audio.AddChild(Row("Master Volume", volume));

        var gameplay = Tab(tabs, "Gameplay");
        var autosave = new OptionButton();
        foreach (int years in SaveSystem.AutosaveIntervals)
        {
            autosave.AddItem(years switch { 0 => "Off", 1 => "Every year", _ => $"Every {years} years" }, years);
            if (years == store.AutosaveInterval)
                autosave.Select(autosave.ItemCount - 1);
        }
        autosave.ItemSelected += index => store.SetAutosaveInterval(autosave.GetItemId((int)index));
        gameplay.AddChild(Row("Autosave", autosave,
            "How often the game saves itself. Autosaves take turns in three autosave slots and never replace your own saves."));

        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        vbox.AddChild(back);
        back.GrabFocus();
    }

    static VBoxContainer Tab(TabContainer tabs, string title)
    {
        var margin = new MarginContainer { Name = title };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 12);
        tabs.AddChild(margin);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 12);
        margin.AddChild(page);
        return page;
    }

    static Control Row(string text, Control control, string? hint = null)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(control);
        box.AddChild(row);
        if (hint != null)
        {
            var note = ThemeAncient.Label(hint, "SubtleLabel", 15);
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.CustomMinimumSize = new Vector2(420, 0);
            box.AddChild(note);
        }
        return box;
    }
}
