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
    public float MasterVolume { get; private set; } = 1f;  // linear 0..1, matches HSlider's natural range
    /// <summary>Autosave every this many years; 0 = off. One of SaveSystem.AutosaveIntervals.</summary>
    public int AutosaveInterval { get; private set; } = SaveSystem.DefaultAutosaveInterval;

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
        AutosaveInterval = ValidInterval(config.GetValue("gameplay", "autosave_interval", AutosaveInterval).AsInt32());
    }

    void Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("audio", "master_volume", MasterVolume);
        config.SetValue("gameplay", "autosave_interval", AutosaveInterval);
        config.Save(SavePath);
    }
}
