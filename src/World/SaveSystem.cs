using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>What LoadGame() restores.</summary>
public sealed record LoadedGame(OwnershipGrid Grid, CharacterRegistry Registry, int DemoYear,
    int PlayerRealmId, GDictionary Population);

/// <summary>A save's summary for the slot lists, read without loading the whole game.</summary>
public sealed record SaveInfo(string Slot, string RealmName, Color RealmColor, int Year, string Ruler,
    double SavedUnix, string Build)
{
    /// <summary>"12 Mar 2026, 21:40" in the player's local time.</summary>
    public string SavedText()
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds((long)(SavedUnix * 1000)).ToLocalTime();
        return local.ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Saves and loads games in slots: three the player picks (slot1..slot3)
/// and one autosave, each a folder under user://saves/ holding
///   info.json       what the slot lists show: realm, year, ruler, when
///   state.json      year, player realm, grid size, and the character registry
///   grid.png        the ownership grid, one byte per cell (owner ids fit a byte)
///   population.bin  population engine state, zstd-compressed; absent when
///                   the game ran without HYDE data
/// A save is written to a side folder first and swapped in only once
/// complete, so a crash or full disk mid-save never damages the old save.
/// The single save of earlier versions (user://save_*) moves into slot 1.
/// </summary>
public static class SaveSystem
{
    public static readonly string[] ManualSlots = { "slot1", "slot2", "slot3" };
    public const string AutosaveSlot = "autosave";
    public const int AutosaveEveryYears = 10;

    /// <summary>
    /// The folder saves live under (the slots in Root/saves, the old single
    /// save directly in Root). Tests point it elsewhere so they never touch
    /// the player's real saves.
    /// </summary>
    public static string Root { get; set; } = "user://";

    static string InRoot(string name) => Root.EndsWith('/') ? Root + name : Root + "/" + name;
    static string SavesDir => InRoot("saves");
    static string LegacyState => InRoot("save_state.json");
    static string LegacyGrid => InRoot("save_grid.png");
    static string LegacyPopulation => InRoot("save_population.bin");

    const string InfoFile = "info.json", StateFile = "state.json", GridFile = "grid.png", PopulationFile = "population.bin";
    const string PartialSuffix = ".partial", OldSuffix = ".old";

    /// <summary>
    /// Set by the main menu (Continue / Load Game) just before changing to
    /// Main.tscn; read and reset by GameRoot to load that slot instead of
    /// starting a new game.
    /// </summary>
    public static string? PendingLoadSlot { get; set; }

    public static string SlotName(string slot) =>
        slot == AutosaveSlot ? "Autosave" : slot.StartsWith("slot") ? "Slot " + slot[4..] : slot;

    static string SlotDir(string slot) => $"{SavesDir}/{slot}";

    public static bool HasSave(string slot)
    {
        RecoverSlot(slot);
        string dir = SlotDir(slot);
        return FileAccess.FileExists($"{dir}/{StateFile}") && FileAccess.FileExists($"{dir}/{GridFile}");
    }

    /// <summary>Every existing save, newest first.</summary>
    public static List<SaveInfo> ListSaves() =>
        ManualSlots.Append(AutosaveSlot).Select(ReadInfo).OfType<SaveInfo>()
            .OrderByDescending(i => i.SavedUnix).ToList();

    /// <summary>The save "Continue" loads: the most recently written one, autosave included.</summary>
    public static SaveInfo? MostRecent() => ListSaves().FirstOrDefault();

    public static SaveInfo? ReadInfo(string slot)
    {
        if (!HasSave(slot))
            return null;
        var parsed = Json.ParseString(FileAccess.GetFileAsString($"{SlotDir(slot)}/{InfoFile}"));
        var d = parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : new GDictionary();
        string Str(string key, string fallback) => d.TryGetValue(key, out var v) ? v.AsString() : fallback;
        double modified = FileAccess.GetModifiedTime($"{SlotDir(slot)}/{StateFile}");
        return new SaveInfo(slot,
            Str("realm", "Unknown realm"),
            Color.FromHtml(Str("color", "#808080")),
            d.TryGetValue("year", out var y) ? y.AsInt32() : 0,
            Str("ruler", "—"),
            d.TryGetValue("saved_unix", out var t) ? t.AsDouble() : modified,
            Str("build", ""));
    }

    public static void DeleteSave(string slot)
    {
        DeleteDir(SlotDir(slot));
        DeleteDir(SlotDir(slot) + OldSuffix);
        DeleteDir(SlotDir(slot) + PartialSuffix);
    }

    /// <summary>
    /// Writes the save to a slot. With a host node inside the tree, waits one
    /// frame first so a "Saving..." message gets drawn before the work starts.
    /// </summary>
    public static async Task<bool> SaveGame(string slot, OwnershipGrid grid, CharacterRegistry registry, int demoYear,
        int playerRealmId, Node? host = null, GDictionary? population = null)
    {
        if (host != null && host.IsInsideTree())
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        string final = SlotDir(slot), partial = final + PartialSuffix, old = final + OldSuffix;
        DeleteDir(partial);
        if (DirAccess.MakeDirRecursiveAbsolute(partial) != Error.Ok)
        {
            GD.PushError("SaveSystem: couldn't create " + partial);
            return false;
        }
        if (!WriteFiles(partial, grid, registry, demoYear, playerRealmId, population))
        {
            DeleteDir(partial);
            return false;
        }

        // Swap the finished save in; the old one is removed only after.
        DeleteDir(old);
        if (DirAccess.DirExistsAbsolute(final) && DirAccess.RenameAbsolute(final, old) != Error.Ok)
        {
            GD.PushError("SaveSystem: couldn't move the previous save aside");
            DeleteDir(partial);
            return false;
        }
        if (DirAccess.RenameAbsolute(partial, final) != Error.Ok)
        {
            GD.PushError("SaveSystem: couldn't put the new save in place");
            RecoverSlot(slot);
            return false;
        }
        DeleteDir(old);
        return true;
    }

    static bool WriteFiles(string dir, OwnershipGrid grid, CharacterRegistry registry, int demoYear,
        int playerRealmId, GDictionary? population)
    {
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
        if (image.SavePng($"{dir}/{GridFile}") != Error.Ok)
        {
            GD.PushError("SaveSystem: failed to write the grid");
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
        if (!WriteText($"{dir}/{StateFile}", Json.Stringify(state)))
            return false;

        if (population != null && population.Count > 0)
        {
            using var popFile = FileAccess.OpenCompressed($"{dir}/{PopulationFile}", FileAccess.ModeFlags.Write,
                FileAccess.CompressionMode.Zstd);
            if (popFile == null)
            {
                GD.PushError("SaveSystem: failed to write the population");
                return false;
            }
            popFile.StoreVar(population);
        }

        return WriteText($"{dir}/{InfoFile}", Json.Stringify(Info(registry, demoYear, playerRealmId)));
    }

    static GDictionary Info(CharacterRegistry registry, int demoYear, int playerRealmId)
    {
        var info = new GDictionary
        {
            ["year"] = demoYear,
            ["saved_unix"] = Time.GetUnixTimeFromSystem(),  // fractional: two saves in one second still order
            ["build"] = UI.MainMenu.BuildCommit(),
        };
        if (registry.Realms.TryGetValue(playerRealmId, out var realm))
        {
            info["realm"] = realm.Name;
            info["color"] = realm.Color.ToHtml(false);
            if (registry.Characters.TryGetValue(realm.RulerId, out var ruler))
                info["ruler"] = ruler.Name;
        }
        return info;
    }

    static bool WriteText(string path, string text)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError("SaveSystem: failed to open " + path + " for writing");
            return false;
        }
        file.StoreString(text);
        return true;
    }

    /// <summary>The saved game in a slot, or null if there's none or it couldn't be read.</summary>
    public static LoadedGame? LoadGame(string slot)
    {
        if (!HasSave(slot))
            return null;
        string dir = SlotDir(slot);
        var parsed = Json.ParseString(FileAccess.GetFileAsString($"{dir}/{StateFile}"));
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PushError($"SaveSystem: {dir}/{StateFile} is not valid JSON");
            return null;
        }
        var state = parsed.AsGodotDictionary();

        var image = new Image();
        if (image.Load($"{dir}/{GridFile}") != Error.Ok)
        {
            GD.PushError($"SaveSystem: failed to read {dir}/{GridFile}");
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
        if (FileAccess.FileExists($"{dir}/{PopulationFile}"))
        {
            using var popFile = FileAccess.OpenCompressed($"{dir}/{PopulationFile}", FileAccess.ModeFlags.Read,
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

    /// <summary>
    /// Moves the one save older versions made (user://save_*) into slot 1,
    /// with a slot summary built from it. Does nothing once it's been moved,
    /// or if slot 1 is already used.
    /// </summary>
    public static void MigrateLegacySave()
    {
        if (!FileAccess.FileExists(LegacyState) || !FileAccess.FileExists(LegacyGrid) || HasSave(ManualSlots[0]))
            return;
        string dir = SlotDir(ManualSlots[0]);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        bool moved = DirAccess.RenameAbsolute(LegacyState, $"{dir}/{StateFile}") == Error.Ok
            && DirAccess.RenameAbsolute(LegacyGrid, $"{dir}/{GridFile}") == Error.Ok;
        if (moved && FileAccess.FileExists(LegacyPopulation))
            DirAccess.RenameAbsolute(LegacyPopulation, $"{dir}/{PopulationFile}");
        if (!moved)
        {
            GD.PushError("SaveSystem: couldn't move the old save into slot 1");
            return;
        }
        // The summary needs the realm and ruler, so this one time the save is read in full.
        var loaded = LoadGame(ManualSlots[0]);
        if (loaded != null)
        {
            var info = Info(loaded.Registry, loaded.DemoYear, loaded.PlayerRealmId);
            info["saved_unix"] = (double)FileAccess.GetModifiedTime($"{dir}/{StateFile}");
            info["build"] = "";
            WriteText($"{dir}/{InfoFile}", Json.Stringify(info));
        }
    }

    /// <summary>
    /// After a crash mid-swap only the ".old" copy may exist; put it back.
    /// A leftover ".partial" is an unfinished save and is ignored (and
    /// replaced by the next save).
    /// </summary>
    static void RecoverSlot(string slot)
    {
        string final = SlotDir(slot), old = final + OldSuffix;
        if (!DirAccess.DirExistsAbsolute(final) && DirAccess.DirExistsAbsolute(old))
            DirAccess.RenameAbsolute(old, final);
    }

    internal static void DeleteDir(string path)
    {
        using var dir = DirAccess.Open(path);
        if (dir == null)
            return;
        foreach (string file in dir.GetFiles())
            dir.Remove(file);
        foreach (string sub in dir.GetDirectories())
            DeleteDir($"{path}/{sub}");
        DirAccess.RemoveAbsolute(path);
    }
}
