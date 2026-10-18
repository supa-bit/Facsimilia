using System;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The list of saves, as an overlay. Load mode (main menu) lists every
/// save including the autosaves, newest first, with Load and Delete. Save
/// mode (pause menu) has a box for the new save's name (your answer "Named
/// saves": any number of them), filled in with your realm and the year, and
/// below it your own saves to save over or delete; a name already used asks
/// before overwriting. Emits SlotChosen (after any confirmation) or Closed
/// and never loads or saves itself; the owner does that.
/// </summary>
public partial class SaveSlotsPanel : Control
{
    /// <summary>The save to load or write; name is a new save's name ("" when saving over one, which keeps its own).</summary>
    [Signal] public delegate void SlotChosenEventHandler(string slot, string name);
    [Signal] public delegate void ClosedEventHandler();

    public enum Mode { Load, Save }

    readonly Mode _mode;
    /// <summary>Save mode: the name the box starts with.</summary>
    public string DefaultName { get; set; } = "";
    VBoxContainer _listPage = null!;
    VBoxContainer _rows = null!;
    LineEdit? _nameBox;
    ScrollContainer _scroll = null!;
    VBoxContainer _confirmPage = null!;
    Label _confirmText = null!;
    Button _confirmYes = null!;
    Button _confirmNo = null!;
    Action? _onConfirm;

    public SaveSlotsPanel() : this(Mode.Load) { }

    public SaveSlotsPanel(Mode mode) => _mode = mode;

