using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>The search box (your answer "Search"): type a name, click what it finds to go there.</summary>
public partial class Hud
{
    PanelContainer? _searchPanel;
    LineEdit _searchBox = null!;
    VBoxContainer _searchRows = null!;

    public void ToggleSearch()
    {
        if (_searchPanel == null)
        {
            _searchPanel = SidePanel("Find", () => _searchPanel!.Visible = false, 520);
            var box = _searchPanel.GetNode<VBoxContainer>("Box");
            _searchBox = new LineEdit { PlaceholderText = "A province, town, person or realm...", ClearButtonEnabled = true };
            _searchBox.TextChanged += _ => RefreshSearch();
            box.AddChild(_searchBox);
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(500, 420), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            box.AddChild(scroll);
            _searchRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            scroll.AddChild(_searchRows);
        }
        _searchPanel.Visible = !_searchPanel.Visible;
        if (_searchPanel.Visible)
        {
            _searchPanel.MoveToFront();
            _searchBox.GrabFocus();
            RefreshSearch();
        }
    }

    void RefreshSearch()
    {
        foreach (Node child in _searchRows.GetChildren())
            child.QueueFree();
        var found = _map.Search(_searchBox.Text);
        if (found.Count == 0)
            _searchRows.AddChild(ThemeAncient.Label(_searchBox.Text.Trim().Length < 2 ? "Type two letters or more." : "Nothing your realm knows of by that name.",
                "SubtleLabel", 15));
        foreach (var f in found)
        {
            var b = new Button { Text = $"{f.Name}  ·  {f.Detail}", Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None };
            b.Pressed += () => Go(f);
            _searchRows.AddChild(b);
        }
    }

    void Go(MapView.Found f)
    {
        switch (f.Kind)
        {
            case MapView.FoundKind.Province:
                _map.SelectProvince(f.Id);
                _map.CenterOn(f.Where, 3f);
                break;
            case MapView.FoundKind.Town:
                _map.CenterOn(f.Where, 4f);
                break;
            case MapView.FoundKind.Person:
                ShowPerson(f.Id);
                break;
            case MapView.FoundKind.Realm:
                if (f.Id == _map.PlayerRealmId)
                    ToggleRealmPanel(true);
                else
                    ToggleDiplomacyPanel(true);
                break;
        }
    }
}
