using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The history of your realm (decision "Next 47"): shown when the game
/// ends (AD 2000, or your realm falls) and any time from Goals ("Your
/// history so far"). Score by category, how closely the world followed
/// history, your rulers, and your people and score over the centuries.
/// </summary>
public partial class Hud
{
    PanelContainer? _endPanel;

    public void ShowEndScreen(bool final)
    {
        _endPanel?.QueueFree();
        _endPanel = SidePanel(final ? "The End of Your Chronicle" : "Your History So Far", () => _endPanel!.Visible = false, 900);
        var box = _endPanel.GetNode<VBoxContainer>("Box");
        var realm = _map.GetPlayerRealm();
        var s = _map.PlayerState;
        bool fallen = !_map.RealmCensus().ContainsKey(_map.PlayerRealmId);
        string span = $"{ThemeAncient.YearText(MapView.StartYear)} to {ThemeAncient.YearText(_map.DemoYear)}";
        var head = ThemeAncient.Label(fallen
            ? $"{realm.Name}, {span}. Your realm has fallen; its story ends here."
            : $"{realm.Name}, {span}.", "HeaderLabel", 20);
        head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(head);

        var score = string.Join("   ", _map.ScoreBreakdown(_map.PlayerRealmId).Select(x => $"{x.Category} {x.Points:0}"));
        box.AddChild(ThemeAncient.Label($"Score {_map.Score(_map.PlayerRealmId):0}:  {score}", fontSize: 17));
        var (share, matched, checkedN) = _map.HistoryCloseness();
        var close = ThemeAncient.Label(checkedN == 0
            ? "Closeness to history: none of history's campaigns has come due yet."
            : $"Closeness to history: {share:P0}. Of {checkedN} campaigns history fought by now, {matched} turned out as they did " +
              (share > 0.75 ? "(the world you made is much like ours)." : share > 0.4 ? "(a world that took its own turns)." : "(a very different world from ours)."),
            fontSize: 17);
        close.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        close.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(close);

        var rulers = string.Join(" · ", _map.Game.RulerLog.Select(r => $"{r.Name} ({ThemeAncient.YearText(r.Year)})"));
        var rl = ThemeAncient.Label($"Your rulers: {rulers}", "SubtleLabel", 15);
        rl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rl.CustomMinimumSize = new Vector2(860, 0);
        box.AddChild(rl);

        if (s.History.Count >= 2)
        {
            var peak = s.History.OrderByDescending(h => h[1]).First();
            box.AddChild(ThemeAncient.Label($"At your height, {ThemeAncient.YearText((int)peak[0])}: {ThemeAncient.GroupThousands((long)peak[1])} people.", fontSize: 16));
            foreach (var (title, index) in new[] { ("People", 1), ("Score", 4) })
            {
                box.AddChild(ThemeAncient.Label(title + " over the centuries", "SubtleLabel", 15));
                var g = new RealmGraph { CustomMinimumSize = new Vector2(860, 140) };
                g.Series = new() { (realm.Color, true, s.History.Select(h => new Vector2(h[0], h[index])).ToList()) };
                box.AddChild(g);
            }
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        box.AddChild(row);
        if (!fallen)
        {
            var more = new Button { Text = final ? "Keep playing anyway" : "Back to the game", FocusMode = FocusModeEnum.None };
            more.Pressed += () => _endPanel!.Visible = false;
            row.AddChild(more);
        }
        var menu = new Button { Text = "Main menu", FocusMode = FocusModeEnum.None };
        menu.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        row.AddChild(menu);
        _endPanel.Visible = true;
        _endPanel.MoveToFront();
    }
}
