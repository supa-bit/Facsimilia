using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Autoload (see project.godot [autoload]). Owns the game's settings -
/// fullscreen, master volume and the autosave interval - persists them to user://settings.cfg, and
/// applies them to the engine. Everything that reads or changes a setting (the
/// settings panel, GameRoot's F11 shortcut) goes through Instance, so every
/// entry point stays in sync with what was last saved.
/// </summary>
public partial class SettingsStore : Node
{
    const string SavePath = "user://settings.cfg";

    public static SettingsStore Instance { get; private set; } = null!;

    public bool Fullscreen { get; private set; }
    /// <summary>Size of panels and text (Interface: "Interface size"): 1 = as designed. One of UiScales.</summary>
    public float UiScale { get; private set; } = 1f;
    public static readonly float[] UiScales = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f };
    public float MasterVolume { get; private set; } = 1f;  // linear 0..1, matches HSlider's natural range
    /// <summary>Autosave every this many years; 0 = off. One of SaveSystem.AutosaveIntervals.</summary>
    public int AutosaveInterval { get; private set; } = SaveSystem.DefaultAutosaveInterval;

    /// <summary>Your armies' battles open on the battle map (decision "Next 1"); off, your generals fight them.</summary>
    public bool FightBattles { get; private set; } = true;
    /// <summary>Keep years over a century old in brief (your answer "Keeping history in saves": a setting). Off: keep everything.</summary>
    public bool ThinOldYears { get; private set; }
    /// <summary>Whether closeness to history counts in your score (your note on "Score parts": it can be switched off).</summary>
    public bool ScoreHistory { get; private set; } = true;

    /// <summary>The narrators speak (decision "Voices"); off, only the text shows.</summary>
    public bool VoiceEnabled { get; private set; } = true;
    public float VoiceVolume { get; private set; } = 1f;  // linear 0..1, on top of the master volume

    public void SetVoiceEnabled(bool value)
    {
        VoiceEnabled = value;
        Save();
        if (!value)
            Voice.Instance?.Stop();
    }

    public void SetVoiceVolume(float value)
    {
        VoiceVolume = Mathf.Clamp(value, 0f, 1f);
        Save();
    }

    /// <summary>A new game begins with the opening, from Göbekli Tepe to 300 BC (it can always be skipped).</summary>
    public bool ShowOpening { get; private set; } = true;

    /// <summary>Whether the opening has been watched before; its first showing explains the controls aloud.</summary>
    public bool OpeningSeen { get; private set; }

    public void SetShowOpening(bool value)
    {
        ShowOpening = value;
        Save();
    }

    public void MarkOpeningSeen()
    {
        OpeningSeen = true;
        Save();
    }

    public void SetFightBattles(bool value)
    {
        FightBattles = value;
        Save();
    }

    public void SetThinOldYears(bool value)
    {
        ThinOldYears = value;
        Save();
    }

    public void SetScoreHistory(bool value)
    {
        ScoreHistory = value;
        Save();
    }

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Load();
        Apply();
        AddChild(new MouseDebugOverlay());  // F3
    }

    public void SetFullscreen(bool value)
    {
        Fullscreen = value;
        Apply();
        Save();
    }

    public void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp(value, 0f, 1f);
        Apply();
        Save();
    }

    public void SetUiScale(float scale)
    {
        UiScale = UiScales.Contains(scale) ? scale : 1f;
        Apply();
        Save();
    }

    public void SetAutosaveInterval(int years)
    {
        AutosaveInterval = ValidInterval(years);
        Save();
    }

    static int ValidInterval(int years) =>
        SaveSystem.AutosaveIntervals.Contains(years) ? years : SaveSystem.DefaultAutosaveInterval;

    /// <summary>
    /// Applies the settings. The window is only touched when its mode actually
    /// needs to change, and never while the game runs embedded in the Godot
    /// editor (the editor owns that window). Leaving fullscreen goes back to
    /// maximized - the project's start mode, which Windows always fits to the
    /// screen. Forcing plain "windowed" at the 1920x1080 base size used to make
    /// the window taller than a 1080p screen once the title bar was added;
    /// Windows then squeezed it and mouse clicks landed offset vertically.
    /// </summary>
    void Apply()
    {
        if (!Engine.IsEmbeddedInEditor())
        {
            var want = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Maximized;
            var current = DisplayServer.WindowGetMode();
            bool isFullscreen = current is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            if (Fullscreen != isFullscreen)
                DisplayServer.WindowSetMode(want);
        }
        if (IsInsideTree())
            GetTree().Root.ContentScaleFactor = UiScale;   // panels and text grow or shrink; the map can be zoomed as ever
        int bus = AudioServer.GetBusIndex("Master");
        if (bus != -1)
            AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
    }

    void Load()
    {
        var config = new ConfigFile();
        if (config.Load(SavePath) != Error.Ok)
            return;  // no settings file yet - defaults stand
        Fullscreen = config.GetValue("display", "fullscreen", Fullscreen).AsBool();
        MasterVolume = config.GetValue("audio", "master_volume", MasterVolume).AsSingle();
        UiScale = config.GetValue("display", "ui_scale", UiScale).AsSingle();
        if (!UiScales.Contains(UiScale))
            UiScale = 1f;
        AutosaveInterval = ValidInterval(config.GetValue("gameplay", "autosave_interval", AutosaveInterval).AsInt32());
        FightBattles = config.GetValue("gameplay", "fight_battles", FightBattles).AsBool();
        ThinOldYears = config.GetValue("gameplay", "thin_old_years", ThinOldYears).AsBool();
        ScoreHistory = config.GetValue("gameplay", "score_history", ScoreHistory).AsBool();
        VoiceEnabled = config.GetValue("audio", "voice_enabled", VoiceEnabled).AsBool();
        ShowOpening = config.GetValue("gameplay", "show_opening", ShowOpening).AsBool();
        OpeningSeen = config.GetValue("gameplay", "opening_seen", OpeningSeen).AsBool();
        VoiceVolume = Mathf.Clamp(config.GetValue("audio", "voice_volume", VoiceVolume).AsSingle(), 0f, 1f);
    }

    void Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("audio", "master_volume", MasterVolume);
        config.SetValue("display", "ui_scale", UiScale);
        config.SetValue("gameplay", "autosave_interval", AutosaveInterval);
        config.SetValue("gameplay", "fight_battles", FightBattles);
        config.SetValue("gameplay", "thin_old_years", ThinOldYears);
        config.SetValue("gameplay", "score_history", ScoreHistory);
        config.SetValue("audio", "voice_enabled", VoiceEnabled);
        config.SetValue("gameplay", "show_opening", ShowOpening);
        config.SetValue("gameplay", "opening_seen", OpeningSeen);
        config.SetValue("audio", "voice_volume", VoiceVolume);
        config.Save(SavePath);
    }
}
