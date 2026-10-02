using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The goods panel: what your realm makes in a year, by family (field
/// crops, metals, cloth ...), each opening to its goods: made, used up
/// making other goods, needed by your people, and what is spare or short.
/// </summary>
public partial class Hud
{
    PanelContainer _goodsPanel = null!;
    VBoxContainer _goodsRows = null!;
    Label _goodsSummary = null!;
    readonly HashSet<string> _openFamilies = new();

    void BuildGoodsButton(HBoxContainer row)
    {
        var button = new Button
        {
            Flat = true,
            Text = "Goods",
            Icon = ThemeAncient.Icon("goods"),
            FocusMode = FocusModeEnum.None,
            TooltipText = "What your realm makes and needs (G)",
            ExpandIcon = false,
        };
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += () => ToggleGoodsPanel();
        row.AddChild(button);
    }

    void BuildGoodsPanel()
    {
        _goodsPanel = new PanelContainer
        {
            ThemeTypeVariation = "GlassPanel",
            CustomMinimumSize = new Vector2(640, 0),
            MouseFilter = MouseFilterEnum.Stop,
            Visible = false,
        };
        _goodsPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        _goodsPanel.OffsetLeft = _goodsPanel.OffsetRight = 20;
        _goodsPanel.OffsetTop = _goodsPanel.OffsetBottom = 80;
        AddChild(_goodsPanel);
        Float(_goodsPanel, "Goods");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        _goodsPanel.AddChild(box);
        var header = new HBoxContainer();
        box.AddChild(header);
        var title = ThemeAncient.Label("Goods", "HeaderLabel", 22);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(IconButton(null, "×", "Close", () => ToggleGoodsPanel(false)));
        _goodsSummary = ThemeAncient.Label("", fontSize: 17);
        _goodsSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _goodsSummary.CustomMinimumSize = new Vector2(600, 0);
        box.AddChild(_goodsSummary);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620, 560), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _goodsRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _goodsRows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(_goodsRows);
    }

    void ToggleGoodsPanel(bool? show = null)
    {
        _goodsPanel.Visible = show ?? !_goodsPanel.Visible;
        if (_goodsPanel.Visible)
        {
            ToggleRealmPanel(false);
            _diplomacyPanel.Visible = false;
            _goalsPanel.Visible = false;
            _guidePanel.Visible = false;
            _armiesPanel.Visible = false;
            _researchPanel.Visible = false;
            RefreshGoodsPanel();
        }
        RefreshMapView();
    }

    static string Loads(double x) => Math.Abs(x) >= 100 ? ThemeAncient.GroupThousands((long)Math.Round(x)) : x.ToString("0.#");

    string RoutesLine(RealmGoods goods)
    {
        var mine = _map.RouteHolders().Where(r => r.Owners.Contains(_map.PlayerRealmId)).Select(r => r.Route.Name).ToList();
        return (mine.Count > 0 ? $"Your trade routes: {string.Join(", ", mine)}." : "You hold no place on a trade route: you trade only through your neighbours.") +
            (goods.TransitIncome > 0 ? $" Goods passing through your lands pay {_map.Money(goods.TransitIncome / 6000)} a year in tolls." : "") +
            " (See them on the map: Trade routes.)\n";
    }

    void RefreshGoodsPanel()
    {
        if (!_goodsPanel.Visible)
            return;
        foreach (Node child in _goodsRows.GetChildren())
            child.QueueFree();
        var cat = _map.GoodsCatalog();
        var goods = _map.CensusOf(_map.PlayerRealmId).Goods;
        if (cat == null || goods == null)
        {
            _goodsSummary.Text = "No goods yet: the land data isn't loaded.";
            return;
        }
        var shortages = cat.Goods.Where(g => g.Need > 0 && g.Source != GoodSource.Beyond && goods.Surplus(g.Index) < -0.05 * goods.Needed[g.Index])
            .Select(g => g.Name.ToLowerInvariant()).ToList();
        _goodsSummary.Text =
            $"Your people make goods worth {_map.Money(goods.Value / 6000)} a year" +
            (goods.Services > 6000 ? $" (of it, townspeople's trade and services {T(goods.Services / 6000)})" : "") +
            ". Taxes are a share of this.\n" +
            $"Trade: you sell goods worth {_map.Money(goods.ExportIncome / 6000)} and buy {_map.Money(goods.ImportCost / 6000)} a year " +
            $"with {goods.Partners.Count} realms (your tolls and customs take {Trade.CustomsRate:P0}).\n" +
            RoutesLine(goods) +
            $"Your people have {goods.Satisfaction:P0} of what they need" +
            (goods.Satisfaction < 0.8 ? " - want breeds unrest." : ".") +
            (shortages.Count > 0 ? $"\nStill short of: {string.Join(", ", shortages)}." : "") +
            "\nAmounts are in loads: what one person makes in a year of full work.";
        AddMarketRows();
        foreach (var (famId, famName) in cat.Families)
        {
            double value = goods.FamilyValue(cat, famId) / 6000;
            bool open = _openFamilies.Contains(famId);
            var button = new Button
            {
                Text = $"{(open ? "▾" : "▸")} {famName}: {_map.Money(value)} a year",
                Alignment = HorizontalAlignment.Left,
                Flat = true,
                FocusMode = FocusModeEnum.None,
            };
            button.AddThemeFontSizeOverride("font_size", 19);
            string id = famId;
            button.Pressed += () =>
            {
                if (!_openFamilies.Remove(id))
                    _openFamilies.Add(id);
                RefreshGoodsPanel();
            };
            _goodsRows.AddChild(button);
            if (!open)
                continue;
            foreach (var g in cat.Goods.Where(g => g.Family == famId))
            {
                double made = goods.Produced[g.Index], used = goods.Used[g.Index], need = goods.Needed[g.Index];
                double bought = goods.Imported[g.Index], sold = goods.Exported[g.Index];
                double spare = goods.Surplus(g.Index);
                double pf = g.Index < _map.PriceFactors.Length ? _map.PriceFactors[g.Index] : 1;
                string text = $"      {g.Name}: " + (g.Source == GoodSource.Beyond ? $"arrived {Loads(made)}" : $"made {Loads(made)}") +
                    (used > 0.05 ? $", used {Loads(used)}" : "") + (need > 0.05 ? $", people need {Loads(need)}" : "") +
                    (bought > 0.05 ? $", bought {Loads(bought)}" : "") + (sold > 0.05 ? $", sold {Loads(sold)}" : "") +
                    (spare < -0.05 ? $", SHORT {Loads(-spare)}" : $", spare {Loads(spare)}") +
                    (Math.Abs(pf - 1) > 0.1 ? $"  (price {pf:0.0}x)" : "");
                var label = ThemeAncient.Label(text, fontSize: 16);
                label.TooltipText = g.Source switch
                {
                    GoodSource.Made => "Made from " + string.Join(g.AnyInput ? " or " : " and ", g.Inputs.Select(x => cat.Goods[x.Good].Name.ToLowerInvariant())) +
                        $"; worth {g.Price:0} drachmae a load",
                    GoodSource.ByProduct => $"Comes with {cat.Goods[g.By].Name.ToLowerInvariant()}; worth {g.Price:0} drachmae a load",
                    GoodSource.Beyond => $"From beyond the map, arriving in {string.Join(", ", g.EntryRegions)}; worth {g.Price:0} drachmae a load",
                    _ => $"From the land (map view: {g.Field}); worth {g.Price:0} drachmae a load",
                };
                label.MouseFilter = MouseFilterEnum.Pass;
                if (spare < -0.05)
                    label.Modulate = new Color(1f, 0.7f, 0.65f);
                _goodsRows.AddChild(label);
            }
        }
    }

    /// <summary>
    /// Your markets (the Ledger topic "Trade"): the great markets serving your
    /// land with their grain price, your merchants, and the price limit edict.
    /// </summary>
    void AddMarketRows()
    {
        var s = _map.PlayerState;
        var mc = _map.Markets();
        var markets = MarketCatalog.Instance.Markets;
        _goodsRows.AddChild(ThemeAncient.Label("Markets", "HeaderLabel", 19));
        var mine = mc.Share.TryGetValue(_map.PlayerRealmId, out var sh) ? sh : new double[mc.Count];
        string Grain(int i) => mc.Prices.GetLength(0) > i && mc.LimitedGoods.Count > 0
            ? $"grain {mc.LimitedGoods.Average(k => mc.Prices[i, k]):0.00}x" : "";
        foreach (int i in Enumerable.Range(0, mc.Count).Where(i => mine[i] > 0.01).OrderByDescending(i => mine[i]))
            _goodsRows.AddChild(ThemeAncient.Label($"      {markets[i].Name} serves {mine[i]:P0} of your people  ·  {Grain(i)}" +
                (mc.Volume.Length > i ? $"  ·  trade {_map.Money(mc.Volume[i] / 6000)} a year" : ""), fontSize: 15));
        // Merchants.
        var merch = new HBoxContainer();
        merch.AddThemeConstantOverride("separation", 6);
        _goodsRows.AddChild(merch);
        merch.AddChild(ThemeAncient.Label($"Merchants ({s.Merchants.Count} of {_map.MaxMerchants(_map.PlayerRealmId)}):", fontSize: 15));
        foreach (var id in s.Merchants.ToList())
        {
            var mk = markets.FirstOrDefault(x => x.Id == id);
            var b = new Button { Text = $"{mk?.Name ?? id} ×", FocusMode = FocusModeEnum.None, TooltipText = "Call this merchant home." };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => { _map.SendMerchant(id, false); RefreshGoodsPanel(); };
            merch.AddChild(b);
        }
        if (s.Merchants.Count < _map.MaxMerchants(_map.PlayerRealmId))
        {
            var send = new MenuButton { Text = "Send a merchant ▾", FocusMode = FocusModeEnum.None, Flat = false,
                TooltipText = $"A merchant at a market takes {Facsimilia.World.MapView.MerchantCut:P0} of its trade as customs for you, shared with the other realms' merchants there." };
            var popup = send.GetPopup();
            var open = Enumerable.Range(0, mc.Count).Where(i => markets[i].OpenIn(_map.DemoYear)).OrderByDescending(i => mc.Volume.Length > i ? mc.Volume[i] : 0).ToList();
            foreach (int i in open)
                popup.AddItem($"{markets[i].Name}: trade {_map.Money((mc.Volume.Length > i ? mc.Volume[i] : 0) / 6000)} a year", i);
            popup.IdPressed += i => { _map.SendMerchant(markets[(int)i].Id); RefreshGoodsPanel(); };
            merch.AddChild(send);
        }
        // The price limit edict.
        var limit = new CheckBox { Text = "Limit grain prices by edict", ButtonPressed = s.PriceLimit, FocusMode = FocusModeEnum.None,
            TooltipText = "Grain is sold at no more than its usual price in your land, like Diocletian's Edict of 301. Sellers hold back: you go short, " +
                "and black markets and hunger add unrest." + (s.LastShortage > 0 ? $"\nLast year you went without grain worth {_map.Money(s.LastShortage / 6000)}." : "") };
        limit.Toggled += on => { s.PriceLimit = on; RefreshGoodsPanel(); };
        _goodsRows.AddChild(limit);
        double piracy = _map.PiracyPressure(_map.PlayerRealmId);
        if (piracy > 0)
            _goodsRows.AddChild(ThemeAncient.Label($"Pirates prey on {piracy:P0} of your sea trade (customs -{Facsimilia.World.MapView.PiracyCustoms * piracy:P0}). " +
                "Take their haven, or keep warships there for six months, to sweep them from the sea.", fontSize: 15));
    }
}
