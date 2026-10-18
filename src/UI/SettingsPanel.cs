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
        var size = new OptionButton();
        foreach (float f in SettingsStore.UiScales)
        {
            size.AddItem($"{f * 100:0}%");
            if (Mathf.IsEqualApprox(f, store.UiScale))
                size.Select(size.ItemCount - 1);
        }
        size.ItemSelected += index => store.SetUiScale(SettingsStore.UiScales[(int)index]);
        display.AddChild(Row("Interface size", size, "Makes every panel, button and line of text larger or smaller."));
        var sight = new OptionButton();
        sight.AddItem("As usual", (int)ColourSight.Usual);
        sight.AddItem("Red-green colour blindness", (int)ColourSight.RedGreen);
        sight.AddItem("Blue-yellow colour blindness", (int)ColourSight.BlueYellow);
        sight.Select(sight.GetItemIndex((int)store.Sight));
        sight.ItemSelected += index => store.SetSight((ColourSight)sight.GetItemId((int)index));
        display.AddChild(Row("Map view colours", sight,
            "Colours for the map views (loyalty, wealth, cultures, diplomacy and the rest) that colour-blind eyes tell apart: blue against orange for red-green " +
            "(the most common kind), teal against red for blue-yellow. The political map keeps history's colours for the realms."));

        var audio = Tab(tabs, "Audio");
        var volume = new HSlider
        {
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0, MaxValue = 1, Step = 0.01, Value = store.MasterVolume,
        };
        volume.ValueChanged += value => store.SetMasterVolume((float)value);
        audio.AddChild(Row("Master Volume", volume));
        var speak = new CheckBox { ButtonPressed = store.VoiceEnabled };
        speak.Toggled += on => store.SetVoiceEnabled(on);
        audio.AddChild(Row("Narrators speak", speak, "The bard, the chronicler and the newsreader read out the great moments."));
        var voiceVolume = new HSlider
        {
            CustomMinimumSize = new Vector2(200, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MinValue = 0, MaxValue = 1, Step = 0.01, Value = store.VoiceVolume,
        };
        voiceVolume.ValueChanged += value => store.SetVoiceVolume((float)value);
        audio.AddChild(Row("Voice Volume", voiceVolume));

        var gameplay = Tab(tabs, "Gameplay");
        var autosave = new OptionButton();
        foreach (int years in SaveSystem.AutosaveIntervals)
        {
            autosave.AddItem(years == 0 ? "Off" : $"Every {years} years", years);
            if (years == store.AutosaveInterval)
                autosave.Select(autosave.ItemCount - 1);
        }
        autosave.ItemSelected += index => store.SetAutosaveInterval(autosave.GetItemId((int)index));
        gameplay.AddChild(Row("Autosave", autosave,
            "How often the game saves itself. Autosaves take turns in three autosave slots and never replace your own saves."));
        var fight = new CheckBox { ButtonPressed = store.FightBattles };
        fight.Toggled += on => store.SetFightBattles(on);
        gameplay.AddChild(Row("Fight battles yourself", fight,
            "When your army meets an enemy, time stops and the battle map opens. Off: your generals fight every battle for you."));
        var thin = new CheckBox { ButtonPressed = store.ThinOldYears };
        thin.Toggled += on => store.SetThinOldYears(on);
        gameplay.AddChild(Row("Thin out old years", thin,
            "Off: saves keep everything - the whole chronicle, every battle report, every province's history. On: years more than a century old are kept in brief " +
            "(the chronicle keeps wars, conquests, revolts, disasters and successions; battle reports keep their numbers but not the blow-by-blow), so very long games save faster."));
        var history = new CheckBox { ButtonPressed = store.ScoreHistory };
        history.Toggled += on => store.SetScoreHistory(on);
        gameplay.AddChild(Row("Score closeness to history", history,
            "On: your score has a part for how closely the world has followed history (2 points for each of history's campaigns that turned out as it did). " +
            "Off: that part is left out, for a game where you mean to change history."));
        var opening = new CheckBox { ButtonPressed = store.ShowOpening };
        opening.Toggled += on => store.SetShowOpening(on);
        gameplay.AddChild(Row("Show the opening", opening,
            "A new game begins with the story from Göbekli Tepe to 300 BC. It can always be skipped."));

        var time = Tab(tabs, "Time");
        time.AddChild(ThemeAncient.Label("Tick what stops the clock when it happens to your realm. Your own battles always stop it when you fight them yourself, and so does the end of the game.",
            "SubtleLabel", 15));
        foreach (var reason in TimeStops.All)
        {
            var tick = new CheckBox { ButtonPressed = store.TimeStops.Contains(reason.Id) };
            string id = reason.Id;
            tick.Toggled += on => store.SetTimeStop(id, on);
            time.AddChild(Row(reason.Name, tick, reason.Detail));
        }

        var keys = Tab(tabs, "Keys");
        keys.AddChild(ThemeAncient.Label("Click an action, then press its new key (Esc to cancel). Esc for the menu and the arrow keys for moving the map always work too.",
            "SubtleLabel", 15));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(440, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        keys.AddChild(scroll);
        _keyRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _keyRows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_keyRows);
        var reset = new Button { Text = "Put every key back as it was" };
        reset.Pressed += () => { store.ResetKeys(); FillKeys(); };
        keys.AddChild(reset);
        FillKeys();

        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        vbox.AddChild(back);
        back.GrabFocus();
    }

    VBoxContainer _keyRows = null!;
    string? _capturing;   // the action waiting for its new key

    /// <summary>One row per action (your answer "Changeable keys"): its name and its key, click to change.</summary>
    void FillKeys()
    {
        foreach (Node child in _keyRows.GetChildren())
            child.QueueFree();
        foreach (var action in KeyBindings.All)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = action.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill });
            string id = action.Id;
            var b = new Button
            {
                Text = _capturing == id ? "Press a key..." : KeyBindings.Name(KeyBindings.KeyFor(id)),
                CustomMinimumSize = new Vector2(140, 0),
                FocusMode = FocusModeEnum.None,
            };
            b.Pressed += () => { _capturing = id; FillKeys(); };
            row.AddChild(b);
            _keyRows.AddChild(row);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_capturing == null || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode != Key.Escape)
            SettingsStore.Instance.SetKey(_capturing, key.Keycode);
        _capturing = null;
        FillKeys();
        GetViewport().SetInputAsHandled();
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
