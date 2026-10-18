using System.Reflection;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the favourites bar of map views.</summary>
public partial class ZzShotFavourites : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        var store = SettingsStore.Instance;
        foreach (var v in new[] { MapView.CultureView, MapView.LoyaltyView, MapView.PeopleView })
            if (!store.FavouriteViews.Contains(v))
                store.ToggleFavouriteView(v);
        map.SetMapView(MapView.LoyaltyView);
        hud.Refresh();
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/favourites.png");
        Finish("shot taken");
    }
}
