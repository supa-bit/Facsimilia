using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Autoload (see project.godot [autoload]). Owns the game's settings -
/// fullscreen and master volume - persists them to user://settings.cfg, and
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

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Load();
        Apply();
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

    void Apply()
    {
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
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
    }

    void Save()
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("audio", "master_volume", MasterVolume);
        config.Save(SavePath);
    }
}
