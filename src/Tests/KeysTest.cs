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
        // The defaults (your own changed keys live in your settings, which tests never read).
        var keys = KeyBindings.All.Select(a => a.Default).ToList();
        Check(keys.All(k => k != Key.None), "every action should have a key");
        Check(keys.Distinct().Count() == keys.Count, "no key should do two things");
        Check(KeyBindings.All.First(a => a.Default == Key.F3).Id == "view_culture" && KeyBindings.All.First(a => a.Default == Key.Enter).Id == "advance", "the default keys");
        var views = MapView.ProvinceViews.Select(v => v.Key).Concat(new[] { MapView.PoliticalView, MapView.TradeView, MapView.RegionsView }).ToHashSet();
        Check(KeyBindings.ViewOf.Values.All(views.Contains) && views.All(KeyBindings.ViewOf.ContainsValue), "each map view should have its own key");
        Check(KeyBindings.ViewOf.Keys.All(id => KeyBindings.All.Any(a => a.Id == id)), "each view key should be a listed action");
        // What stops time: the old fixed list is ticked at first; others' news never stops it.
        var ticked = TimeStops.Defaults.ToList();
        Check(TimeStops.Stops(new(Dynasties.ChronicleKind.War, 7, "war"), 7, ticked) && !TimeStops.Stops(new(Dynasties.ChronicleKind.War, 8, "war"), 7, ticked),
            "your wars should stop time, others' should not");
        Check(!TimeStops.Stops(new(Dynasties.ChronicleKind.Birth, 7, "born"), 7, ticked) && TimeStops.Stops(new(Dynasties.ChronicleKind.Birth, 7, "born"), 7, new[] { "birth" }),
            "a birth should stop time only when ticked");
        Check(TimeStops.Stops(new(Dynasties.ChronicleKind.Economy, 7, "a famine"), 7, ticked) && !TimeStops.Stops(new(Dynasties.ChronicleKind.Economy, 7, "debt grows"), 7, ticked),
            "of the economy, only trouble should stop time");
        Check(TimeStops.All.Select(r => r.Id).Distinct().Count() == TimeStops.All.Length, "each reason should have its own id");
        Finish($"Keys tests passed: {KeyBindings.All.Length} actions, each with its own key.");
        return Task.CompletedTask;
    }
}
