using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.World;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Tests;

/// <summary>
/// Save slots: grid, registry and population round-trip; slot summaries;
/// overwriting; deleting; recovery after a crash mid-save; moving the old
/// single save into slot 1. Uses its own folder, never the player's saves.
/// </summary>
public partial class SaveSystemTest : TestRunner
{
    internal const string TestRoot = "user://test_saves";

    /// <summary>Points SaveSystem at an empty test folder.</summary>
    internal static void UseCleanTestFolder()
    {
        SaveSystem.Root = TestRoot;
        SaveSystem.DeleteDir(TestRoot);
        DirAccess.MakeDirRecursiveAbsolute(TestRoot);
    }

    protected override async Task Run()
    {
        UseCleanTestFolder();
        Check(SaveSystem.ListSaves().Count == 0 && SaveSystem.MostRecent() == null, "the test folder isn't empty");

        var grid = new OwnershipGrid(20, 15);
        grid.FillRect(0, 0, 10, 10, 1);
        grid.FillRect(10, 0, 20, 10, 2);
        grid.SetOwner(5, 12, 255);  // the sea sentinel, still within a byte

        var registry = new CharacterRegistry();
        var founder = registry.CreateCharacter("Numerius", "male", -345, culture: Culture.Latin);
        registry.CreateDynasty("House Numerius", founder);
        var spouse = registry.CreateCharacter("Cornelia", "female", -343, culture: Culture.Latin);
        CharacterRegistry.Marry(founder, spouse);
        var heir = registry.HaveChild(spouse, founder, "Marcus", "male", -322);
        var realm = registry.CreateRealm("Rome", founder, SuccessionLaw.MalePreferencePrimogeniture,
            new Color(0.75f, 0.20f, 0.20f), Culture.Latin);

        Check(await SaveSystem.SaveGame("slot2", grid, registry, -280, realm.Id), "save failed");
        Check(SaveSystem.HasSave("slot2") && !SaveSystem.HasSave("slot1"), "saved into the wrong slot");
        var loaded = SaveSystem.LoadGame("slot2");
        if (!Check(loaded != null, "load failed"))
        {
            Finish("");
            return;
        }
        var g = loaded!.Grid;
        Check(g.Width == 20 && g.Height == 15, "grid size");
        Check(g.GetOwner(5, 5) == 1 && g.GetOwner(15, 5) == 2 && g.GetOwner(5, 12) == 255 && g.GetOwner(0, 14) == 0, "grid cells");
        Check(loaded.DemoYear == -280 && loaded.PlayerRealmId == realm.Id, "year / player realm");

        var r = loaded.Registry;
        Check(r.Characters.Count == 3 && r.Dynasties.Count == 1 && r.Realms.Count == 1, "registry sizes");
        var f = r.Characters[founder.Id];
        Check(f.Name == "Numerius" && f.IsAlive && f.ChildrenIds.SequenceEqual(new[] { heir.Id }), "founder restored");
        var lr = r.Realms[realm.Id];
        Check(lr.Name == "Rome" && lr.RulerId == founder.Id && lr.SuccessionLaw == SuccessionLaw.MalePreferencePrimogeniture
            && Mathf.IsEqualApprox(lr.Color.R, 0.75f) && lr.Culture == Culture.Latin, "realm restored");
        // Id counters continue past what was loaded, so new ids never collide.
        var arrival = r.CreateCharacter("New Arrival", "male", -280);
        Check(arrival.Id > founder.Id && arrival.Id > heir.Id, "id counter restarted");
        Check(loaded.Population.Count == 0, "no population was saved");

        // The slot list's summary, read without loading the game.
        var info = SaveSystem.ReadInfo("slot2");
        Check(info != null && info.RealmName == "Rome" && info.Year == -280 && info.Ruler == "Numerius"
            && Mathf.IsEqualApprox(info.RealmColor.R, 0.75f, 0.01f), "slot summary");

        // Population round-trips; overwriting without one clears it.
        var pop = new float[] { 0f, 12.5f, 30000f };
        Check(await SaveSystem.SaveGame("slot2", grid, registry, -279, realm.Id, null, new GDictionary { ["year"] = -279, ["pop"] = pop }), "save with population");
        var withPop = SaveSystem.LoadGame("slot2")!;
        Check(withPop.Population["year"].AsInt32() == -279 && withPop.Population["pop"].AsFloat32Array().SequenceEqual(pop), "population round-trip");
        Check(await SaveSystem.SaveGame("slot2", grid, registry, -278, realm.Id), "overwrite without population");
        Check(SaveSystem.LoadGame("slot2")!.Population.Count == 0, "stale population kept");
        Check(SaveSystem.ReadInfo("slot2")!.Year == -278, "overwrite didn't replace the summary");

        // Slots are independent; Continue picks the most recently saved.
        Check(await SaveSystem.SaveGame(SaveSystem.NextAutosaveSlot(), grid, registry, -270, realm.Id), "autosave");
        Check(SaveSystem.LoadGame("slot2")!.DemoYear == -278, "the autosave touched slot 2");
        Check(SaveSystem.ListSaves().Count == 2, "expected two saves listed");
        Check(SaveSystem.MostRecent()?.Slot == "autosave1", "Continue should pick the newest save");

        // Autosaves take turns: three slots fill up, then the oldest is replaced.
        foreach (int year in new[] { -260, -250, -240 })
            Check(await SaveSystem.SaveGame(SaveSystem.NextAutosaveSlot(), grid, registry, year, realm.Id), "autosave");
        Check(SaveSystem.ReadInfo("autosave1")?.Year == -240 && SaveSystem.ReadInfo("autosave2")?.Year == -260
            && SaveSystem.ReadInfo("autosave3")?.Year == -250, "the fourth autosave should replace the oldest");
        Check(SaveSystem.MostRecent()?.Slot == "autosave1" && SaveSystem.SlotName("autosave3") == "Autosave 3", "autosave names");
        foreach (string slot in SaveSystem.AutosaveSlots)
            SaveSystem.DeleteSave(slot);
        Check(await SaveSystem.SaveGame(SaveSystem.NextAutosaveSlot(), grid, registry, -270, realm.Id), "autosave");

        // A save that fails part-way (an owner id that doesn't fit a byte) leaves the old save intact.
        var bad = new OwnershipGrid(20, 15);
        bad.SetOwner(1, 1, 300);
        Check(!await SaveSystem.SaveGame("slot2", bad, registry, -200, realm.Id), "a broken save reported success");
        Check(SaveSystem.LoadGame("slot2")?.DemoYear == -278, "a failed save damaged the old one");

        // A crash between moving the old save aside and putting the new one in place.
        DirAccess.RenameAbsolute($"{TestRoot}/saves/slot2", $"{TestRoot}/saves/slot2.old");
        Check(SaveSystem.HasSave("slot2") && SaveSystem.LoadGame("slot2")?.DemoYear == -278, "the old save wasn't recovered");

        SaveSystem.DeleteSave("slot2");
        Check(!SaveSystem.HasSave("slot2") && SaveSystem.ListSaves().Count == 1, "delete");

        // The one autosave folder of the first slot version becomes Autosave 1.
        SaveSystem.DeleteSave("autosave1");
        Check(await SaveSystem.SaveGame("autosave", grid, registry, -230, realm.Id), "old-style autosave");
        SaveSystem.MigrateLegacySave();
        Check(SaveSystem.ReadInfo("autosave1")?.Year == -230 && !DirAccess.DirExistsAbsolute($"{TestRoot}/saves/autosave"),
            "the old autosave wasn't moved to Autosave 1");
        SaveSystem.DeleteSave("autosave1");

        // The single save of older versions moves into slot 1, summary included.
        Check(await SaveSystem.SaveGame("slot3", grid, registry, -250, realm.Id), "save for the legacy test");
        DirAccess.RenameAbsolute($"{TestRoot}/saves/slot3/state.json", $"{TestRoot}/save_state.json");
        var png = Image.CreateFromData(grid.Width, grid.Height, false, Image.Format.L8,
            grid.Cells.Select(c => (byte)c).ToArray());
        png.SavePng($"{TestRoot}/save_grid.png");  // older versions stored the grid as a PNG
        SaveSystem.DeleteSave("slot3");
        SaveSystem.MigrateLegacySave();
        var migrated = SaveSystem.ReadInfo("slot1");
        Check(migrated != null && migrated.Year == -250 && migrated.RealmName == "Rome", "old save not moved into slot 1");
        Check(!FileAccess.FileExists($"{TestRoot}/save_state.json"), "old save left behind");
        var old = SaveSystem.LoadGame("slot1");
        Check(old?.Registry.Characters.Count == 3 && old.Grid.GetOwner(15, 5) == 2 && old.Grid.GetOwner(5, 12) == 255,
            "moved save (PNG grid) doesn't load");

        SaveSystem.DeleteDir(TestRoot);
        Finish("SaveSystem tests passed: round-trip per slot, slot summaries, overwrite, failed-save safety, " +
            "crash recovery, delete, newest-save Continue, and the old single save moving into slot 1.");
    }
}
