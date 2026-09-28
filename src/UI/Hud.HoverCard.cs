using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Hover cards (decision "Next 45"): rest the mouse on a province and a
/// small card shows it: owner, people, culture and faith, loyalty and
/// unrest, garrison and its chief towns.
/// </summary>
public partial class Hud
{
    const double HoverDelay = 0.35;   // seconds the mouse must rest before the card shows
    PanelContainer? _hoverCard;
    Label _hoverText = null!;
    int _hoverProvince;
    double _hoverRest;
    Vector2 _hoverMouse;

    void UpdateHoverCard(double delta)
    {
        if (_map.Provinces == null)
            return;
        if (_map.Pending != null)
        {
            if (_hoverCard != null)
                _hoverCard.Visible = false;
            return;   // the battle map is open
        }
        if (_hoverCard == null)
        {
            _hoverCard = new PanelContainer { ThemeTypeVariation = "GlassPanel", MouseFilter = MouseFilterEnum.Ignore, Visible = false, ZIndex = 50 };
            _hoverText = ThemeAncient.Label("", fontSize: 15);
            _hoverText.MouseFilter = MouseFilterEnum.Ignore;
            _hoverCard.AddChild(_hoverText);
            AddChild(_hoverCard);
        }
        var mouse = GetViewport().GetMousePosition();
        bool overUi = GetViewport().GuiGetHoveredControl() is { } c && c != this && c.MouseFilter != MouseFilterEnum.Ignore;
        if (overUi)
        {
            _hoverCard.Visible = false;
            _hoverProvince = 0;
            return;
        }
        int id = _map.ProvinceIdAtWorld(_map.GetGlobalMousePosition());
        if (id != _hoverProvince || mouse.DistanceTo(_hoverMouse) > 6)
        {
            _hoverProvince = id;
            _hoverMouse = mouse;
            _hoverRest = 0;
            _hoverCard.Visible = false;
            return;
        }
        _hoverRest += delta;
        if (id == 0 || _hoverRest < HoverDelay || _hoverCard.Visible)
            return;
        _hoverText.Text = HoverText(id);
        _hoverCard.Visible = true;
        _hoverCard.ResetSize();
        var size = _hoverCard.GetCombinedMinimumSize();
        var screen = GetViewportRect().Size;
        _hoverCard.Position = new Vector2(
            Mathf.Min(mouse.X + 18, screen.X - size.X - 8),
            Mathf.Min(mouse.Y + 18, screen.Y - size.Y - 8));
    }

    string HoverText(int provinceId)
    {
        var p = _map.Provinces!.Provinces[provinceId];
        var st = _map.ProvinceStateOf(provinceId);
        var towns = _map.TownsIn(provinceId, 3);
        string owner = p.RealmId > 0 && p.RealmId != MapView.SeaOwnerId ? _map.RealmName(p.RealmId) : "No one";
        return $"{p.Name}  ·  {owner}\n" +
            $"People: {ThemeAncient.GroupThousands((long)_map.ProvincePopulation(provinceId))}" +
            (st.Culture != "" ? $"  ·  {_map.CultureName(st.Culture)}" : "") +
            (st.Religion != "" ? $", {_map.ReligionName(st.Religion)}" : "") + "\n" +
            $"Loyalty {st.Integration:P0}  ·  Unrest {st.Unrest:P0}  ·  Garrison {_map.GarrisonOf(p):0.#}" +
            (towns.Count > 0 ? $"\nTowns: {string.Join(", ", towns)}" : "");
    }
}
