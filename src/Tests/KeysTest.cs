using System.Linq;
using System.Threading.Tasks;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>The game's keys (Changeable keys, Map view keys): every action has its own key, and each view key shows a real view.</summary>
public partial class KeysTest : TestRunner
{
    protected override Task Run()
    {
        var keys = KeyBindings.All.Select(a => KeyBindings.KeyFor(a.Id)).ToList();
        Check(keys.All(k => k != Key.None), "every action should have a key");
        Check(keys.Distinct().Count() == keys.Count, "no key should do two things");
        Check(KeyBindings.ActionFor(Key.F3) == "view_culture" && KeyBindings.ActionFor(Key.Enter) == "advance", "the default keys");
        var views = MapView.ProvinceViews.Select(v => v.Key).Concat(new[] { MapView.PoliticalView, MapView.TradeView, MapView.RegionsView }).ToHashSet();
        Check(KeyBindings.ViewOf.Values.All(views.Contains) && views.All(KeyBindings.ViewOf.ContainsValue), "each map view should have its own key");
        Check(KeyBindings.ViewOf.Keys.All(id => KeyBindings.All.Any(a => a.Id == id)), "each view key should be a listed action");
        Finish($"Keys tests passed: {KeyBindings.All.Length} actions, each with its own key.");
        return Task.CompletedTask;
    }
}
