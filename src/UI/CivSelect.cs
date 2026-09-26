using System.Linq;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Realm selection at the start of a new game: a card per playable civ with
/// its map color, region and description. The whole card is the click target.
/// </summary>
public partial class CivSelect : Control
{
    [Signal] public delegate void CivChosenEventHandler(string civKey);

    sealed record CivInfo(string Key, string Name, string Region, string Blurb);

    // Every realm of 300 BC (data/realms_bc300.json), grouped by region.
    static readonly CivInfo[] Civs = MapView.RealCivs
        .Select(c => new CivInfo(c.Key, c.RealmName!, c.Region, c.Blurb + (c.Tribal ? " (A people of many tribes: no provinces at the start.)" : "")))
        .ToArray();
    const string DefaultKey = "rome";
    static readonly Vector2 CardSize = new(372, 150);

    bool _selectionMade;  // guards against a double-fire from overlapping click handlers

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ThemeAncient.FullRect(this);
        AddChild(ThemeAncient.Backdrop(0.72f));
        var center = ThemeAncient.FullRect(new CenterContainer());
        AddChild(center);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        center.AddChild(column);
        column.AddChild(ThemeAncient.Label("CHOOSE YOUR REALM", "HeaderLabel", 46, HorizontalAlignment.Center));
        column.AddChild(ThemeAncient.Label($"The world as it stood in 300 BC: {Civs.Length} realms, each where history had it. Scroll for more.",
            "SubtleLabel", 21, HorizontalAlignment.Center));
        column.AddChild(ThemeAncient.Ornament(520));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(4 * CardSize.X + 3 * 14 + 24, 700),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        scroll.AddChild(grid);
        foreach (var civ in Civs)
            grid.AddChild(Card(civ));
        column.AddChild(ThemeAncient.Label("Click a realm to begin.", "SmallLabel", align: HorizontalAlignment.Center));
    }

    PanelContainer Card(CivInfo civ)
    {
        var panel = new PanelContainer
        {
            ThemeTypeVariation = "CardPanel",
            CustomMinimumSize = CardSize,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        panel.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                SelectCiv(civ.Key);
        };
        panel.MouseEntered += () => panel.ThemeTypeVariation = "CardPanelHover";
        panel.MouseExited += () => panel.ThemeTypeVariation = "CardPanel";

        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 14);
        panel.AddChild(row);
        row.AddChild(new ColorRect
        {
            Color = MapView.CivColor(civ.Key),
            CustomMinimumSize = new Vector2(8, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 2);
        row.AddChild(text);
        float textWidth = CardSize.X - 60;
        var name = ThemeAncient.Label(civ.Name, "HeaderLabel", 20);
        Wrap(name, textWidth);
        text.AddChild(name);
        text.AddChild(ThemeAncient.Label(civ.Region + (civ.Key == DefaultKey ? "  ·  suggested start" : ""), "SubtleLabel", 17));
        var blurb = ThemeAncient.Label(civ.Blurb, fontSize: 14);
        blurb.AddThemeColorOverride("font_color", ThemeAncient.Text);
        Wrap(blurb, textWidth);
        text.AddChild(blurb);
        foreach (var child in text.GetChildren())
            ((Control)child).MouseFilter = MouseFilterEnum.Ignore;
        return panel;
    }

    static void Wrap(Label label, float width)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(width, 0);
    }

    /// <summary>Picks a realm, as clicking its card does.</summary>
    public void SelectCiv(string civKey)
    {
        if (_selectionMade)
            return;
        _selectionMade = true;
        EmitSignal(SignalName.CivChosen, civKey);
    }
}
