using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a province where veterans farm, with the button to call them back.</summary>
public partial class ZzShotReserves : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var latium0 = map.Provinces!.Provinces.Values.First(p => p.Name == "Latium");
        map.PlayerState.VeteransWaiting = 4000;
        map.PlayerState.VeteransXp = 0.6;
        map.SettleVeterans(latium0.Id);
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        var latium = map.Provinces!.Provinces.Values.First(p => p.Name == "Latium");
        map.SelectProvince(latium.Id);
        hud.Refresh();
        ((TabContainer)hud.Get("_provinceTabs")).CurrentTab = 1;   // Land
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/reserves.png");
        Finish("shot taken");
    }
}
