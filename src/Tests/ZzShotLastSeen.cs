using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): an enemy army out of sight, drawn where it was last seen.</summary>
public partial class ZzShotLastSeen : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv("rome");
        await map.GenerateWorld();
        int rome = map.PlayerRealmId, carthage = map.CivRealmIds["carthage"];
        map.StartWar(carthage, rome, Pretexts.History);
        var army = map.Game.Realm(carthage).Armies.First(a => !a.IsEmpty);
        var pop = map.Population!;
        var known = map.Known();
        // Seen four months ago on the African coast, now somewhere out of sight.
        int seenAt = pop.LandNodes.Where(i => !known[i] && pop.NodeOwner[i] == carthage)
            .OrderBy(i => (map.NodeCell(i) - map.NodeCell(map.CapitalNode(carthage))).Length()).First();
        army.Node = pop.LandNodes.First(i => !known[i] && i != seenAt);
        map.Game.LastSeen[(carthage, army.Id)] = (seenAt, map.DemoYear * 12 + map.Game.MonthsMarched - 4);
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        map.CenterOn(map.NodeCell(seenAt) * MapView.CellPixels + new Vector2(150, -120), 6f);
        map.RefreshArmyMarkers();
        hud.Refresh();
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/lastseen.png");
        Finish("shot taken");
    }
}
