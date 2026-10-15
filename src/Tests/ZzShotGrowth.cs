using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a Roman province's panel after a great battle in Italy, showing why its region grows as it does.</summary>
public partial class ZzShotGrowth : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var pop = map.Population!;
        int italia = pop.Regions.First(x => x.Name == "Italia").Id;
        map.RavageAtNode(pop.LandNodes.First(i => pop.RegionOf(i) == italia && pop.Pop[i] > 0), Security.Battle(40_000));
        map.AdvanceYear();
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
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/growth.png");
        Finish("shot taken");
    }
}
