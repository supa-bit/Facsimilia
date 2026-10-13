using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The Voices screen (main menu): every spoken line with its text, both
/// recorded takes to listen to, and a choice of which one the game plays.
/// Lines not chosen play take A. Choices are kept at once (Voice.Pick).
/// Only the recordings already in the project are played: nothing here
/// generates speech or costs anything.
/// </summary>
public partial class VoicePicker : Control
{
    [Signal] public delegate void ClosedEventHandler();

    const int PerPage = 20;

    VoiceCatalog _catalog = null!;
    OptionButton _group = null!, _voice = null!;
    CheckBox _undecided = null!;
    Label _progress = null!, _pageLabel = null!;
    VBoxContainer _rows = null!;
    Button _prev = null!, _next = null!;
    ScrollContainer _scroll = null!;
    readonly List<string> _voices = new();
    List<VoiceLine> _shown = new();
    int _page;

    public override void _Ready()
    {
        _catalog = Voice.Instance!.Catalog;
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(ThemeAncient.FullRect(new ColorRect { Color = new Color(0.03f, 0.02f, 0.01f, 0.8f) }));
        var center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(center);
        var panel = new PanelContainer();
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1240, 0) };
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        box.AddChild(ThemeAncient.Label("Voices", "HeaderLabel", align: HorizontalAlignment.Center));
        box.AddChild(ThemeAncient.Ornament(300));
        var about = ThemeAncient.Label(
            "Every line was recorded twice. Listen to take A and take B and choose the one the game should play; " +
            $"lines you haven't chosen play take A. Your choices are kept at once, in {Voice.PicksFile}.", "SubtleLabel", 16);
        about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        about.CustomMinimumSize = new Vector2(1200, 0);
        box.AddChild(about);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 12);
        box.AddChild(filters);
        filters.AddChild(new Label { Text = "Show" });
        _group = new OptionButton { FocusMode = FocusModeEnum.None };
        _group.AddItem($"Every line ({_catalog.All.Count})");
        foreach (var (plays, name) in VoiceCatalog.Groups)
            _group.AddItem($"{name} ({_catalog.All.Count(l => l.Plays == plays)})");
        _group.ItemSelected += _ => Filter();
        filters.AddChild(_group);
        filters.AddChild(new Label { Text = "spoken by" });
        _voice = new OptionButton { FocusMode = FocusModeEnum.None };
        _voice.AddItem("anyone");
        foreach (string v in _catalog.All.Select(l => l.Voice).Distinct().OrderBy(v => v))
        {
            _voices.Add(v);
            _voice.AddItem($"the {v}");
        }
        _voice.ItemSelected += _ => Filter();
        filters.AddChild(_voice);
        _undecided = new CheckBox { Text = "Only lines not yet chosen", FocusMode = FocusModeEnum.None };
        _undecided.Toggled += _ => Filter();
        filters.AddChild(_undecided);
        filters.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _progress = new Label();
        filters.AddChild(_progress);

        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 640), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_rows);

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pager.AddThemeConstantOverride("separation", 16);
        box.AddChild(pager);
        _prev = new Button { Text = "◀ Previous", FocusMode = FocusModeEnum.None };
        _prev.Pressed += () => Turn(-1);
        pager.AddChild(_prev);
        _pageLabel = new Label();
        pager.AddChild(_pageLabel);
        _next = new Button { Text = "Next ▶", FocusMode = FocusModeEnum.None };
        _next.Pressed += () => Turn(1);
        pager.AddChild(_next);
        pager.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });
        var back = new Button { Text = "Back" };
        back.Pressed += () =>
        {
            Voice.Instance!.Stop();
            EmitSignal(SignalName.Closed);
        };
        pager.AddChild(back);

        Filter();
    }

    void Filter()
    {
        IEnumerable<VoiceLine> lines = _catalog.All;
        if (_group.Selected > 0)
            lines = lines.Where(l => l.Plays == VoiceCatalog.Groups[_group.Selected - 1].Plays);
        if (_voice.Selected > 0)
            lines = lines.Where(l => l.Voice == _voices[_voice.Selected - 1]);
        if (_undecided.ButtonPressed)
            lines = lines.Where(l => !_catalog.Picks.ContainsKey(l.Id));
        _shown = lines.ToList();
        _page = 0;
        Render();
    }

    int Pages => Mathf.Max(1, (_shown.Count + PerPage - 1) / PerPage);

    void Turn(int by)
    {
        _page = Mathf.Clamp(_page + by, 0, Pages - 1);
        Render();
    }

    void Render()
    {
        foreach (Node child in _rows.GetChildren())
            child.QueueFree();
        foreach (var line in _shown.Skip(_page * PerPage).Take(PerPage))
            _rows.AddChild(Row(line));
        if (_shown.Count == 0)
            _rows.AddChild(ThemeAncient.Label("No lines here: every one has been chosen.", "SubtleLabel", 16));
        _pageLabel.Text = $"Page {_page + 1} of {Pages}  ({_shown.Count} lines)";
        _prev.Disabled = _page == 0;
        _next.Disabled = _page >= Pages - 1;
        _scroll.ScrollVertical = 0;
        ShowProgress();
    }

    void ShowProgress() => _progress.Text = $"Chosen: {_catalog.Picks.Count} of {_catalog.All.Count}";

    Control Row(VoiceLine line)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        words.AddThemeConstantOverride("separation", 0);
        var text = ThemeAncient.Label(line.Subtitle, fontSize: 16);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.CustomMinimumSize = new Vector2(840, 0);
        words.AddChild(text);
        string group = VoiceCatalog.Groups.FirstOrDefault(g => g.Plays == line.Plays).Name ?? line.Plays;
        words.AddChild(ThemeAncient.Label($"{group} · the {line.Voice} · {line.Id}", "SubtleLabel", 13));
        row.AddChild(words);

        bool hasB = Voice.Exists(line.Id, VoiceCatalog.TakeB);
        foreach (string take in new[] { VoiceCatalog.Take, VoiceCatalog.TakeB })
        {
            string t = take;
            var play = new Button
            {
                Text = $"▶ {t.ToUpperInvariant()}",
                FocusMode = FocusModeEnum.None,
                Disabled = t == VoiceCatalog.TakeB && !hasB,
                TooltipText = t == VoiceCatalog.TakeB && !hasB ? "This line has only one take." : $"Listen to take {t.ToUpperInvariant()}.",
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            play.Pressed += () => Voice.Instance!.PlayTake(line.Id, t);
            row.AddChild(play);
        }
        var choices = new List<Button>();
        foreach (string take in new[] { VoiceCatalog.Take, VoiceCatalog.TakeB })
        {
            string t = take;
            var use = new Button
            {
                Text = $"Use {t.ToUpperInvariant()}",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                Disabled = t == VoiceCatalog.TakeB && !hasB,
                CustomMinimumSize = new Vector2(84, 0),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            };
            choices.Add(use);
            row.AddChild(use);
        }
        void Show()
        {
            bool chosen = _catalog.Picks.TryGetValue(line.Id, out string? pick);
            for (int i = 0; i < 2; i++)
            {
                bool on = chosen && pick == (i == 0 ? VoiceCatalog.Take : VoiceCatalog.TakeB);
                choices[i].SetPressedNoSignal(on);
                choices[i].Modulate = on ? new Color(1f, 0.85f, 0.45f) : Colors.White;
            }
            text.Modulate = chosen ? Colors.White : new Color(1f, 0.92f, 0.75f);
        }
        for (int i = 0; i < 2; i++)
        {
            string t = i == 0 ? VoiceCatalog.Take : VoiceCatalog.TakeB;
            choices[i].Pressed += () =>
            {
                Voice.Instance!.Pick(line.Id, t);
                Show();
                ShowProgress();
            };
        }
        Show();
        return row;
    }
}
