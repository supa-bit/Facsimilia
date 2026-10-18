using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): two realms side by side in the realms table.</summary>
public partial class ZzShotCompare : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        map.Game.DiplomacyFog = Game.FogLevel.Off;   // the whole world's news, for a full page
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        for (int y = 0; y < 11; y++)
            hud.LogEvents(map.AdvanceYear());
        hud.Refresh();
        typeof(Hud).GetMethod("ToggleRealmsPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(hud, new object?[] { true });
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/compare.png");
        Finish("shot taken");
    }
}