    public override void _Ready()
    {
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(ThemeAncient.FullRect(new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.7f) }));
        var center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var pages = new VBoxContainer();
        panel.AddChild(pages);

        _listPage = Page(pages, _mode == Mode.Save ? "Save Game" : "Load Game");
        if (_mode == Mode.Save)
            _listPage.AddChild(NameRow());
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(600, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _listPage.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_rows);
        _scroll = scroll;
        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        _listPage.AddChild(back);

        _confirmPage = Page(pages, "Are You Sure?");
        _confirmPage.Visible = false;
        _confirmText = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(520, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _confirmPage.AddChild(_confirmText);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        _confirmPage.AddChild(row);
        _confirmYes = new Button();
        _confirmYes.AddThemeColorOverride("font_color", new Color(1f, 0.62f, 0.52f));
        _confirmYes.Pressed += () => _onConfirm?.Invoke();
        row.AddChild(_confirmYes);
        _confirmNo = new Button { Text = "Cancel" };
        _confirmNo.Pressed += () => ShowConfirm(false);
        row.AddChild(_confirmNo);

        BuildRows();
    }

    static VBoxContainer Page(Container parent, string title)
    {
        var page = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        page.AddThemeConstantOverride("separation", 12);
        page.AddChild(ThemeAncient.Label(title, "HeaderLabel", align: HorizontalAlignment.Center));
        page.AddChild(ThemeAncient.Ornament(360));
        parent.AddChild(page);
        return page;
    }

    /// <summary>Save mode: the new save's name, and its Save button.</summary>
    Control NameRow()
    {
        var box = new VBoxContainer();
        box.AddChild(ThemeAncient.Label("Name this save:", "SubtleLabel", 16));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        box.AddChild(row);
        _nameBox = new LineEdit { Text = DefaultName, MaxLength = 60, SizeFlagsHorizontal = SizeFlags.ExpandFill, SelectAllOnFocus = true };
        row.AddChild(_nameBox);
        var save = new Button { Text = "Save", Icon = ThemeAncient.Icon("save"), CustomMinimumSize = new Vector2(96, 0) };
        row.AddChild(save);
        void SaveNamed()
        {
            string name = _nameBox.Text.Trim();
            string slot = SaveSystem.SlotFor(name);
            if (name == "" || slot == "s_")
                return;
            var info = SaveSystem.ReadInfo(slot);
            if (info == null)
                EmitSignal(SignalName.SlotChosen, slot, name);
            else
                Confirm($"There is already a save called {SaveSystem.SlotName(slot)} ({info.RealmName}, {ThemeAncient.YearText(info.Year)}). " +
                    "Overwrite it? That saved game will be replaced.", "Overwrite", () => EmitSignal(SignalName.SlotChosen, slot, name));
        }
        save.Pressed += SaveNamed;
        _nameBox.TextSubmitted += _ => SaveNamed();
        _nameBox.TextChanged += text => save.Disabled = SaveSystem.SlotFor(text) == "s_";
        _nameBox.CallDeferred(Control.MethodName.GrabFocus);
        return box;
    }

    void BuildRows()
    {
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        Button? first = null;
        var saves = _mode == Mode.Save ? SaveSystem.ListOwnSaves() : SaveSystem.ListSaves();
        if (_mode == Mode.Save && saves.Count > 0)
            _rows.AddChild(ThemeAncient.Label("Or save over one of your saves:", "SubtleLabel", 16));
        foreach (var info in saves)
        {
            var button = SlotRow(info.Slot, info);
            first ??= button;
        }
        if (_mode == Mode.Load && saves.Count == 0)
            _rows.AddChild(ThemeAncient.Label("No saved games yet.", "SubtleLabel", align: HorizontalAlignment.Center));
        Callable.From(FitList).CallDeferred();
        if (_nameBox == null)
            (first ?? (Control)_listPage.GetChild(_listPage.GetChildCount() - 1)).CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>The list as tall as its saves, up to a screenful; more saves scroll.</summary>
    void FitList() => _scroll.CustomMinimumSize = new Vector2(600, Mathf.Min(_rows.GetCombinedMinimumSize().Y, 440));

    /// <summary>One save: color bar, name and summary, and its buttons.</summary>
    Button SlotRow(string slot, SaveInfo info)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardPanel" };
        _rows.AddChild(card);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        row.AddChild(new ColorRect
        {
            Color = info.RealmColor,
            CustomMinimumSize = new Vector2(8, 0),
        });

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        row.AddChild(text);
        string name = SaveSystem.SlotName(slot);
        string title = SaveSystem.IsNamed(slot) ? name : $"{name} — {info.RealmName}";
        text.AddChild(ThemeAncient.Label(title, fontSize: 20));
        text.AddChild(ThemeAncient.Label(
            (SaveSystem.IsNamed(slot) ? info.RealmName + " · " : "") +
            $"{ThemeAncient.YearText(info.Year)} · Ruler: {info.Ruler} · Saved {info.SavedText()}", "SubtleLabel", 16));

        var main = new Button { SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(96, 0) };
        row.AddChild(main);
        if (_mode == Mode.Save)
        {
            main.Text = "Save over";
            main.Icon = ThemeAncient.Icon("save");
            main.Pressed += () => Confirm($"Overwrite {name} ({info.RealmName}, {ThemeAncient.YearText(info.Year)})? " +
                "That saved game will be replaced.", "Overwrite", () => EmitSignal(SignalName.SlotChosen, slot, ""));
        }
        else
        {
            main.Text = "Load";
            main.Icon = ThemeAncient.Icon("continue");
            main.Pressed += () => EmitSignal(SignalName.SlotChosen, slot, "");
        }
        var delete = new Button { Text = "Delete", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        delete.Pressed += () => Confirm(
            $"Delete {name} ({info.RealmName}, {ThemeAncient.YearText(info.Year)})? This can't be undone.",
            "Delete", () =>
            {
                SaveSystem.DeleteSave(slot);
                ShowConfirm(false);
                BuildRows();
            });
        row.AddChild(delete);
        return main;
    }

    void Confirm(string question, string yes, Action onYes)
    {
        _confirmText.Text = question;
        _confirmYes.Text = yes;
        _onConfirm = onYes;
        ShowConfirm(true);
    }

    void ShowConfirm(bool confirm)
    {
        _listPage.Visible = !confirm;
        _confirmPage.Visible = confirm;
        if (confirm)
            _confirmNo.GrabFocus();  // the safe choice is the default
    }
}
