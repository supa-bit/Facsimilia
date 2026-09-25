using System.Collections.Generic;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// In-game overlay opened by GameRoot on Escape. Only emits action signals:
/// it holds no map reference and does no saving itself (GameRoot owns the map
/// and the async save), the same split as the HUD. Leaving without saving
/// asks for confirmation first, on a second page of the same panel.
/// </summary>
public partial class PauseMenu : Control
{
    [Signal] public delegate void ResumeRequestedEventHandler();
    /// <summary>Save to this slot, then: 0 stay in the pause menu, 1 return to the main menu, 2 quit.</summary>
    [Signal] public delegate void SaveRequestedEventHandler(string slot, int then);

    public const int ThenStay = 0, ThenMainMenu = 1, ThenQuit = 2;

    /// <summary>
    /// The slot this game was loaded from or last saved to, set by GameRoot;
    /// "Save and ..." saves there without asking. Null for a new game (or one
    /// loaded from the autosave): they ask for a slot first.
    /// </summary>
    public string? CurrentSlot { get; set; }
    [Signal] public delegate void ReturnToMenuWithoutSavingRequestedEventHandler();
    [Signal] public delegate void QuitWithoutSavingRequestedEventHandler();

    static readonly PackedScene SettingsPanelScene = GD.Load<PackedScene>("res://scenes/SettingsPanel.tscn");

    readonly List<Button> _buttons = new();
    Label _status = null!;
    CenterContainer _center = null!;
    VBoxContainer _mainPage = null!;
    VBoxContainer _confirmPage = null!;
    Label _confirmText = null!;
    Button _confirmLeave = null!;
    Button _confirmCancel = null!;
    StringName? _pendingLeave;  // the signal the confirmation page will emit
    SettingsPanel? _settingsPanel;
    SaveSlotsPanel? _slotsPanel;
    Button _saveAndMenu = null!;
    Button _saveAndQuit = null!;

    public override void _Ready()
    {
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(ThemeAncient.FullRect(new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.7f) }));
        _center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(_center);
        var panel = new PanelContainer();
        _center.AddChild(panel);

        var pages = new VBoxContainer();
        panel.AddChild(pages);
        _mainPage = Page(pages, "Paused");
        AddButton(_mainPage, "Resume", "end_turn", () => EmitSignal(SignalName.ResumeRequested)).GrabFocus();
        AddButton(_mainPage, "Save Game...", "save", () => ChooseSlot(ThenStay));
        AddButton(_mainPage, "Settings", "settings", OpenSettings);
        _mainPage.AddChild(new HSeparator());
        _saveAndMenu = AddButton(_mainPage, "Save and Return to Main Menu", "save", () => SaveThen(ThenMainMenu));
        _saveAndQuit = AddButton(_mainPage, "Save and Quit to Desktop", "save", () => SaveThen(ThenQuit));
        _mainPage.AddChild(new HSeparator());
        AddButton(_mainPage, "Return to Main Menu Without Saving", "quit",
            () => AskToLeave(SignalName.ReturnToMenuWithoutSavingRequested, "Return to the main menu without saving?"));
        AddButton(_mainPage, "Quit to Desktop Without Saving", "quit",
            () => AskToLeave(SignalName.QuitWithoutSavingRequested, "Quit to the desktop without saving?"));
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(380, 0), Visible = false };
        _mainPage.AddChild(_status);
        _mainPage.AddChild(ThemeAncient.Label(MainMenu.BuildStamp(), "SubtleLabel", 15, HorizontalAlignment.Center));

        _confirmPage = Page(pages, "Leave Without Saving");
        _confirmPage.Visible = false;
        _confirmText = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(380, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _confirmPage.AddChild(_confirmText);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        _confirmPage.AddChild(row);
        _confirmLeave = new Button { Text = "Leave Without Saving", Icon = ThemeAncient.Icon("quit") };
        _confirmLeave.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.52f));
        _confirmLeave.AddThemeColorOverride("icon_normal_color", new Color(1f, 0.62f, 0.52f));
        _confirmLeave.Pressed += () => EmitSignal(_pendingLeave!);
        row.AddChild(_confirmLeave);
        _confirmCancel = new Button { Text = "Cancel" };
        _confirmCancel.Pressed += () => ShowPage(confirm: false);
        row.AddChild(_confirmCancel);
    }

    static VBoxContainer Page(Container parent, string title)
    {
        var page = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        page.AddThemeConstantOverride("separation", 10);
        page.AddChild(ThemeAncient.Label(title, "HeaderLabel", align: HorizontalAlignment.Center));
        page.AddChild(ThemeAncient.Ornament(300));
        parent.AddChild(page);
        return page;
    }

    void AskToLeave(StringName signal, string question)
    {
        _pendingLeave = signal;
        _confirmText.Text = question + " Everything since your last save will be lost.";
        ShowPage(confirm: true);
    }

    void ShowPage(bool confirm)
    {
        _mainPage.Visible = !confirm;
        _confirmPage.Visible = confirm;
        if (confirm)
            _confirmCancel.GrabFocus();  // the safe choice is the default
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

    public override void _Process(double delta)
    {
        string where = CurrentSlot == null ? "" : $" ({SaveSystem.SlotName(CurrentSlot)})";
        _saveAndMenu.Text = "Save and Return to Main Menu" + where;
        _saveAndQuit.Text = "Save and Quit to Desktop" + where;
    }

    void SaveThen(int then)
    {
        if (CurrentSlot != null)
            EmitSignal(SignalName.SaveRequested, CurrentSlot, then);
        else
            ChooseSlot(then);
    }

    void ChooseSlot(int then)
    {
        if (_slotsPanel != null)
            return;
        _slotsPanel = new SaveSlotsPanel(SaveSlotsPanel.Mode.Save) { Theme = Theme };
        AddChild(_slotsPanel);
        _center.Visible = false;
        void Close()
        {
            _slotsPanel!.QueueFree();
            _slotsPanel = null;
            _center.Visible = true;
        }
        _slotsPanel.SlotChosen += slot =>
        {
            Close();
            EmitSignal(SignalName.SaveRequested, slot, then);
        };
        _slotsPanel.Closed += Close;
    }

    /// <summary>Called by GameRoot while a save is in flight, so it can't be triggered twice.</summary>
    public void SetBusy(bool busy)
    {
        foreach (var b in _buttons)
            b.Disabled = busy;
    }

    public void ShowMessage(string text)
    {
        _status.Text = text;
        _status.Visible = text != "";
    }
}
