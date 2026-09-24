using System;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The save slot list, as an overlay. Load mode (main menu) lists every
/// save including the autosave, with Load and Delete. Save mode (pause menu)
/// lists the three manual slots; picking a used one asks before
/// overwriting it. Emits SlotChosen (after any confirmation) or Closed and
/// never loads or saves itself; the owner does that.
/// </summary>
public partial class SaveSlotsPanel : Control
{
    [Signal] public delegate void SlotChosenEventHandler(string slot);
    [Signal] public delegate void ClosedEventHandler();

    public enum Mode { Load, Save }

    readonly Mode _mode;
    VBoxContainer _listPage = null!;
    VBoxContainer _rows = null!;
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
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 10);
        _listPage.AddChild(_rows);
        var back = new Button { Text = "Back" };
        back.Pressed += () => EmitSignal(SignalName.Closed);
        _listPage.AddChild(back);

        _confirmPage = Page(pages, _mode == Mode.Save ? "Overwrite Save" : "Delete Save");
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

    void BuildRows()
    {
        foreach (var child in _rows.GetChildren())
            child.QueueFree();
        Button? first = null;
        var slots = _mode == Mode.Save ? SaveSystem.ManualSlots : [.. SaveSystem.ManualSlots, SaveSystem.AutosaveSlot];
        foreach (string slot in slots)
        {
            var info = SaveSystem.ReadInfo(slot);
            var button = SlotRow(slot, info);
            first ??= button.Disabled ? null : button;
        }
        if (_mode == Mode.Load && SaveSystem.ListSaves().Count == 0)
            _rows.AddChild(ThemeAncient.Label("No saved games yet.", "SubtleLabel", align: HorizontalAlignment.Center));
        (first ?? (Control)_listPage.GetChild(_listPage.GetChildCount() - 1)).CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>One slot: color bar, name and summary, and its action button(s).</summary>
    Button SlotRow(string slot, SaveInfo? info)
    {
        var card = new PanelContainer { ThemeTypeVariation = "CardPanel" };
        _rows.AddChild(card);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        row.AddChild(new ColorRect
        {
            Color = info?.RealmColor ?? new Color(0.25f, 0.22f, 0.18f),
            CustomMinimumSize = new Vector2(8, 0),
        });

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        row.AddChild(text);
        string title = SaveSystem.SlotName(slot) + (info == null ? " — empty" : $" — {info.RealmName}");
        text.AddChild(ThemeAncient.Label(title, fontSize: 20));
        if (info != null)
            text.AddChild(ThemeAncient.Label(
                $"{ThemeAncient.YearText(info.Year)} · Ruler: {info.Ruler} · Saved {info.SavedText()}", "SubtleLabel", 16));

        var main = new Button { SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(96, 0) };
        row.AddChild(main);
        if (_mode == Mode.Save)
        {
            main.Text = "Save";
            main.Icon = ThemeAncient.Icon("save");
            main.Pressed += () =>
            {
                if (info == null)
                    EmitSignal(SignalName.SlotChosen, slot);
                else
                    Confirm($"Overwrite {SaveSystem.SlotName(slot)} ({info.RealmName}, {ThemeAncient.YearText(info.Year)})? " +
                        "That saved game will be replaced.", "Overwrite", () => EmitSignal(SignalName.SlotChosen, slot));
            };
        }
        else
        {
            main.Text = "Load";
            main.Icon = ThemeAncient.Icon("continue");
            main.Disabled = info == null;
            main.Pressed += () => EmitSignal(SignalName.SlotChosen, slot);
            if (info != null)
            {
                var delete = new Button { Text = "Delete", SizeFlagsVertical = SizeFlags.ShrinkCenter };
                delete.Pressed += () => Confirm(
                    $"Delete {SaveSystem.SlotName(slot)} ({info.RealmName}, {ThemeAncient.YearText(info.Year)})? This can't be undone.",
                    "Delete", () =>
                    {
                        SaveSystem.DeleteSave(slot);
                        ShowConfirm(false);
                        BuildRows();
                    });
                row.AddChild(delete);
            }
        }
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
