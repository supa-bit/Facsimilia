using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the HUD with the ruler's panel open, after a few years of court life.</summary>
public partial class ZzShotPeople : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("seleucid");
        map.StartGame();
        for (int y = 0; y < 12; y++)
            map.AdvanceYear();
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        hud.Refresh();
        hud.Call("ShowPerson", map.Registry.Realms[map.PlayerRealmId].RulerId);
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/person.png");
        Finish("shot taken");
    }
}
