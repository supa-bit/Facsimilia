using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>What LoadGame() restores.</summary>
public sealed record LoadedGame(OwnershipGrid Grid, CharacterRegistry Registry, int DemoYear,
    int PlayerRealmId, GDictionary Population);

/// <summary>
/// Saves and loads a game in three files under user://:
///   save_state.json      year, player realm, grid size, and the character registry
///   save_grid.png        the ownership grid, one byte per cell (owner ids fit a byte)
///   save_population.bin  population engine state (PopulationEngine.ToDict()), zstd-compressed,
///                        absent when the game ran without HYDE data
/// The formats are unchanged from the GDScript version, so older saves load.
/// </summary>
public static class SaveSystem
{
    public const string StatePath = "user://save_state.json";
    public const string GridPath = "user://save_grid.png";
    public const string PopulationPath = "user://save_population.bin";

    /// <summary>
    /// Set by the main menu's "Continue" button just before changing to
    /// Main.tscn; read and reset by GameRoot to load instead of starting new.
    /// </summary>
    public static bool ContinueRequested { get; set; }

    public static bool HasSave() => FileAccess.FileExists(StatePath) && FileAccess.FileExists(GridPath);

    /// <summary>
    /// Writes the save. With a host node inside the tree, waits one frame
    /// first so a "Saving..." message gets drawn before the work starts.
    /// </summary>
    public static async Task<bool> SaveGame(OwnershipGrid grid, CharacterRegistry registry, int demoYear,
        int playerRealmId, Node? host = null, GDictionary? population = null)
    {
        if (host != null && host.IsInsideTree())
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        var bytes = new byte[grid.Cells.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            int owner = grid.Cells[i];
            if (owner is < 0 or > 255)
            {
                GD.PushError($"SaveSystem: owner id {owner} at cell {i} doesn't fit the single-byte save format");
                return false;
            }
            bytes[i] = (byte)owner;
        }
        var image = Image.CreateFromData(grid.Width, grid.Height, false, Image.Format.L8, bytes);
        if (image.SavePng(GridPath) != Error.Ok)
        {
            GD.PushError("SaveSystem: failed to write " + GridPath);
            return false;
        }

        var state = new GDictionary
        {
            ["grid_width"] = grid.Width,
            ["grid_height"] = grid.Height,
            ["demo_year"] = demoYear,
            ["player_realm_id"] = playerRealmId,
            ["registry"] = registry.ToDict(),
        };
        using (var file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                GD.PushError("SaveSystem: failed to open " + StatePath + " for writing");
                return false;
            }
            file.StoreString(Json.Stringify(state));
        }

        if (population == null || population.Count == 0)
        {
            if (FileAccess.FileExists(PopulationPath))
                DirAccess.RemoveAbsolute(PopulationPath);  // don't pair a stale population with this save
            return true;
        }
        using var popFile = FileAccess.OpenCompressed(PopulationPath, FileAccess.ModeFlags.Write,
            FileAccess.CompressionMode.Zstd);
        if (popFile == null)
        {
            GD.PushError("SaveSystem: failed to open " + PopulationPath + " for writing");
            return false;
        }
        popFile.StoreVar(population);
        return true;
    }

    /// <summary>The saved game, or null if there's none or it couldn't be read.</summary>
    public static LoadedGame? LoadGame()
    {
        if (!HasSave())
            return null;
        var parsed = Json.ParseString(FileAccess.GetFileAsString(StatePath));
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PushError("SaveSystem: " + StatePath + " is not valid JSON");
            return null;
        }
        var state = parsed.AsGodotDictionary();

        var image = new Image();
        if (image.Load(GridPath) != Error.Ok)
        {
            GD.PushError("SaveSystem: failed to read " + GridPath);
            return null;
        }
        image.Convert(Image.Format.L8);
        int width = state.TryGetValue("grid_width", out var w) ? w.AsInt32() : image.GetWidth();
        int height = state.TryGetValue("grid_height", out var h) ? h.AsInt32() : image.GetHeight();
        if (image.GetWidth() != width || image.GetHeight() != height)
        {
            GD.PushError("SaveSystem: saved grid image size doesn't match saved state");
            return null;
        }
        var grid = new OwnershipGrid(width, height);
        byte[] data = image.GetData();
        for (int i = 0; i < data.Length; i++)
            grid.Cells[i] = data[i];

        var registry = new CharacterRegistry();
        registry.LoadFromDict(state.TryGetValue("registry", out var reg) ? reg.AsGodotDictionary() : new GDictionary());

        var population = new GDictionary();
        if (FileAccess.FileExists(PopulationPath))
        {
            using var popFile = FileAccess.OpenCompressed(PopulationPath, FileAccess.ModeFlags.Read,
                FileAccess.CompressionMode.Zstd);
            var value = popFile?.GetVar() ?? default;
            if (value.VariantType == Variant.Type.Dictionary)
                population = value.AsGodotDictionary();
        }

        return new LoadedGame(grid, registry,
            state.TryGetValue("demo_year", out var y) ? y.AsInt32() : 0,
            state.TryGetValue("player_realm_id", out var p) ? p.AsInt32() : 0,
            population);
    }
}
