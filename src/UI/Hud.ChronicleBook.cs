using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The whole chronicle (your answer "A searchable chronicle": filter by
/// kind, realm and place, search, and read the whole game's history):
/// every line the chronicle has written since 300 BC, newest first.
/// </summary>
public partial class Hud
{
    PanelContainer? _bookPanel;
    LineEdit _bookSearch = null!;
    OptionButton _bookKind = null!, _bookRealm = null!, _bookPlace = null!;
    RichTextLabel _bookText = null!;
    Label _bookCount = null!;
    readonly List<int> _bookRealmIds = new();
    readonly List<string> _bookPlaces = new();

    /// <summary>The kinds of line, in plain words, and which chronicle kinds each covers.</summary>
    static readonly (string Name, ChronicleKind[] Kinds)[] BookKinds =
    {
        ("Every kind", System.Array.Empty<ChronicleKind>()),
        ("Wars", new[] { ChronicleKind.War }),
        ("Peace", new[] { ChronicleKind.Peace }),
        ("Conquests", new[] { ChronicleKind.Conquest }),
        ("Revolts", new[] { ChronicleKind.Revolt }),
        ("Disasters", new[] { ChronicleKind.Disaster }),
        ("Rulers and houses", new[] { ChronicleKind.Succession, ChronicleKind.NewHouse }),
        ("Families", new[] { ChronicleKind.Death, ChronicleKind.Marriage, ChronicleKind.Birth }),
        ("The treasury and works", new[] { ChronicleKind.Economy }),
    };

    /// <summary>Most lines shown at once (the newest); a search finds older ones.</summary>
    const int BookMaxLines = 3000;

    public void ShowChronicleBook()
    {
        if (_bookPanel == null)
            BuildChronicleBook();
        FillBookFilters();
        RefreshChronicleBook();
        _bookPanel!.Visible = true;
        _bookPanel.MoveToFront();
    }

    void BuildChronicleBook()
    {
        _bookPanel = SidePanel("The Chronicle", () => _bookPanel!.Visible = false, 900);
        var box = _bookPanel.GetNode<VBoxContainer>("Box");
        var filters = new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation", 8);
        filters.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(filters);
        _bookSearch = new LineEdit { PlaceholderText = "Search the chronicle...", CustomMinimumSize = new Vector2(260, 0), ClearButtonEnabled = true };
        _bookSearch.TextChanged += _ => RefreshChronicleBook();
        filters.AddChild(_bookSearch);
        _bookKind = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Which kind of news" };
        foreach (var (name, _) in BookKinds)
            _bookKind.AddItem(name);
        _bookKind.ItemSelected += _ => RefreshChronicleBook();
        filters.AddChild(_bookKind);
        _bookRealm = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "News of one realm" };
        _bookRealm.ItemSelected += _ => RefreshChronicleBook();
        filters.AddChild(_bookRealm);
        _bookPlace = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "News that names one place" };
        _bookPlace.ItemSelected += _ => RefreshChronicleBook();
        filters.AddChild(_bookPlace);
        _bookCount = ThemeAncient.Label("", "SubtleLabel", 15);
        box.AddChild(_bookCount);
        _bookText = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(860, 600),
            ScrollActive = true,
            SelectionEnabled = true,
            BbcodeEnabled = true,
        };
        _bookText.AddThemeFontSizeOverride("normal_font_size", 16);
        _bookText.AddThemeColorOverride("default_color", ThemeAncient.Text);
        box.AddChild(_bookText);
    }

    /// <summary>The realm and place choices: the realms and provinces the chronicle has named.</summary>
    void FillBookFilters()
    {
        var chronicle = _map.Game.Chronicle;
        int realmPick = _bookRealm.Selected >= 0 && _bookRealm.Selected < _bookRealmIds.Count ? _bookRealmIds[_bookRealm.Selected] : -1;
        string placePick = _bookPlace.Selected > 0 && _bookPlace.Selected < _bookPlaces.Count ? _bookPlaces[_bookPlace.Selected] : "";
        _bookRealmIds.Clear();
        _bookRealm.Clear();
        _bookRealmIds.Add(-1);
        _bookRealm.AddItem("Every realm");
        _bookRealmIds.Add(_map.PlayerRealmId);
        _bookRealm.AddItem("Yours: " + _map.RealmName(_map.PlayerRealmId));
        foreach (var id in chronicle.Select(e => e.Realm).Where(id => id > 0 && id != _map.PlayerRealmId).Distinct()
                     .OrderBy(id => _map.RealmName(id)))
        {
            _bookRealmIds.Add(id);
            _bookRealm.AddItem(_map.RealmName(id));
        }
        _bookRealm.Selected = System.Math.Max(0, _bookRealmIds.IndexOf(realmPick));

        _bookPlaces.Clear();
        _bookPlace.Clear();
        _bookPlaces.Add("");
        _bookPlace.AddItem("Every place");
        var names = _map.Provinces?.Provinces.Values.Select(p => p.Name).Where(n => n.Length > 2).Distinct().ToList() ?? new List<string>();
        var text = string.Join("\n", chronicle.Select(e => e.Text));
        foreach (var name in names.Where(n => text.Contains(n)).OrderBy(n => n))
        {
            _bookPlaces.Add(name);
            _bookPlace.AddItem(name);
        }
        _bookPlace.Selected = System.Math.Max(0, _bookPlaces.IndexOf(placePick));
    }

    /// <summary>
    /// The chronicle's lines that match, newest first: of these kinds (none:
    /// every kind), about this realm (by its id or its name in the line; -1:
    /// every realm), naming this place, and containing the search words.
    /// </summary>
    public static List<(int Year, int Kind, int Realm, string Text)> FilterChronicle(
        IEnumerable<(int Year, int Kind, int Realm, string Text)> chronicle, ChronicleKind[] kinds, int realm, string realmName, string place, string search) =>
        chronicle.Where(e =>
                (kinds.Length == 0 || kinds.Contains((ChronicleKind)e.Kind))
                && (realm < 0 || e.Realm == realm || (realmName != "" && e.Text.Contains(realmName)))
                && (place == "" || e.Text.Contains(place))
                && (search == "" || e.Text.Contains(search, System.StringComparison.OrdinalIgnoreCase)))
            .Reverse().ToList();

    void RefreshChronicleBook()
    {
        if (_bookPanel == null)
            return;
        var kinds = BookKinds[System.Math.Max(0, _bookKind.Selected)].Kinds;
        int realm = _bookRealm.Selected >= 0 && _bookRealm.Selected < _bookRealmIds.Count ? _bookRealmIds[_bookRealm.Selected] : -1;
        string place = _bookPlace.Selected > 0 && _bookPlace.Selected < _bookPlaces.Count ? _bookPlaces[_bookPlace.Selected] : "";
        string search = _bookSearch.Text.Trim();
        var lines = FilterChronicle(_map.Game.Chronicle, kinds, realm, realm < 0 ? "" : _map.RealmName(realm), place, search);
        _bookCount.Text = lines.Count == 0 ? "Nothing in the chronicle matches."
            : $"{lines.Count:N0} line{(lines.Count == 1 ? "" : "s")}, newest first" + (lines.Count > BookMaxLines ? $" (the newest {BookMaxLines:N0} shown; search to find older ones)" : "") + ".";
        var sb = new System.Text.StringBuilder();
        int? year = null;
        foreach (var e in lines.Take(BookMaxLines))
        {
            if (e.Year != year)
            {
                year = e.Year;
                sb.Append($"\n[color=#{ThemeAncient.Gold.ToHtml(false)}]{ThemeAncient.YearText(e.Year)}[/color]\n");
            }
            sb.Append("    ").Append(e.Text.Replace("[", "[lb]")).Append('\n');
        }
        _bookText.Text = sb.ToString().TrimStart('\n');
    }
}
