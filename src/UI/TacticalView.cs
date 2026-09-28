using System;
using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The battle map (decision "Next 1": battles on a tactical map, on land and
/// at sea). The ground where the armies met, each side's troops in blocks.
/// Click one of your blocks, then a lit square to move it or a marked enemy
/// to strike; End turn lets the enemy move; "Let the general decide" plays
/// the rest for you. The result goes back to the campaign map.
/// </summary>
public partial class TacticalView : Control
{
    [Signal] public delegate void FinishedEventHandler();

    const int TileSize = 58;
    static readonly string[] RoleShort = { "Foot", "Light", "Bows", "Horse", "H. arch.", "Elephants", "Ships" };

    readonly MapView.PendingBattle _p;
    readonly TacticalBattle _t;
    readonly Color[] _colors;
    Block? _selected;
    Vector2 _origin;
    Label _title = null!, _info = null!, _log = null!;
    Button _endTurn = null!, _auto = null!, _done = null!;

    public TacticalView(MapView.PendingBattle p, Color attacker, Color defender)
    {
        _p = p;
        _t = p.Tactics;
        _colors = new[] { attacker, defender };
    }

    int Me => _p.Side;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Size = GetViewportRect().Size;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 50;
        GetViewport().SizeChanged += () => Size = GetViewportRect().Size;
        var panel = new PanelContainer { ThemeTypeVariation = "GlassPanel", CustomMinimumSize = new Vector2(420, 0) };
        panel.SetAnchorsPreset(LayoutPreset.RightWide);
        panel.OffsetLeft = -440;
        panel.OffsetRight = -16;
        panel.OffsetTop = 16;
        panel.OffsetBottom = -16;
        AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        _title = ThemeAncient.Label("", "HeaderLabel", 22);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_title);
        _info = ThemeAncient.Label("", fontSize: 16);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_info);
        _endTurn = new Button { Text = "End turn", FocusMode = FocusModeEnum.None };
        _endTurn.Pressed += EndTurn;
        box.AddChild(_endTurn);
        _auto = new Button { Text = "Let the general decide", FocusMode = FocusModeEnum.None, TooltipText = "Your general fights the rest of the battle." };
        _auto.Pressed += () =>
        {
            _t.PlayOut();
            _selected = null;
            Refresh();
        };
        box.AddChild(_auto);
        _done = new Button { Text = "Return to the map", FocusMode = FocusModeEnum.None, Visible = false };
        _done.Pressed += () => EmitSignal(SignalName.Finished);
        box.AddChild(_done);
        box.AddChild(ThemeAncient.Label("How the day went", "HeaderLabel", 18));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _log = ThemeAncient.Label("", "SubtleLabel", 15);
        _log.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _log.CustomMinimumSize = new Vector2(380, 0);
        scroll.AddChild(_log);
        if (_t.Turn != Me)
            _t.PlayTurn();   // the attacker moves first
        Refresh();
    }

    void EndTurn()
    {
        if (_t.Winner != null)
            return;
        _t.EndTurn();
        if (_t.Winner == null && _t.Turn != Me)
            _t.PlayTurn();
        _selected = null;
        Refresh();
    }

    void Refresh()
    {
        string you = _t.SideNames[Me], them = _t.SideNames[1 - Me];
        _title.Text = $"{(_t.Naval ? "Sea battle" : "Battle")} of {_p.Place}: {you} against {them}";
        string help = _t.Winner != null
            ? (_t.Winner == Me ? "Victory! The field is yours." : "Defeat. Your troops fall back.")
            : $"Round {_t.Round} of {TacticalBattle.MaxRounds}. " +
              "Click one of your blocks, then a lit square to move or a red-ringed enemy to strike. Each block may move, then strike, once a turn.";
        if (_selected is { } b && _t.Winner == null)
            help += $"\n\nSelected: {b.Name} ({RoleShort[b.Role]}), {b.Men:N0} men, strength {b.Share:P0}, morale {Math.Max(0, b.Morale):P0}." +
                (b.Moved ? " (moved)" : "") + (b.Acted ? " (has fought)" : "");
        help += $"\n\nStanding: {you} {_t.Standing(Me):P0}, {them} {_t.Standing(1 - Me):P0}. A side breaks when less than 30% stands.";
        help += "\n\nGround: " + (_t.Naval ? "open sea" : "hills (brown) shield those on them; forest (dark green) hides light troops and hampers horse; marsh and river (blue) slow and weaken.");
        _info.Text = help;
        _log.Text = string.Join("\n", _t.Log.AsEnumerable().Reverse());
        _endTurn.Visible = _auto.Visible = _t.Winner == null;
        _done.Visible = _t.Winner != null;
        QueueRedraw();
    }

    Rect2 TileRect(int x, int y) => new(_origin + new Vector2(x * TileSize, y * TileSize), new Vector2(TileSize - 2, TileSize - 2));

    static Color TileColor(Tile t) => t switch
    {
        Tile.Hills => new Color(0.55f, 0.45f, 0.3f),
        Tile.Forest => new Color(0.2f, 0.35f, 0.18f),
        Tile.Marsh => new Color(0.35f, 0.45f, 0.38f),
        Tile.River => new Color(0.25f, 0.4f, 0.6f),
        Tile.Water => new Color(0.16f, 0.3f, 0.45f),
        _ => new Color(0.52f, 0.55f, 0.35f),
    };

    public override void _Draw()
    {
        var size = GetRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.06f, 0.05f, 0.04f, 0.97f));
        float gridW = TacticalBattle.Width * TileSize, gridH = TacticalBattle.Height * TileSize;
        _origin = new Vector2(Math.Max(16, (size.X - 460 - gridW) / 2), Math.Max(16, (size.Y - gridH) / 2));
        var font = ThemeDB.FallbackFont;
        var reach = _selected != null && !_selected.Moved && !_selected.Acted && _t.Turn == Me ? _t.Reach(_selected) : new();
        for (int x = 0; x < TacticalBattle.Width; x++)
            for (int y = 0; y < TacticalBattle.Height; y++)
            {
                var r = TileRect(x, y);
                var tile = _t.Map[x, y];
                DrawRect(r, TileColor(tile));
                if (tile == Tile.Hills)
                    DrawString(font, r.Position + new Vector2(18, 38), "^^", HorizontalAlignment.Left, -1, 18, new Color(0.3f, 0.22f, 0.12f));
                if (tile == Tile.Forest)
                    DrawCircle(r.GetCenter(), 12, new Color(0.12f, 0.24f, 0.1f));
                if (reach.Contains((x, y)))
                    DrawRect(r, new Color(1f, 0.95f, 0.6f, 0.28f));
            }
        foreach (var b in _t.Blocks.Where(b => b.Alive))
        {
            var r = TileRect(b.X, b.Y).Grow(-4);
            var c = _colors[b.Side];
            DrawRect(r, c.Darkened(b.Side == Me && (b.Moved || b.Acted) ? 0.45f : 0.1f));
            DrawRect(r, b.Side == Me ? new Color(1f, 0.95f, 0.8f) : new Color(0.1f, 0.05f, 0.05f), false, 2);
            DrawString(font, r.Position + new Vector2(3, 16), RoleShort[b.Role], HorizontalAlignment.Left, r.Size.X - 4, 12, Colors.White);
            // Strength and morale bars.
            DrawRect(new Rect2(r.Position + new Vector2(3, r.Size.Y - 14), new Vector2((r.Size.X - 6) * (float)b.Share, 5)), new Color(0.9f, 0.85f, 0.4f));
            DrawRect(new Rect2(r.Position + new Vector2(3, r.Size.Y - 7), new Vector2((r.Size.X - 6) * (float)Math.Clamp(b.Morale, 0, 1), 4)), new Color(0.5f, 0.8f, 1f));
            if (_selected != null && _t.CanStrike(_selected, b))
                DrawRect(r.Grow(3), new Color(1f, 0.2f, 0.15f), false, 3);
        }
        if (_selected != null)
            DrawRect(TileRect(_selected.X, _selected.Y).Grow(1), new Color(1f, 1f, 0.6f), false, 3);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || _t.Winner != null || _t.Turn != Me)
            return;
        var cell = (mb.Position - _origin) / TileSize;
        int x = (int)Math.Floor(cell.X), y = (int)Math.Floor(cell.Y);
        if (x < 0 || y < 0 || x >= TacticalBattle.Width || y >= TacticalBattle.Height)
            return;
        var there = _t.BlockAt(x, y);
        if (there != null && there.Side == Me)
            _selected = there;
        else if (_selected != null && there != null && _t.CanStrike(_selected, there))
            _t.Strike(_selected, there);
        else if (_selected != null && there == null)
            _t.Move(_selected, x, y);
        Refresh();
        AcceptEvent();
    }
}
