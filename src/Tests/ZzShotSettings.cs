using System.Threading.Tasks;
using Facsimilia.UI;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Screenshot helper (not a test): the Settings panel's Gameplay tab.</summary>
public partial class ZzShotSettings : TestRunner
{
    protected override async Task Run()
    {
        var layer = new CanvasLayer { Layer = 1 };
        Root.AddChild(layer);
        var panel = GD.Load<PackedScene>("res://scenes/SettingsPanel.tscn").Instantiate<Control>();
        panel.Theme = ThemeAncient.Build();
        layer.AddChild(panel);
        for (int i = 0; i < 5; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        var tabs = panel.FindChildren("*", "TabContainer", true, false)[0] as TabContainer;
        tabs!.CurrentTab = 2;
        for (int i = 0; i < 10; i++)
            await ToSignal(this, SceneTree.SignalName.ProcessFrame);
        Root.GetViewport().GetTexture().GetImage().SavePng(OS.GetEnvironment("SHOTDIR") + "/settings.png");
        Finish("shot taken");
    }
}
