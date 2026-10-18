using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a few years in, the Samnites' card with their aims, and the busiest realm's full list.</summary>
public partial class ZzShotAims : TestRunner
{
    protected override async Task Run()
    {
        var map = new MapView { Name = "MapView" };
        Root.AddChild(map);
        map.SetPlayerCiv(OS.GetEnvironment("CIV") is { Length: > 0 } civ ? civ : "rome");
        await map.GenerateWorld();
        for (int y = 0; y < 7; y++)
            map.AdvanceYear();
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        hud.Refresh();
        hud.SetProcess(false);   // the mouse isn't on the cards: don't let the hover check close them
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        int samnites = map.CivRealmIds[OS.GetEnvironment("SHOW") is { Length: > 0 } show ? show : "samnites"];
        string card = (string)typeof(Hud).GetMethod("CardFor", flags)!.Invoke(hud, new object[] { $"realm:{samnites}" })!;
        typeof(Hud).GetMethod("OpenCard", flags)!.Invoke(hud, new object[] { 0, card, new Vector2(40, 120) });
        int busiest = map.Registry.Realms.Keys.Where(r => r != map.PlayerRealmId).OrderByDescending(r => map.AimsOf(r).Count).ThenBy(r => r).First();
        foreach (var r in map.Registry.Realms.Keys.Where(r => r != map.PlayerRealmId).Where(r => map.AimsOf(r).Count > 1))
            GD.Print($"AIMS {map.RealmName(r)}: {string.Join(" | ", map.AimsOf(r).Select(a => a.Text))}");
        string all = (string)typeof(Hud).GetMethod("AimsCard", flags)!.Invoke(hud, new object[] { busiest, true })!;
        typeof(Hud).GetMethod("OpenCard", flags)!.Invoke(hud, new object[] { 1, all, new Vector2(760, 120) });
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/aims.png");
        Finish("shot taken");
    }
}
