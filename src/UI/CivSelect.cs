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

    // MapView.RealCivs (real 300 BC boundary data) plus MapView.FrontierZones
    // (tribal regions with no unified state at this date).
    static readonly CivInfo[] Civs =
    {
        new("rome", "Rome", "Italy", "A modest city-state on the Tiber, one power among many on the Italian peninsula. History remembers what it became - not what it started as."),
        new("carthage", "Carthage", "North Africa", "The dominant trading power of the western Mediterranean, backed by a navy few can match."),
        new("egypt", "Ptolemaic Egypt", "Nile Valley", "Alexander's Macedonian generals still rule the Nile, richer than almost anyone."),
        new("kush", "Kingdom of Kush", "Nubia, south of Egypt", "An indigenous African kingdom on the upper Nile, with its own pharaohs, iron industry, and pyramids - never conquered by the Ptolemies to its north."),
        new("seleucid", "Seleucid Empire", "Syria / Mesopotamia", "The largest of Alexander's successor kingdoms, stretching deep into the east."),
        new("greek_world", "Greek World", "Macedon / Greece", "Kassander's Macedon and the southern Greek city-states, forever rivals to one another."),
        new("lysimachus", "Kingdom of Lysimachus", "Thrace", "One of Alexander's own bodyguards, now a king in his own right on the European side of the straits."),
        new("antigonus", "Kingdom of Antigonus", "Anatolia / Syria", "The One-Eyed's sprawling, contested holdings across Asia Minor and the Levant."),
        new("nabatea", "Nabatean Kingdom", "Arabia", "Desert traders controlling the incense routes, centered on their rock-cut capital."),
        new("iberia", "Iberian & Celtiberian Tribes", "Spain", "Fierce, fragmented tribal peoples across the peninsula, prized as mercenaries - no single king rules here yet."),
        new("gaul", "Gallic Tribes", "Gaul / the Alps", "Sprawling, fractious confederations north of the Alps, fiercely independent."),
        new("scythia", "Scythian Peoples", "Pontic Steppe", "Mounted nomads ranging the grasslands north of the Black Sea."),
    };
    const string DefaultKey = "rome";
    static readonly Vector2 CardSize = new(400, 176);

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
        column.AddChild(ThemeAncient.Label("The world as it stood in 300 BC. Every realm starts where history had it.",
            "SubtleLabel", 21, HorizontalAlignment.Center));
        column.AddChild(ThemeAncient.Ornament(520));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 18);
        column.AddChild(grid);
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
        var name = ThemeAncient.Label(civ.Name, "HeaderLabel", 22);
        Wrap(name, textWidth);
        text.AddChild(name);
        text.AddChild(ThemeAncient.Label(civ.Region + (civ.Key == DefaultKey ? "  ·  suggested start" : ""), "SubtleLabel", 17));
        var blurb = ThemeAncient.Label(civ.Blurb, fontSize: 16);
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
