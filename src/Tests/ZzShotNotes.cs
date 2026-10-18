using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): notes on the map and in the province panel.</summary>
public partial class ZzShotNotes : TestRunner
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
        var latium = map.Provinces!.Provinces.Values.First(p => p.Name == "Latium");
        map.Game.Notes.Add((latium.Id, map.DemoYear, "Raise two legions here before the Samnites move"));
        var campania = map.Provinces.Provinces.Values.First(p => p.Name == "Campania");
        map.Game.Notes.Add((campania.Id, map.DemoYear, "Capua wavers: keep a garrison"));
        map.CenterOn(latium.LabelCell * MapView.CellPixels, 4f);
        map.RefreshArmyMarkers();
        map.SelectProvince(latium.Id);
        hud.Refresh();
        ((TabContainer)hud.Get("_provinceTabs")).CurrentTab = 4;   // History
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/notes.png");
        Finish("shot taken");
    }
}
