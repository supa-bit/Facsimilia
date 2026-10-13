using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The opening (decisions "What the opening shows", "Watching the opening",
/// "Is the opening simulated?"): the map from 10,000 BC to 300 BC. The years
/// run while the bard tells seven scenes; people spread as HYDE's record has
/// them; the ancient places light up in their years. Pause, slow down, hurry
/// on or skip; while paused, open a lit place or the step of the story to
/// hear it. Emits Finished (also when skipped). Plays only recordings in the
/// project (src/UI/Voice.cs): nothing here generates speech.
/// </summary>
public partial class Opening : Control
{
    [Signal] public delegate void FinishedEventHandler();

    const string TerrainPath = "res://data/terrain_texture.png";
    static readonly float[] Speeds = { 0.5f, 1f, 2f, 4f };
    const float WorldWidth = 2400f;
    const double Pad = 1.5;   // seconds of quiet at the end of each chapter

    const string FrameShader = @"
shader_type canvas_item;
uniform sampler2D frame_a : filter_linear;
uniform sampler2D frame_b : filter_linear;
uniform float mix_f = 0.0;
void fragment() {
    float v = mix(texture(frame_a, UV).r, texture(frame_b, UV).r, mix_f);
    // Hunters' few people barely show; farmland glows; towns and cities shine.
    vec3 warm = mix(vec3(0.85, 0.55, 0.2), vec3(1.0, 0.95, 0.78), smoothstep(0.55, 0.9, v));
    COLOR = vec4(warm, smoothstep(0.22, 0.8, v) * 0.85);
}";

    OpeningScript _script = null!;
    VoiceCatalog _lines = null!;
    readonly List<Texture2D> _frames = new();
    Control _world = null!;
    ShaderMaterial _people = null!;
    readonly List<(OpeningSite Site, Button Marker, Label Name)> _sites = new();
    Label _year = null!, _stage = null!, _subtitle = null!, _speedLabel = null!;
    Button _pause = null!;
    PanelContainer _aside = null!;
    Label _asideTitle = null!, _asideText = null!, _photoCredit = null!;
    TextureRect _photo = null!;
    Dictionary<string, (string Author, string Licence, string File)> _photos = new();
    AudioStreamPlayer _narrator = null!, _voiceAside = null!;

    // Where the story is: chapter, fraction of it told (0..1), and what the narrator does next.
    int _chapter = -1;
    double _p;
    bool _yearSaid, _sceneSaid;
    double _chapterSeconds;
    readonly HashSet<int> _yearsSaid = new();
    int _speed = 1;
    bool _paused, _openedWhilePaused, _resuming, _intro, _done;
    double _hold;   // seconds left to wait at the very end
    Rect2 _view;    // the lon/lat rectangle shown (x = west, y = south, w, h)

    public override void _Ready()
    {
        _script = new OpeningScript(FileAccess.GetFileAsString(OpeningScript.Path));
        _lines = Voice.Instance!.Catalog;
        ThemeAncient.FullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(ThemeAncient.FullRect(new ColorRect { Color = new Color(0.05f, 0.06f, 0.07f) }));

        _world = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_world);
        var terrain = GD.Load<Texture2D>(TerrainPath);
        var size = new Vector2(WorldWidth, WorldWidth * terrain.GetHeight() / terrain.GetWidth());
        _world.Size = size;
        _world.AddChild(new TextureRect
        {
            Texture = terrain, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Size = size,
            Modulate = new Color(0.72f, 0.7f, 0.68f), MouseFilter = MouseFilterEnum.Ignore,
        });
        foreach (int year in _script.Frames)
            _frames.Add(GD.Load<Texture2D>($"res://data/opening/pop_{(year < 0 ? $"{-year}BC" : $"{year}AD")}.png"));
        _people = new ShaderMaterial { Shader = new Shader { Code = FrameShader } };
        _world.AddChild(new TextureRect
        {
            Texture = _frames[0], ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Size = size,
            Material = _people, MouseFilter = MouseFilterEnum.Ignore,
        });
        foreach (var site in _script.Sites)
            AddSite(site, size);

        BuildCaptions();
        BuildControls();
        BuildAside();
        LoadPhotoCredits();
        _narrator = new AudioStreamPlayer { Name = "Narrator" };
        _voiceAside = new AudioStreamPlayer { Name = "Aside" };
        AddChild(_narrator);
        AddChild(_voiceAside);
        _narrator.Finished += OnNarratorFinished;
        _voiceAside.Finished += OnAsideFinished;

        _view = FocusOf(_script.Chapters[0]);
        // The first time, the bard explains the controls before the years begin.
        _intro = !SettingsStore.Instance.OpeningSeen && Say(_narrator, "opening_help_pause");
        if (!_intro)
            NextChapter();
        ShowYear(_script.Start);
    }

    // ---------- building the screen ----------

    void AddSite(OpeningSite site, Vector2 size)
    {
        var at = new Vector2(
            (float)((site.Lon - MapLonMin) / (MapLonMax - MapLonMin)) * size.X,
            (float)((MapLatMax - site.Lat) / (MapLatMax - MapLatMin)) * size.Y);
        var marker = new Button
        {
            Text = "◆", Flat = true, FocusMode = FocusModeEnum.None, Visible = false,
            TooltipText = $"{site.Name}: open its story",
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        marker.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        marker.AddThemeColorOverride("font_hover_color", Colors.White);
        marker.AddThemeFontSizeOverride("font_size", 22);
        marker.Pressed += () => OpenSite(site);
        _world.AddChild(marker);
        marker.Position = at - new Vector2(14, 18);
        marker.Size = new Vector2(28, 36);
        var name = ThemeAncient.Label(site.Name, fontSize: 18);
        name.MouseFilter = MouseFilterEnum.Ignore;
        name.Visible = false;
        name.Position = at + new Vector2(12, -14);
        Shadowed(name);
        _world.AddChild(name);
        _sites.Add((site, marker, name));
    }

    static double MapLonMin => World.MapView.LonMin;
    static double MapLonMax => World.MapView.LonMax;
    static double MapLatMin => World.MapView.LatMin;
    static double MapLatMax => World.MapView.LatMax;

    void BuildCaptions()
    {
        var top = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.SetAnchorsPreset(LayoutPreset.CenterTop);
        top.GrowHorizontal = GrowDirection.Both;
        top.OffsetTop = 28;
        top.AddThemeConstantOverride("separation", 6);
        AddChild(top);
        _year = ThemeAncient.Label("", "TitleLabel", align: HorizontalAlignment.Center);
        _year.AddThemeFontSizeOverride("font_size", 64);
        Shadowed(_year);
        top.AddChild(_year);
        var stageRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        stageRow.AddThemeConstantOverride("separation", 10);
        top.AddChild(stageRow);
        _stage = ThemeAncient.Label("", fontSize: 21, align: HorizontalAlignment.Center);
        Shadowed(_stage);
        stageRow.AddChild(_stage);
        var hear = new Button { Text = "▶ Tell me", FocusMode = FocusModeEnum.None, TooltipText = "Pause and hear this step of the story." };
        hear.Pressed += () => OpenStage(_script.StageAt(CurrentYear));
        stageRow.AddChild(hear);

        _subtitle = ThemeAncient.Label("", fontSize: 22, align: HorizontalAlignment.Center);
        _subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _subtitle.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _subtitle.GrowHorizontal = GrowDirection.Both;
        _subtitle.GrowVertical = GrowDirection.Begin;
        _subtitle.CustomMinimumSize = new Vector2(1300, 0);
        _subtitle.OffsetLeft = -650;
        _subtitle.OffsetRight = 650;
        _subtitle.OffsetBottom = -96;
        _subtitle.MouseFilter = MouseFilterEnum.Ignore;
        Shadowed(_subtitle);
        AddChild(_subtitle);
    }

    /// <summary>Words over the map get a dark outline, to read on desert and sea alike.</summary>
    static void Shadowed(Label label)
    {
        label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.03f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 6);
    }

    void BuildControls()
    {
        var bar = new PanelContainer { ThemeTypeVariation = "GlassPanel" };
        bar.SetAnchorsPreset(LayoutPreset.CenterBottom);
        bar.GrowHorizontal = GrowDirection.Both;
        bar.GrowVertical = GrowDirection.Begin;
        bar.OffsetBottom = -20;
        AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);
        _pause = ControlButton("❚❚ Pause", "Stop the years (Space). Then open any lit place to hear its story.", TogglePause);
        row.AddChild(_pause);
        row.AddChild(ControlButton("« Slower", "Let the years run more slowly.", () => SetSpeed(_speed - 1)));
        _speedLabel = ThemeAncient.Label("", fontSize: 18);
        _speedLabel.CustomMinimumSize = new Vector2(70, 0);
        _speedLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _speedLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_speedLabel);
        row.AddChild(ControlButton("Faster »", "Hurry the years on. The bard is cut short when the next scene begins.", () => SetSpeed(_speed + 1)));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(30, 0) });
        row.AddChild(ControlButton("Skip ▸▸", "Go straight to choosing your realm (Esc).", Finish));
        SetSpeed(1);
    }

    static Button ControlButton(string text, string tip, Action pressed)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    void BuildAside()
    {
        _aside = new PanelContainer { Visible = false };
        _aside.SetAnchorsPreset(LayoutPreset.CenterRight);
        _aside.GrowHorizontal = GrowDirection.Begin;
        _aside.GrowVertical = GrowDirection.Both;
        _aside.OffsetRight = -40;
        AddChild(_aside);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
        box.AddThemeConstantOverride("separation", 10);
        _aside.AddChild(box);
        _asideTitle = ThemeAncient.Label("", "HeaderLabel", 26);
        box.AddChild(_asideTitle);
        box.AddChild(ThemeAncient.Ornament(260));
        // The place as it is today (decision "Pictures for the opening's places": free photographs).
        _photo = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = new Vector2(460, 300), Visible = false,
        };
        box.AddChild(_photo);
        _photoCredit = ThemeAncient.Label("", "SubtleLabel", 13);
        _photoCredit.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _photoCredit.CustomMinimumSize = new Vector2(460, 0);
        box.AddChild(_photoCredit);
        _asideText = ThemeAncient.Label("", fontSize: 18);
        _asideText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _asideText.CustomMinimumSize = new Vector2(460, 0);
        box.AddChild(_asideText);
        var go = new Button { Text = "Continue ▶", FocusMode = FocusModeEnum.None };
        go.Pressed += Resume;
        box.AddChild(go);
    }

    // ---------- the story ----------

    double CurrentYear => _chapter < 0 ? _script.Start : OpeningScript.YearIn(_script.Chapters[_chapter], _p);

    void NextChapter()
    {
        _chapter++;
        if (_chapter >= _script.Chapters.Count)
        {
            _chapter = _script.Chapters.Count - 1;
            _p = 1;
            _hold = 2.5;
            return;
        }
        var c = _script.Chapters[_chapter];
        _p = 0;
        _yearSaid = _sceneSaid = false;
        // The chapter lasts as long as its year and scene take to say, and a breath.
        _chapterSeconds = LengthOf(OpeningScript.YearLine(c.From)) + LengthOf(c.Scene) + Pad;
        _narrator.Stop();
    }

    public override void _Process(double delta)
    {
        if (_done)
            return;
        float k = 1f - Mathf.Exp(-1.2f * (float)delta);
        if (_chapter >= 0)
        {
            var goal = FocusOf(_script.Chapters[_chapter]);
            _view = new Rect2(_view.Position.Lerp(goal.Position, k), _view.Size.Lerp(goal.Size, k));
        }
        Frame();
        if (_paused || _resuming || _intro)
            return;
        if (_silent > 0 && (_silent -= delta) <= 0)
            _subtitle.Text = "";
        if (_hold > 0)
        {
            _hold -= delta;
            if (_hold <= 0 && !NarratorBusy)
                Finish();
            return;
        }
        var c = _script.Chapters[_chapter];
        _p += delta * Speeds[_speed] / _chapterSeconds;
        if (!_yearSaid)
        {
            _yearSaid = true;
            _yearsSaid.Add(c.From);
            Say(_narrator, OpeningScript.YearLine(c.From));
        }
        else if (!_sceneSaid && !NarratorBusy)
        {
            _sceneSaid = true;
            Say(_narrator, c.Scene);
        }
        else if (_sceneSaid && !NarratorBusy)
        {
            // A slower clock leaves time to read out the years it passes.
            int passed = YearMarks.LastOrDefault(y => y <= CurrentYear && y > c.From && y < _script.End);
            if (passed != 0 && _yearsSaid.Add(passed))
                Say(_narrator, OpeningScript.YearLine(passed));
        }
        if (_p >= 1)
            NextChapter();
        ShowYear(CurrentYear);
    }

    IEnumerable<int> YearMarks => _lines.All.Where(l => l.Plays == "opening_year").Select(l => int.Parse(l.Key)).OrderBy(y => y);

    void Frame()
    {
        double year = CurrentYear;
        var (a, b, mix) = _script.FramesAt(year);
        _people.SetShaderParameter("frame_a", _frames[a]);
        _people.SetShaderParameter("frame_b", _frames[b]);
        _people.SetShaderParameter("mix_f", mix);
        foreach (var (site, marker, name) in _sites)
        {
            bool lit = site.Year <= year;
            marker.Visible = lit;
            double age = year - site.Year;
            name.Visible = lit && (age < 900 || marker.IsHovered());
            name.Modulate = new Color(1, 1, 1, (float)Math.Clamp(1.2 - age / 900.0, 0.35, 1));
            marker.Modulate = new Color(1, 1, 1, (float)Math.Clamp(0.35 + age / 250.0, 0.35, 1));
        }
        // Fit the view's lon/lat rectangle to the screen.
        var screen = GetViewportRect().Size;
        var rect = WorldRect(_view);
        // The view fills the screen and never shows past the map's edges.
        var world = _world.Size;
        float scale = Mathf.Max(Mathf.Max(screen.X / rect.Size.X, screen.Y / rect.Size.Y) * 0.92f,
            Mathf.Max(screen.X / world.X, screen.Y / world.Y));
        _world.Scale = Vector2.One * scale;
        var at = screen / 2 - (rect.Position + rect.Size / 2) * scale;
        _world.Position = new Vector2(Mathf.Clamp(at.X, screen.X - world.X * scale, 0), Mathf.Clamp(at.Y, screen.Y - world.Y * scale, 0));
        // Markers and names keep their size on screen.
        foreach (var (_, marker, name) in _sites)
        {
            marker.Scale = name.Scale = Vector2.One / scale;
            marker.PivotOffset = marker.Size / 2;
        }
    }

    void ShowYear(double year)
    {
        _year.Text = OpeningScript.YearText(Math.Round(year / 10) * 10);
        _stage.Text = $"{Capital(_lines[OpeningScript.StageLine(_script.StageAt(year).Key)]?.Subtitle.Split('.')[0] ?? "")}.";
    }

    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static Rect2 FocusOf(OpeningChapter c) =>
        new((float)c.Focus[0], (float)c.Focus[1], (float)(c.Focus[2] - c.Focus[0]), (float)(c.Focus[3] - c.Focus[1]));

    Rect2 WorldRect(Rect2 lonLat)
    {
        var size = _world.Size;
        float x0 = (float)((lonLat.Position.X - MapLonMin) / (MapLonMax - MapLonMin)) * size.X;
        float x1 = (float)((lonLat.End.X - MapLonMin) / (MapLonMax - MapLonMin)) * size.X;
        float y0 = (float)((MapLatMax - lonLat.End.Y) / (MapLatMax - MapLatMin)) * size.Y;
        float y1 = (float)((MapLatMax - lonLat.Position.Y) / (MapLatMax - MapLatMin)) * size.Y;
        return new Rect2(x0, y0, x1 - x0, y1 - y0);
    }

    // ---------- sound ----------

    /// <summary>
    /// Plays a line on a player, the narrator's with its subtitle. With the
    /// narrators off (Settings, Audio) the subtitle stays for as long as the
    /// line would take to say. False if nothing is heard.
    /// </summary>
    bool Say(AudioStreamPlayer player, string id)
    {
        var line = _lines[id];
        if (line == null)
            return false;
        var stream = Voice.Instance!.StreamOf(id);
        if (player == _narrator)
        {
            _subtitle.Text = line.Subtitle;
            _silent = 0;
        }
        if (stream == null || !SettingsStore.Instance.VoiceEnabled)
        {
            if (player == _narrator)
                _silent = stream?.GetLength() ?? line.Subtitle.Length / 14.0;
            return false;
        }
        player.Stream = stream;
        player.VolumeDb = Voice.VolumeDb;
        player.Play();
        return true;
    }

    double _silent;   // seconds a silent (narrators off) line still shows

    bool NarratorBusy => _narrator.Playing || _silent > 0;

    void OnNarratorFinished()
    {
        _subtitle.Text = "";
        if (!_intro)
            return;
        // "Pause whenever you wish...", then "Slow the years...", then the story.
        if (_introStep++ == 0 && Say(_narrator, "opening_help_speed"))
            return;
        EndIntro();
    }

    int _introStep;

    double LengthOf(string id) => Voice.Instance!.StreamOf(id)?.GetLength() ?? 0;

    // ---------- controls ----------

    void TogglePause()
    {
        if (_paused)
            Resume();
        else
            Pause();
    }

    void Pause()
    {
        if (_paused || _done)
            return;
        _paused = true;
        _narrator.StreamPaused = true;
        _pause.Text = "▶ Play";
    }

    void Resume()
    {
        if (!_paused)
            return;
        _aside.Visible = false;
        _voiceAside.Stop();
        _paused = false;
        _pause.Text = "❚❚ Pause";
        if (_openedWhilePaused)
        {
            // "And so the years run on." Then the bard picks up where he stopped.
            _openedWhilePaused = false;
            _resuming = Say(_voiceAside, "opening_help_resume");
            if (_resuming)
                return;
        }
        _openedWhilePaused = false;
        _narrator.StreamPaused = false;
    }

    void OnAsideFinished()
    {
        if (!_resuming)
            return;
        _resuming = false;
        _narrator.StreamPaused = false;
    }

    void SetSpeed(int i)
    {
        _speed = Math.Clamp(i, 0, Speeds.Length - 1);
        _speedLabel.Text = Speeds[_speed] switch { 0.5f => "½ ×", 1f => "1 ×", var s => $"{s:0} ×" };
    }

    void OpenSite(OpeningSite site)
    {
        OpenAside(site.Name, $"From about {OpeningScript.YearText(site.Year)}", OpeningScript.SiteLine(site.Key));
        string path = $"{PhotoDir}{site.Key}.jpg";
        if (ResourceLoader.Exists(path) && _photos.TryGetValue(site.Key, out var credit))
        {
            _photo.Texture = GD.Load<Texture2D>(path);
            _photo.Visible = true;
            _photoCredit.Text = $"{site.Name} today. Photo: {credit.Author}, {credit.Licence}, via Wikimedia Commons (full credits: assets/opening/sites/CREDITS.md).";
        }
    }

    const string PhotoDir = "res://assets/opening/sites/";
    const string PhotosPath = "res://data/opening_photos.json";

    /// <summary>Who took each place's photograph and under what licence (tools/fetch_opening_photos.py).</summary>
    void LoadPhotoCredits()
    {
        if (!FileAccess.FileExists(PhotosPath))
            return;
        using var doc = System.Text.Json.JsonDocument.Parse(FileAccess.GetFileAsString(PhotosPath));
        foreach (var p in doc.RootElement.EnumerateObject())
            _photos[p.Name] = (p.Value.GetProperty("author").GetString()!, p.Value.GetProperty("licence").GetString()!,
                p.Value.GetProperty("file").GetString()!);
    }

    void OpenStage(OpeningStage stage) => OpenAside("The story so far", OpeningScript.YearText(stage.Year), OpeningScript.StageLine(stage.Key));

    void OpenAside(string title, string when, string lineId)
    {
        if (_intro)
            EndIntro();
        Pause();
        _resuming = false;
        _openedWhilePaused = true;
        _asideTitle.Text = title;
        _photo.Visible = false;
        _photoCredit.Text = "";
        _asideText.Text = $"{when}\n\n{_lines[lineId]?.Subtitle}";
        _aside.Visible = true;
        Say(_voiceAside, lineId);
    }

    void EndIntro()
    {
        _intro = false;
        _narrator.Stop();
        _subtitle.Text = "";
        SettingsStore.Instance.MarkOpeningSeen();
        if (_chapter < 0)
            NextChapter();
    }

    void Finish()
    {
        if (_done)
            return;
        _done = true;
        _narrator.Stop();
        _voiceAside.Stop();
        SettingsStore.Instance.MarkOpeningSeen();
        EmitSignal(SignalName.Finished);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.Keycode == Key.Escape)
                Finish();
            else if (key.Keycode == Key.Space)
                TogglePause();
            else
                return;
            GetViewport().SetInputAsHandled();
        }
    }

}
