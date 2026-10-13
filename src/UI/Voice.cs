using System;
using System.Collections.Generic;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Autoload (see project.godot [autoload]). Speaks the recorded lines in
/// assets/voice: the game asks for a cue ("moment:battle_won") and the
/// narrator of the year says one of its lines. Only files already in the
/// project are played; nothing here generates speech or goes online.
///
/// Which take of each line plays (A or B) is the designer's choice, made in
/// the main menu's Voices screen (VoicePicker) and kept in data/voice_picks.json.
/// </summary>
public partial class Voice : Node
{
    public const string PicksPath = "res://data/voice_picks.json";
    const string UserPicksPath = "user://voice_picks.json";

    /// <summary>Lines waiting while another is spoken; more than this are dropped, so speech never lags far behind play.</summary>
    const int MaxWaiting = 1;

    public static Voice? Instance { get; private set; }

    public VoiceCatalog Catalog { get; private set; } = null!;

    AudioStreamPlayer _player = null!;
    readonly Queue<string> _waiting = new();
    readonly Random _rng = new();

    /// <summary>The line now being spoken (id), or null.</summary>
    public string? Speaking { get; private set; }

    /// <summary>Raised when a line starts (its id) or speech stops (null), for the Voices screen.</summary>
    public event Action<string?>? SpeakingChanged;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Catalog = new VoiceCatalog(FileAccess.GetFileAsString(VoiceCatalog.LinesPath));
        if (FileAccess.FileExists(PicksPath))
            Catalog.LoadPicks(FileAccess.GetFileAsString(PicksPath));
        if (FileAccess.FileExists(UserPicksPath))
            Catalog.LoadPicks(FileAccess.GetFileAsString(UserPicksPath));
        _player = new AudioStreamPlayer { Name = "Speaker" };
        _player.Finished += Next;
        AddChild(_player);
    }

    /// <summary>
    /// Says a line for a cue in a game year, if the narrators are on and the
    /// cue has one. Returns the line chosen, or null.
    /// </summary>
    public VoiceLine? Cue(string? cue, int year)
    {
        if (cue == null || !SettingsStore.Instance.VoiceEnabled)
            return null;
        var line = Catalog.Choose(cue, year, _rng);
        if (line != null)
            Say(line.Id);
        return line;
    }

    /// <summary>Speaks a line now, or after the one being spoken.</summary>
    public void Say(string id)
    {
        if (!SettingsStore.Instance.VoiceEnabled)
            return;
        if (!_player.Playing)
        {
            Play(id, Catalog.TakeOf(id));
            return;
        }
        if (_waiting.Count < MaxWaiting && id != Speaking && !_waiting.Contains(id))
            _waiting.Enqueue(id);
    }

    /// <summary>Plays one take of a line straight away (the Voices screen), whatever the settings say.</summary>
    public bool PlayTake(string id, string take)
    {
        _waiting.Clear();
        return Play(id, take);
    }

    public void Stop()
    {
        _waiting.Clear();
        _player.Stop();
        SetSpeaking(null);
    }

    /// <summary>Whether a take was recorded (and is in the project).</summary>
    public static bool Exists(string id, string take) => FileAccess.FileExists(VoiceCatalog.FileOf(id, take));

    /// <summary>
    /// A take's sound. Take A is imported by Godot like any sound; take B
    /// (assets/voice/takes, kept out of the import by its .gdignore so the
    /// project doesn't hold every line twice) is read straight from its file.
    /// </summary>
    static AudioStream? Load(string id, string take)
    {
        string path = VoiceCatalog.FileOf(id, take);
        if (take == VoiceCatalog.Take && ResourceLoader.Exists(path))
            return GD.Load<AudioStream>(path);
        return FileAccess.FileExists(path) ? AudioStreamMP3.LoadFromFile(path) : null;
    }

    bool Play(string id, string take)
    {
        if (Load(id, take) is not { } stream)
        {
            GD.PushWarning($"Voice: no recording {VoiceCatalog.FileOf(id, take)}");
            return false;
        }
        _player.Stream = stream;
        _player.VolumeDb = Mathf.LinearToDb(Mathf.Max(SettingsStore.Instance.VoiceVolume, 0.0001f));
        _player.Play();
        SetSpeaking(id);
        return true;
    }

    void Next()
    {
        SetSpeaking(null);
        if (_waiting.Count > 0)
        {
            string id = _waiting.Dequeue();
            Play(id, Catalog.TakeOf(id));
        }
    }

    void SetSpeaking(string? id)
    {
        Speaking = id;
        SpeakingChanged?.Invoke(id);
    }

    /// <summary>
    /// Chooses which take a line plays and keeps the choice. Run from the
    /// Godot editor, it is written into the project (data/voice_picks.json,
    /// to commit with GitHub Desktop); in an exported game, into the player's
    /// own folder.
    /// </summary>
    public void Pick(string id, string take)
    {
        Catalog.Picks[id] = take;
        string json = Catalog.SavePicks();
        string path = OS.HasFeature("editor") ? PicksPath : UserPicksPath;
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushWarning($"Voice: could not save picks to {path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(json);
    }

    /// <summary>Where the choices are kept, for the Voices screen to say.</summary>
    public static string PicksFile => OS.HasFeature("editor") ? "data/voice_picks.json" : "your settings folder";
}
