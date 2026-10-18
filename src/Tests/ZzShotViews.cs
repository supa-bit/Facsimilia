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
        if (System.Enum.TryParse<ColourSight>(OS.GetEnvironment("SIGHT"), out var sight))
            map.Sight = sight;   // a colour-blind palette
        var views = OS.GetEnvironment("VIEWS") is { Length: > 0 } list ? list.Split(',') : new[] { MapView.RoadsView, MapView.TerrainView, MapView.SupplyView, MapView.GoodsView };
        foreach (var view in views)
        {
            map.SetMapView(view);
            for (int i = 0; i < 30; i++)
                await ToSignal(this, SceneTree.SignalName.ProcessFrame);
            Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + $"/view_{view}{(sight != ColourSight.Usual ? "_" + sight : "")}.png");
        }
        Finish("shot taken");
    }
}
