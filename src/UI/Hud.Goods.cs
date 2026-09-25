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
            RefreshGoodsPanel();
        }
        RefreshMapView();
    }

    static string Loads(double x) => Math.Abs(x) >= 100 ? ThemeAncient.GroupThousands((long)Math.Round(x)) : x.ToString("0.#");

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
        var shortages = cat.Goods.Where(g => g.Need > 0 && goods.Surplus(g.Index) < -0.05 * goods.Needed[g.Index])
            .Select(g => g.Name.ToLowerInvariant()).ToList();
        _goodsSummary.Text =
            $"Your people make goods worth {T(goods.Value / 6000)} talents a year " +
            $"(of it, townspeople's trade and services {T(goods.Services / 6000)}). Taxes are a share of this.\n" +
            "Amounts are in loads: what one person makes in a year of full work." +
            (shortages.Count > 0 ? $"\nShort of: {string.Join(", ", shortages)} (trade will bring these later)." : "");
        foreach (var (famId, famName) in cat.Families)
        {
            double value = goods.FamilyValue(cat, famId) / 6000;
            bool open = _openFamilies.Contains(famId);
            var button = new Button
            {
                Text = $"{(open ? "▾" : "▸")} {famName}: {T(value)} talents a year",
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
                double spare = made - used - need;
                string text = $"      {g.Name}: made {Loads(made)}" +
                    (used > 0.05 ? $", used {Loads(used)}" : "") + (need > 0.05 ? $", people need {Loads(need)}" : "") +
                    (spare < -0.05 ? $", SHORT {Loads(-spare)}" : $", spare {Loads(spare)}");
                var label = ThemeAncient.Label(text, fontSize: 16);
                label.TooltipText = g.Source switch
                {
                    GoodSource.Made => "Made from " + string.Join(g.AnyInput ? " or " : " and ", g.Inputs.Select(x => cat.Goods[x.Good].Name.ToLowerInvariant())) +
                        $"; worth {g.Price:0} drachmae a load",
                    GoodSource.ByProduct => $"Comes with {cat.Goods[g.By].Name.ToLowerInvariant()}; worth {g.Price:0} drachmae a load",
                    _ => $"From the land (map view: {g.Field}); worth {g.Price:0} drachmae a load",
                };
                label.MouseFilter = MouseFilterEnum.Pass;
                if (spare < -0.05)
                    label.Modulate = new Color(1f, 0.7f, 0.65f);
                _goodsRows.AddChild(label);
            }
        }
    }
}
