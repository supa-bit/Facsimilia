using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the Find window with a search typed in.</summary>
public partial class ZzShotSearch : TestRunner
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
        for (int y = 0; y < 1; y++)
            hud.LogEvents(map.AdvanceYear());
        hud.Refresh();
        hud.ToggleSearch();
        ((LineEdit)hud.Get("_searchBox")).Text = "Ca";
        typeof(Hud).GetMethod("RefreshSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(hud, null);
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/search.png");
        Finish("shot taken");
    }
}
