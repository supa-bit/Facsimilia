using System;
using System.Linq;
using System.Collections.Generic;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The HUD's realm parts: the treasury in the top bar, the "Treasury and
/// Army" panel (accounts, tax rate, manpower, raising and disbanding units,
/// Might), and the turn-length choice by the turn button.
/// </summary>
public partial class Hud
{
    Button _treasuryButton = null!;
    PanelContainer _realmPanel = null!;
    Label _accounts = null!, _manpower = null!, _might = null!, _realmHint = null!;
    readonly Button[] _taxButtons = new Button[4];
    OptionButton _turnLength = null!;
    Button _advanceButton = null!;

    static readonly string[] TaxNames = { "Low", "Normal", "Heavy", "Crushing" };
    VBoxContainer _remedies = null!;
    static readonly string[] TaxTips =
    {
        "6% of output. Families grow faster; people are drawn to your land.",
        "10% of output: the usual ancient rate.",
        "16% of output. Growth slows; unrest will rise.",
        "25% of output. People flee and families shrink.",
    };

    /// <summary>Top bar: the treasury, clickable to open the panel.</summary>
    void BuildTreasuryStat(HBoxContainer row)
    {
        var button = new Button
        {
            Flat = true,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Treasury, in talents of silver (1 talent = 6,000 drachmae). Click for your accounts and army.",
            Icon = ThemeAncient.Icon("coins"),
            ExpandIcon = false,
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += () => ToggleRealmPanel();
        row.AddChild(button);
        _treasuryButton = button;
    }

    void BuildRealmPanel()
    {
        _realmPanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(430, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _realmPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        _realmPanel.OffsetLeft = _realmPanel.OffsetRight = 20;
        _realmPanel.OffsetTop = _realmPanel.OffsetBottom = 80;
        AddChild(_realmPanel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _realmPanel.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        var title = ThemeAncient.Label("Treasury", "HeaderLabel", 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(IconButton(null, "×", "Close", () => ToggleRealmPanel(false)));

        _accounts = ThemeAncient.Label("", fontSize: 16);
        box.AddChild(_accounts);

        var taxRow = new HBoxContainer();
        taxRow.AddThemeConstantOverride("separation", 4);
        box.AddChild(taxRow);
        taxRow.AddChild(ThemeAncient.Label("Taxes:", fontSize: 17));
        var group = new ButtonGroup();
        for (int i = 0; i < 4; i++)
        {
            int rate = i;
            var b = new Button
            {
                Text = TaxNames[i], ToggleMode = true, ButtonGroup = group, TooltipText = TaxTips[i],
                FocusMode = FocusModeEnum.None,
            };
            b.Pressed += () =>
            {
                _map.PlayerState.Tax = (TaxRate)rate;
                RefreshRealmPanel();
            };
            taxRow.AddChild(b);
            _taxButtons[i] = b;
        }

        _remedies = new VBoxContainer();
        _remedies.AddThemeConstantOverride("separation", 4);
        box.AddChild(_remedies);
        _manpower = ThemeAncient.Label("", fontSize: 16);
        box.AddChild(_manpower);
        _might = ThemeAncient.Label("", fontSize: 16);
        box.AddChild(_might);
        _realmHint = ThemeAncient.Label("", "SubtleLabel", 14);
        _realmHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _realmHint.CustomMinimumSize = new Vector2(400, 0);
        box.AddChild(_realmHint);
    }

    public void ShowRealmPanel() => ToggleRealmPanel(true);

    void ToggleRealmPanel(bool? show = null)
    {
        _realmPanel.Visible = show ?? !_realmPanel.Visible;
        if (_realmPanel.Visible)
        {
            _diplomacyPanel.Visible = false;
            _goodsPanel.Visible = false;
            _goalsPanel.Visible = false;
            _guidePanel.Visible = false;
            _armiesPanel.Visible = false;
            _researchPanel.Visible = false;
            RefreshRealmPanel();
        }
        RefreshMapView();   // the land legend shares the top-left corner
    }

    static string T(double talents) => $"{ThemeAncient.GroupThousands((long)Math.Round(talents))}";

    void RefreshTreasury()
    {
        var s = _map.PlayerState;
        _treasuryButton.Text = s.Debt > 0.5 ? $"{T(s.Treasury)}  (debt {T(s.Debt)})" : T(s.Treasury);
        _treasuryButton.Modulate = s.Debt > 0.5 ? new Color(1f, 0.75f, 0.7f) : Colors.White;
        if (_realmPanel.Visible)
            RefreshRealmPanel();
    }

    void RefreshRealmPanel()
    {
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        var (tax, tribute) = Economy.Revenue(c, s.Tax);
        double upkeep = Economy.Upkeep(s), admin = Economy.AdminPerProvince * c.Provinces + c.BuildingUpkeep, interest = s.Debt * Economy.InterestRate;
        double net = tax + tribute + (c.Goods != null ? Trade.Customs(c.Goods) : 0) - upkeep - admin - interest;
        _accounts.Text =
            $"Treasury: {T(s.Treasury)} talents" + (s.Debt > 0.5 ? $"   Debt: {T(s.Debt)} (10% interest)" : "") + "\n" +
            $"Each year at this rate:\n" +
            $"   Taxes +{T(tax)}   Tribute from unorganized land +{T(tribute)}" +
            (c.Goods != null ? $"   Tolls and customs +{T(Trade.Customs(c.Goods))}" : "") + "\n" +
            $"   Administration −{T(admin)} ({c.Provinces} provinces" + (c.BuildingUpkeep > 0.5 ? $", buildings {T(c.BuildingUpkeep)}" : "") + ")\n" +
            $"     Army −{T(upkeep)}" +
            (interest > 0.5 ? $"   Interest −{T(interest)}" : "") + "\n" +
            (s.LastCaptiveSales > 0.5 ? $"   Last year's sale of captives +{T(s.LastCaptiveSales)}\n" : "") +
            $"   Balance {(net >= 0 ? "+" : "−")}{T(Math.Abs(net))} talents a year";
        for (int i = 0; i < 4; i++)
            _taxButtons[i].SetPressedNoSignal((int)s.Tax == i);
        RefreshRemedies();
        RefreshRival();
        _manpower.Text = $"Men who can be called up: {ThemeAncient.GroupThousands((long)s.Manpower)}" +
            $" (refills toward {ThemeAncient.GroupThousands((long)Economy.SustainableManpower(c, s.ManpowerMultiplier))})." +
            "\nBeyond them, mercenaries can be hired at twice the price." +
            (c.Free + c.Dependent + c.Enslaved > 0
                ? $"\nYour people: {c.Free / (c.Free + c.Dependent + c.Enslaved):P0} free, " +
                  $"{c.Dependent / (c.Free + c.Dependent + c.Enslaved):P0} dependent, " +
                  $"{c.Enslaved / (c.Free + c.Dependent + c.Enslaved):P0} enslaved" +
                  (s.Captives >= 1 ? $" ({ThemeAncient.GroupThousands((long)s.Captives)} of them captives of war)" : "") +
                  ".\nLevies come from free men; dependents give half as many."
                : "");
        _might.Text = $"Might: land {Military.Might(s, Domain.Land):0.0}, sea {Military.Might(s, Domain.Naval):0.0}" +
            (Military.Fatigue(s) > 0.05 ? $"   (weary from campaigning: {Military.Fatigue(s):P0})" : "") +
            $"\nUnder arms: {ThemeAncient.GroupThousands(Military.Soldiers(s))} men";
        _realmHint.Text = "Your armies, their units and where they stand are in the Armies panel.";
        _treasuryButton.Text = s.Debt > 0.5 ? $"{T(s.Treasury)}  (debt {T(s.Debt)})" : T(s.Treasury);
    }

    /// <summary>Administration, corruption and the rival (decision "Playable 25").</summary>
    void RefreshRival()
    {
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        int capacity = _map.AdminCapacity(_map.PlayerRealmId);
        double corruption = _map.Corruption(_map.PlayerRealmId);
        var far = _map.Provinces!.Provinces.Values.Where(p => p.RealmId == _map.PlayerRealmId && _map.DistanceFromCapital(p) > MapView.FarKm).ToList();
        _remedies.AddChild(ThemeAncient.Label(
            $"Your court manages {capacity} provinces well; you rule {c.Provinces}. Corruption eats {corruption:P0} of your taxes." +
            (far.Count > 0 ? $" {far.Count} province{(far.Count == 1 ? " is" : "s are")} far from the capital (over {MapView.FarKm:0} km): slower to settle, more restless unless roads join them." : ""),
            "SubtleLabel", 14));
        ((Label)_remedies.GetChild(_remedies.GetChildCount() - 1)).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ((Label)_remedies.GetChild(_remedies.GetChildCount() - 1)).CustomMinimumSize = new Vector2(560, 0);
        if (s.RivalStrength >= 1)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _remedies.AddChild(row);
            var l = ThemeAncient.Label($"A rival's following: {s.RivalStrength:0} of 100 (at 100 he rises and the most restless provinces go with him).", fontSize: 15);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(420, 0);
            if (s.RivalStrength >= 60)
                l.Modulate = new Color(1f, 0.7f, 0.6f);
            row.AddChild(l);
            double cost = _map.RivalAppeaseCost();
            var b = new Button { Text = $"Win them over ({cost:0})", Disabled = s.Treasury < cost, FocusMode = FocusModeEnum.None,
                TooltipText = "Gifts, offices and marriages: -30 to the rival's following." };
            b.AddThemeFontSizeOverride("font_size", 14);
            b.Pressed += () =>
            {
                string? text = _map.AppeaseRival();
                if (text != null)
                    LogEvents(new List<Facsimilia.Dynasties.ChronicleEvent> { new(Facsimilia.Dynasties.ChronicleKind.Economy, _map.PlayerRealmId, text) });
                RefreshRealmPanel();
                RefreshTreasury();
            };
            row.AddChild(b);
        }
    }

    /// <summary>Historical remedies, offered only when the treasury is in trouble (decision "Playable 6").</summary>
    void RefreshRemedies()
    {
        foreach (Node child in _remedies.GetChildren())
            child.QueueFree();
        var s = _map.PlayerState;
        var active = Remedies.All.Where(r => Remedies.Active(s, r.Id)).Select(r => $"{r.Name.ToLowerInvariant()} ({s.RemedyYears[r.Id]} more years)").ToList();
        if (active.Count > 0)
            _remedies.AddChild(ThemeAncient.Label("Still felt: " + string.Join(", ", active), "SubtleLabel", 14));
        if (!Remedies.InTrouble(s))
            return;
        _remedies.AddChild(ThemeAncient.Label("The treasury is in trouble. Remedies:", "HeaderLabel", 16));
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        _remedies.AddChild(row);
        foreach (var (remedy, problem, silver) in _map.RemediesNow())
        {
            var b = new Button
            {
                Text = $"{remedy.Name} (+{silver:0})",
                Disabled = problem != null,
                FocusMode = FocusModeEnum.None,
                TooltipText = remedy.Description + (problem != null ? "\n" + problem : ""),
            };
            b.AddThemeFontSizeOverride("font_size", 14);
            string id = remedy.Id;
            b.Pressed += () =>
            {
                string? text = _map.TakeRemedy(id);
                if (text != null)
                    LogEvents(new List<Facsimilia.Dynasties.ChronicleEvent> { new(Facsimilia.Dynasties.ChronicleKind.Economy, _map.PlayerRealmId, text) });
                RefreshRealmPanel();
                RefreshTreasury();
            };
            row.AddChild(b);
        }
    }

    /// <summary>Turn length choice, beside the turn button.</summary>
    Control BuildTurnLength()
    {
        _turnLength = new OptionButton
        {
            TooltipText = "How many years one turn covers. A long turn stops early if something needs you.",
            FocusMode = FocusModeEnum.None,
        };
        foreach (int y in GameState.TurnLengths)
            _turnLength.AddItem(y == 1 ? "1 year" : $"{y} years", y);
        _turnLength.ItemSelected += index =>
        {
            _map.Game.YearsPerTurn = _turnLength.GetItemId((int)index);
            RefreshAdvanceText();
        };
        return _turnLength;
    }

    void RefreshAdvanceText()
    {
        int years = _map.Game.YearsPerTurn;
        _advanceButton.Text = years == 1 ? "Advance Year" : $"Advance {years} Years";
        for (int i = 0; i < _turnLength.ItemCount; i++)
            if (_turnLength.GetItemId(i) == years)
                _turnLength.Select(i);
    }
}
