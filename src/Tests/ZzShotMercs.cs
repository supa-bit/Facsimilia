using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the mercenary companies, one hired.</summary>
public partial class ZzShotMercs : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var rs = map.PlayerState;
        rs.Treasury = 100000;
        var co = System.Linq.Enumerable.First(map.Game.Companies, c => c.Employer == 0 && c.SpecId == "taenarum_hoplites");
        map.Hire(map.PlayerRealmId, co, 24);
        co.Loyalty = 0.35;
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        hud.Refresh();
        typeof(Hud).GetMethod("ToggleArmiesPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(hud, new object?[] { true });
        typeof(Hud).GetField("_mercMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(hud, true);
        typeof(Hud).GetMethod("RefreshArmiesPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(hud, null);
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/mercs.png");
        Finish("shot taken");
    }
}
