using System.Reflection;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the full map fog (land Rome never knew left blank) and the realms table under diplomacy fog.</summary>
public partial class ZzShotFog : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };   // in the scene, as the game makes it, so the map is drawn
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        map.MarchMonth();   // draws the fog
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        map.ZoomToFit();
        hud.Refresh();
        string dir = OS.GetEnvironment("SHOTDIR");
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/fog_map.png");
        typeof(Hud).GetMethod("ToggleRealmsPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hud, new object?[] { true });
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/fog_realms.png");

        var select = GD.Load<PackedScene>("res://scenes/CivSelect.tscn").Instantiate<CivSelect>();
        select.Theme = ThemeAncient.Build();
        var top = new CanvasLayer { Layer = 5 };
        Root.AddChild(top);
        top.AddChild(select);
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/fog_start.png");
        Finish("shot taken");
    }
}
