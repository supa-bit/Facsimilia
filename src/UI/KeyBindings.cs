using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The game's keys (your answers "Changeable keys": keys for everything,
/// changeable in Settings; and "Map view keys": a key for each view). Each
/// action has a plain name and a default key; Settings keeps any you change.
/// Esc (the menu) and the arrow keys (moving the map) always work as well.
/// </summary>
public static class KeyBindings
{
    public sealed record Action(string Id, string Name, Key Default);

    public static readonly Action[] All =
    {
        new("advance", "Play the year (end the turn)", Key.Enter),
        new("play", "Let the months run, or pause", Key.Space),
        new("mode_inspect", "Inspect", Key.Key1),
        new("mode_provinces", "Redraw provinces", Key.Key2),
        new("mode_conquest", "Plan a conquest", Key.Key3),
        new("pan_up", "Move the map up", Key.W),
        new("pan_left", "Move the map left", Key.A),
        new("pan_down", "Move the map down", Key.S),
        new("pan_right", "Move the map right", Key.D),
        new("view_political", "Map view: political", Key.F1),
        new("view_trade", "Map view: trade routes", Key.F2),
        new("view_culture", "Map view: culture", Key.F3),
        new("view_religion", "Map view: religion", Key.F4),
        new("view_loyalty", "Map view: loyalty", Key.F5),
        new("view_wealth", "Map view: wealth", Key.F6),
        new("view_people", "Map view: people", Key.F7),
        new("view_diplomacy", "Map view: diplomacy", Key.F8),
        new("view_regions", "Map view: regions", Key.F9),
        new("panel_goods", "Goods panel", Key.G),
        new("panel_research", "Research panel", Key.T),
        new("panel_realms", "Realms table", Key.L),
        new("panel_chronicle", "The whole chronicle", Key.C),
        new("fullscreen", "Fullscreen", Key.F11),
    };

    /// <summary>The map view each view action shows (keys from MapView's views).</summary>
    public static readonly Dictionary<string, string> ViewOf = new()
    {
        ["view_political"] = World.MapView.PoliticalView, ["view_trade"] = World.MapView.TradeView,
        ["view_culture"] = World.MapView.CultureView, ["view_religion"] = World.MapView.ReligionView,
        ["view_loyalty"] = World.MapView.LoyaltyView, ["view_wealth"] = World.MapView.WealthView,
        ["view_people"] = World.MapView.PeopleView, ["view_diplomacy"] = World.MapView.DiplomacyView,
        ["view_regions"] = World.MapView.RegionsView,
    };

    /// <summary>The key for an action now: yours if you changed it, else its default.</summary>
    public static Key KeyFor(string id)
    {
        if (SettingsStore.Instance is { } store && store.KeyOverrides.TryGetValue(id, out var k))
            return k;
        return All.FirstOrDefault(a => a.Id == id)?.Default ?? Key.None;
    }

    /// <summary>The action a key press means, or null.</summary>
    public static string? ActionFor(Key key) => All.FirstOrDefault(a => KeyFor(a.Id) == key)?.Id;

    /// <summary>A key's name as a player would say it.</summary>
    public static string Name(Key key) => key == Key.None ? "none" : OS.GetKeycodeString(key);

    /// <summary>"  (F3)" for a tooltip.</summary>
    public static string Hint(string id) => $"  ({Name(KeyFor(id))})";
}
