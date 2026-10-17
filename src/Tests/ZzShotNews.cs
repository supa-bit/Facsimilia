using System.Collections.Generic;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the chronicle with far news arriving late.</summary>
public partial class ZzShotNews : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        map.Game.DiplomacyFog = FogLevel.Light;
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        int egypt = map.CivRealmIds["egypt"], carthage = map.CivRealmIds["carthage"];
        hud.LogEvents(new List<ChronicleEvent>
        {
            new(ChronicleKind.Succession, egypt, "Ptolemy dies; his son Ptolemy succeeds him in Egypt."),
            new(ChronicleKind.War, carthage, "Carthage declares war on Syracuse."),
        });
        map.Game.MonthsMarched = map.NewsMonths(egypt);
        hud.LogEvents(new List<ChronicleEvent>());
        map.ZoomToFit();
        hud.Refresh();
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/news.png");
        Finish("shot taken");
    }
}
