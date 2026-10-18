using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a province's hover card with the realm's own card opened from it.</summary>
public partial class ZzShotHover : TestRunner
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
        hud.Refresh();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var prov = map.Provinces!.Provinces.Values.First(p => p.Name == "Campania");
        int samnites = map.CivRealmIds["samnites"];
        string first = (string)typeof(Hud).GetMethod("HoverText", flags)!.Invoke(hud, new object[] { prov.Id })!;
        typeof(Hud).GetMethod("OpenCard", flags)!.Invoke(hud, new object[] { 0, first, new Vector2(600, 400) });
        string second = (string)typeof(Hud).GetMethod("CardFor", flags)!.Invoke(hud, new object[] { $"realm:{samnites}" })!;
        typeof(Hud).GetMethod("OpenCard", flags)!.Invoke(hud, new object[] { 1, second, new Vector2(700, 460) });
        string culture = map.RealmPeople(samnites).Culture;
        string third = (string)typeof(Hud).GetMethod("CardFor", flags)!.Invoke(hud, new object[] { $"culture:{culture}" })!;
        typeof(Hud).GetMethod("OpenCard", flags)!.Invoke(hud, new object[] { 2, third, new Vector2(800, 560) });
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/hover.png");
        Finish("shot taken");
    }
}
