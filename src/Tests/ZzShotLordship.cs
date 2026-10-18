using System.Linq;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a mercenary captain's new lordship on the map.</summary>
public partial class ZzShotLordship : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        int rome = map.PlayerRealmId;
        var r = map.PlayerState;
        r.Treasury = 100000;
        var c = map.Game.Companies.First(x => x.Employer == 0);
        map.Hire(rome, c, 12);
        var army = r.ArmyById(c.ArmyId)!;
        var seat = map.Provinces!.Provinces.Values.First(p => p.RealmId == rome && p.Name == "Campania");
        army.Node = map.Population!.LandNodes.First(i => map.ProvinceOfNodeForTest(i) == seat.Id);
        army.Route.Clear();
        map.Game.Companies.Remove(c);
        var events = map.Mutiny(rome, r, army, lordship: true);
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        hud.LogEvents(events);
        await map.RedrawTerritory();
        map.CenterOn(map.NodeCell(army.Node) * MapView.CellPixels + new Vector2(80, 60), 5f);
        map.RefreshArmyMarkers();
        hud.Refresh();
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/lordship.png");
        Finish("shot taken");
    }
}
