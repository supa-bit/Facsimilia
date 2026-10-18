using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the save panel with named saves, in save mode (MODE=load for load mode).</summary>
public partial class ZzShotSaves : TestRunner
{
    protected override async Task Run()
    {
        var grid = new OwnershipGrid(20, 15);
        var registry = new CharacterRegistry();
        var founder = registry.CreateCharacter("Numerius", "male", -345, culture: Culture.Latin);
        var realm = registry.CreateRealm("Rome", founder, SuccessionLaw.MalePreferencePrimogeniture, new Color(0.75f, 0.20f, 0.20f), Culture.Latin);
        foreach (var (name, year) in new[] { ("Before Cannae", -217), ("Rome 264 BC", -264), ("Pyrrhus lands", -280), ("The Ebro treaty", -226) })
            await SaveSystem.SaveGame(SaveSystem.SlotFor(name), grid, registry, year, realm.Id, name: name);
        await SaveSystem.SaveGame("autosave1", grid, registry, -210, realm.Id);
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var mode = OS.GetEnvironment("MODE") == "load" ? SaveSlotsPanel.Mode.Load : SaveSlotsPanel.Mode.Save;
        var panel = new SaveSlotsPanel(mode) { Theme = ThemeAncient.Build(), DefaultName = "Rome 208 BC" };
        layer.AddChild(panel);
        for (int i = 0; i < 12; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + $"/saves_{mode}.png");
        Finish("shot taken");
    }
}
