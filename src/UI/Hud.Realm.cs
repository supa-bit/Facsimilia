using System;
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
    readonly List<(Label Name, Label Count, Button Raise, Button Disband)> _unitRows = new();
    OptionButton _turnLength = null!;
    Button _advanceButton = null!;

    static readonly string[] TaxNames = { "Low", "Normal", "Heavy", "Crushing" };
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
        var title = ThemeAncient.Label("Treasury and Army", "HeaderLabel", 22);
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

        _manpower = ThemeAncient.Label("", fontSize: 16);
        box.AddChild(_manpower);
        box.AddChild(ThemeAncient.Label("Army", "HeaderLabel", 18));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        box.AddChild(grid);
        for (int t = 0; t < UnitTypes.Count; t++)
        {
            int type = t;
            var name = ThemeAncient.Label("", fontSize: 16);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.TooltipText = UnitTypes.All[t].Description;
            name.MouseFilter = MouseFilterEnum.Pass;
            var count = ThemeAncient.Label("", fontSize: 16, align: HorizontalAlignment.Right);
            count.CustomMinimumSize = new Vector2(36, 0);
            var raise = new Button { Text = "Raise", FocusMode = FocusModeEnum.None };
            raise.Pressed += () =>
            {
                Military.Recruit(_map.PlayerState, _map.CensusOf(_map.PlayerRealmId), _map.CultureOf(_map.PlayerRealmId),
                    type, _map.PlayerState.ElephantSource);
                RefreshRealmPanel();
            };
            var disband = new Button { Text = "Disband", FocusMode = FocusModeEnum.None, TooltipText = "Send one unit home; its men return to the manpower pool." };
            disband.Pressed += () =>
            {
                Military.Disband(_map.PlayerState, type);
                RefreshRealmPanel();
            };
            grid.AddChild(name);
            grid.AddChild(count);
            grid.AddChild(raise);
            grid.AddChild(disband);
            _unitRows.Add((name, count, raise, disband));
        }
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
        var culture = _map.CultureOf(_map.PlayerRealmId);
        var (tax, tribute) = Economy.Revenue(c, s.Tax);
        double upkeep = Economy.Upkeep(s), admin = Economy.AdminPerProvince * c.Provinces, interest = s.Debt * Economy.InterestRate;
        double net = tax + tribute + (c.Goods != null ? Trade.Customs(c.Goods) : 0) - upkeep - admin - interest;
        _accounts.Text =
            $"Treasury: {T(s.Treasury)} talents" + (s.Debt > 0.5 ? $"   Debt: {T(s.Debt)} (10% interest)" : "") + "\n" +
            $"Each year at this rate:\n" +
            $"   Taxes +{T(tax)}   Tribute from unorganized land +{T(tribute)}" +
            (c.Goods != null ? $"   Tolls and customs +{T(Trade.Customs(c.Goods))}" : "") + "\n" +
            $"   Administration −{T(admin)} ({c.Provinces} provinces; your court manages {Loyalty.AdminCapacity} well" +
            (c.Provinces > Loyalty.AdminCapacity ? ", so every province is restless" : "") + ")\n" +
            $"     Army −{T(upkeep)}" +
            (interest > 0.5 ? $"   Interest −{T(interest)}" : "") + "\n" +
            $"   Balance {(net >= 0 ? "+" : "−")}{T(Math.Abs(net))} talents a year";
        for (int i = 0; i < 4; i++)
            _taxButtons[i].SetPressedNoSignal((int)s.Tax == i);
        _manpower.Text = $"Men who can be called up: {ThemeAncient.GroupThousands((long)s.Manpower)}" +
            $" (refills toward {ThemeAncient.GroupThousands((long)Economy.SustainableManpower(c, s.ManpowerMultiplier))})." +
            "\nBeyond them, mercenaries can be hired at twice the price.";
        for (int t = 0; t < UnitTypes.Count; t++)
        {
            var (name, count, raise, disband) = _unitRows[t];
            var u = UnitTypes.All[t];
            name.Text = UnitTypes.LocalName(t, culture);
            count.Text = s.Units[t].ToString();
            var (problem, cost) = Military.CanRecruit(s, c, culture, t, s.ElephantSource);
            bool never = !UnitTypes.CanRaise(t, culture) || (problem != null && cost == 0);
            raise.Disabled = problem != null;
            raise.TooltipText = never ? problem : $"Raise {u.Men:N0} {(u.Domain == Domain.Naval ? "men and 10 ships" : "men")} " +
                $"for {cost:0} talents; upkeep {u.Upkeep:0} a year." + (problem != null ? "\n" + problem : "") +
                (u.Needs != null && !c.Resources.Contains(u.Needs) && cost > 0 ? "\nImported at a premium: you hold no source." : "");
            disband.Disabled = s.Units[t] == 0;
        }
        _might.Text = $"Might: land {Military.Might(s, Domain.Land):0.0}, sea {Military.Might(s, Domain.Naval):0.0}" +
            (s.Fatigue > 0.05 ? $"   (weary from campaigning: {s.Fatigue:P0})" : "") +
            $"\nUnder arms: {ThemeAncient.GroupThousands(Military.Soldiers(s))} men";
        _realmHint.Text = "Units are counted for the whole realm. Painting land in Conquest mode sends them to war.";
        _treasuryButton.Text = s.Debt > 0.5 ? $"{T(s.Treasury)}  (debt {T(s.Debt)})" : T(s.Treasury);
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
