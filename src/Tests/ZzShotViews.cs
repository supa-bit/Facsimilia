using System.Linq;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the roads, going, supply and trade goods map views.</summary>
public partial class ZzShotViews : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        map.Game.MapFog = Game.FogLevel.Off;
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        map.CenterOn(map.NodeCell(map.CapitalNode(map.PlayerRealmId)) * MapView.CellPixels + new Vector2(300, 150), 1.6f);
        hud.Refresh();
        foreach (var view in new[] { MapView.RoadsView, MapView.TerrainView, MapView.SupplyView, MapView.GoodsView })
        {
            map.SetMapView(view);
            for (int i = 0; i < 30; i++)
                await ToSignal(this, SceneTree.SignalName.ProcessFrame);
            Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + $"/view_{view}.png");
        }
        Finish("shot taken");
    }
}
