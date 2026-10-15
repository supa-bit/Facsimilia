using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): a siege's spare/plunder/raze choice in the armies panel, and a ruin in the province panel.</summary>
public partial class ZzShotSack : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var legio = map.PlayerState.Armies[0];
        var etruria = map.Provinces!.Provinces.Values.First(p => p.Name == "Etruria");
        map.Game.Sieges.Add(new Siege
        {
            Attacker = map.PlayerRealmId, Owner = etruria.RealmId, ProvinceId = etruria.Id, ArmyId = legio.Id,
            Name = "Veii", Node = legio.Node, StartYear = map.DemoYear, Progress = 0.4, Choice = SackChoice.Raze,
        });
        legio.Regiments.Insert(0, new Regiment { Type = UnitCatalog.Instance["hastati_principes"].Index, Men = 1000, Xp = 0.9 });
        map.PlayerState.ArmWins[UnitRoles.HeavyInfantry] = 4;
        map.PlayerState.Treasury += 500;
        map.PlayerState.ReformsOffered.Add("lex_militaris");
        map.PlayerState.ReformsPending["scipio_spain"] = 1;
        map.PlayerState.Reforms["velites"] = map.DemoYear;
        map.RaiseUnit(map.PlayerState, UnitCatalog.Instance["equites"], legio);
        map.RaiseUnit(map.PlayerState, UnitCatalog.Instance["hastati_principes"], legio);
        legio.LoyalTo = legio.GeneralId;
        legio.GeneralLoyalty = 0.7;
        ArmyLoyalty.Discharge(map.PlayerState, 3200, 0.5, true, map.DemoYear - 2);
        map.PlayerState.VeteransOfferSilver = true;
        var latium = map.Provinces.Provinces.Values.First(p => p.Name == "Latium");
        map.Game.Ruins.Add(new Ruin(latium.Id, "Norba", map.DemoYear - 2, etruria.RealmId));
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var hud = GD.Load<PackedScene>("res://scenes/Hud.tscn").Instantiate<Hud>();
        hud.Theme = ThemeAncient.Build();
        layer.AddChild(hud);
        hud.Setup(map);
        hud.Refresh();
        ((Control)hud.Get("_armiesPanel")).Visible = true;
        hud.Call("RefreshArmiesPanel");
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        string dir = OS.GetEnvironment("SHOTDIR");
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/sack.png");
        ((Control)hud.Get("_armiesPanel")).Visible = false;
        map.SelectProvince(latium.Id);
        hud.Refresh();
        ((TabContainer)hud.Get("_provinceTabs")).CurrentTab = 1;   // Land
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/ruin.png");
        map.SelectProvince(map.Provinces.Provinces.Values.First(p => p.Name == "Campania").Id);
        hud.Refresh();
        ((TabContainer)hud.Get("_provinceTabs")).CurrentTab = 1;
        for (int i = 0; i < 20; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(dir + "/capital.png");
        Finish("shot taken");
    }
}
