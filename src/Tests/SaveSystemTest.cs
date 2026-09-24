using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.World;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Tests;

/// <summary>Grid, registry and population round-trip through SaveGame/LoadGame.</summary>
public partial class SaveSystemTest : TestRunner
{
    internal static void DeleteSave()
    {
        using var dir = DirAccess.Open("user://");
        if (dir == null)
            return;
        foreach (string f in new[] { "save_state.json", "save_grid.png", "save_population.bin" })
            dir.Remove(f);
    }

    protected override async Task Run()
    {
        // Clean slate: an earlier run or real play may have left a save behind.
        DeleteSave();
        Check(!SaveSystem.HasSave(), "a save exists after deleting it");

        var grid = new OwnershipGrid(20, 15);
        grid.FillRect(0, 0, 10, 10, 1);
        grid.FillRect(10, 0, 20, 10, 2);
        grid.SetOwner(5, 12, 255);  // the sea sentinel, still within a byte

        var registry = new CharacterRegistry();
        var founder = registry.CreateCharacter("Numerius", "male", -345);
        registry.CreateDynasty("House Numerius", founder);
        var spouse = registry.CreateCharacter("Cornelia", "female", -343);
        CharacterRegistry.Marry(founder, spouse);
        var heir = registry.HaveChild(spouse, founder, "Marcus", "male", -322);
        var realm = registry.CreateRealm("Rome", founder, SuccessionLaw.MalePreferencePrimogeniture, new Color(0.75f, 0.20f, 0.20f));

        Check(await SaveSystem.SaveGame(grid, registry, -280, realm.Id), "save failed");
        Check(SaveSystem.HasSave(), "no save after saving");
        var loaded = SaveSystem.LoadGame();
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
            && Mathf.IsEqualApprox(lr.Color.R, 0.75f), "realm restored");
        // Id counters continue past what was loaded, so new ids never collide.
        var arrival = r.CreateCharacter("New Arrival", "male", -280);
        Check(arrival.Id > founder.Id && arrival.Id > heir.Id, "id counter restarted");
        Check(loaded.Population.Count == 0, "no population was saved");

        // Population round-trips; saving again without one clears it.
        var pop = new float[] { 0f, 12.5f, 30000f };
        Check(await SaveSystem.SaveGame(grid, registry, -279, realm.Id, null, new GDictionary { ["year"] = -279, ["pop"] = pop }), "save with population");
        var withPop = SaveSystem.LoadGame()!;
        Check(withPop.Population["year"].AsInt32() == -279 && withPop.Population["pop"].AsFloat32Array().SequenceEqual(pop), "population round-trip");
        Check(await SaveSystem.SaveGame(grid, registry, -278, realm.Id), "save without population");
        Check(SaveSystem.LoadGame()!.Population.Count == 0, "stale population kept");

        Finish("SaveSystem tests passed: grid + registry + population round-trip through SaveGame/LoadGame, id counters continue correctly.");
    }
}
