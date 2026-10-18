using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Facsimilia.World;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// Hover cards (decision "Next 45"): rest the mouse on a province and a
/// small card shows it: owner, people, culture and faith, loyalty and
/// unrest, garrison and its chief towns. Underlined words open their own
/// card when the mouse rests on them (your answer "Nested hover cards", as
/// in Crusader Kings 3): the realm, its people, its gods, and on from there.
/// A card stays while the mouse is on it.
/// </summary>
public partial class Hud
{
    const double HoverDelay = 0.35;   // seconds the mouse must rest before the card shows
    /// <summary>The open cards: the province's first, then one for each word followed.</summary>
    readonly List<PanelContainer> _cards = new();
    int _hoverProvince;
    double _hoverRest;
    Vector2 _hoverMouse;

    void UpdateHoverCard(double delta)
    {
        if (_map.Provinces == null)
            return;
        if (_map.Pending != null)
        {
            CloseCards(0);
            return;   // the battle map is open
        }
        var mouse = GetViewport().GetMousePosition();
        if (_cards.Count > 0 && _cards.Any(c => c.GetGlobalRect().Grow(12).HasPoint(mouse)))
            return;   // reading a card, or moving between them
        bool overUi = GetViewport().GuiGetHoveredControl() is { } c && c != this && c.MouseFilter != MouseFilterEnum.Ignore;
        if (overUi)
        {
            CloseCards(0);
            _hoverProvince = 0;
            return;
        }
        int id = _map.ProvinceIdAtWorld(_map.GetGlobalMousePosition());
        if (id != _hoverProvince || mouse.DistanceTo(_hoverMouse) > 6)
        {
            _hoverProvince = id;
            _hoverMouse = mouse;
            _hoverRest = 0;
            CloseCards(0);
            return;
        }
        _hoverRest += delta;
        if (id == 0 || _hoverRest < HoverDelay || _cards.Count > 0)
            return;
        OpenCard(0, HoverText(id), mouse + new Vector2(18, 18));
    }

    /// <summary>Opens the card at this depth (closing any deeper), near a point on the screen.</summary>
    void OpenCard(int depth, string bbcode, Vector2 at)
    {
        CloseCards(depth);
        var card = new PanelContainer { ThemeTypeVariation = "GlassPanel", MouseFilter = MouseFilterEnum.Stop, ZIndex = 50 + depth };
        // Long cards (a realm's aims and their reasons) wrap at a reading width.
        bool wrap = bbcode.Length > 360;
        var text = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            CustomMinimumSize = new Vector2(wrap ? 460 : 0, 0),
            MouseFilter = MouseFilterEnum.Pass, Text = bbcode,
        };
        text.AddThemeFontSizeOverride("normal_font_size", 15);
        text.AddThemeColorOverride("default_color", ThemeAncient.Text);
        text.MetaHoverStarted += meta =>
        {
            string body = CardFor(meta.AsString());
            if (body != "")
                OpenCard(depth + 1, body, GetViewport().GetMousePosition() + new Vector2(14, 14));
        };
        text.MetaClicked += meta =>
        {
            // "More on click" (your answer "Why they did that, in detail"): every aim, not just the first three.
            string m = meta.AsString();
            if (m.StartsWith("aims:") && int.TryParse(m[5..], out int realm))
                OpenCard(depth + 1, AimsCard(realm, all: true), GetViewport().GetMousePosition() + new Vector2(14, 14));
        };
        card.AddChild(text);
        AddChild(card);
        _cards.Add(card);
        card.ResetSize();
        var size = card.GetCombinedMinimumSize();
        var screen = GetViewportRect().Size;
        card.Position = new Vector2(Mathf.Min(at.X, screen.X - size.X - 8), Mathf.Min(at.Y, screen.Y - size.Y - 8));
    }

    void CloseCards(int fromDepth)
    {
        while (_cards.Count > fromDepth)
        {
            _cards[^1].QueueFree();
            _cards.RemoveAt(_cards.Count - 1);
        }
    }

    /// <summary>A card's title, in gold rather than bold (easier on the eyes).</summary>
    static string Title(string text) => $"[color=#{ThemeAncient.GoldBright.ToHtml(false)}]{text}[/color]";

    /// <summary>A word that opens its own card: [url=realm:5]Carthage[/url].</summary>
    static string Link(string kind, string id, string text) => $"[url={kind}:{id}][u]{text}[/u][/url]";

    string HoverText(int provinceId)
    {
        var p = _map.Provinces!.Provinces[provinceId];
        var st = _map.ProvinceStateOf(provinceId);
        var towns = _map.TownsIn(provinceId, 3);
        bool owned = p.RealmId > 0 && p.RealmId != MapView.SeaOwnerId;
        string owner = owned ? Link("realm", p.RealmId.ToString(), _map.RealmName(p.RealmId)) : "No one";
        return $"{p.Name}  ·  {owner}\n" +
            $"People: {ThemeAncient.GroupThousands((long)_map.ProvincePopulation(provinceId))}" +
            (st.Culture != "" ? $"  ·  {Link("culture", st.Culture, _map.CultureName(st.Culture))}" : "") +
            (st.Religion != "" ? $", {Link("religion", st.Religion, _map.ReligionName(st.Religion))}" : "") + "\n" +
            $"Loyalty {st.Integration:P0}  ·  Unrest {st.Unrest:P0}  ·  Garrison {_map.GarrisonOf(p):0.#}" +
            (towns.Count > 0 ? $"\nTowns: {string.Join(", ", towns)}" : "");
    }

    /// <summary>The card a linked word opens.</summary>
    string CardFor(string meta)
    {
        int colon = meta.IndexOf(':');
        if (colon < 0)
            return "";
        string kind = meta[..colon], id = meta[(colon + 1)..];
        return kind switch
        {
            "realm" when int.TryParse(id, out int realm) => RealmCard(realm),
            "culture" => PeopleCard(id),
            "religion" => FaithCard(id),
            _ => "",
        };
    }

    string RealmCard(int realmId)
    {
        var (culture, religion) = _map.RealmPeople(realmId);
        var lines = new List<string> { Title(_map.RealmName(realmId)) };
        if (_map.Registry.Realms.TryGetValue(realmId, out var realm) && _map.Registry.Characters.TryGetValue(realm.RulerId, out var ruler))
            lines.Add($"Ruled by {ruler.Name}, aged {_map.DemoYear - ruler.BirthYear}");
        var insight = _map.InsightInto(realmId);
        if (insight != MapView.Insight.Unknown)
        {
            double people = _map.CensusOf(realmId).People;
            lines.Add($"People: {(insight == MapView.Insight.Rough ? "about " + ThemeAncient.GroupThousands((long)MapView.Rough(people)) : ThemeAncient.GroupThousands((long)people))}");
        }
        else
            lines.Add("Your realm knows little of them.");
        if (culture != "")
            lines.Add($"Mostly {Link("culture", culture, _map.CultureName(culture))}" + (religion != "" ? $", worshipping {Link("religion", religion, _map.ReligionName(religion))}" : ""));
        if (realmId != _map.PlayerRealmId)
        {
            var (opinion, _) = _map.Opinion(realmId, _map.PlayerRealmId);
            string status = _map.Game.Wars.AtWar(realmId, _map.PlayerRealmId) ? "At war with you" : _map.Game.Treaties.Allied(realmId, _map.PlayerRealmId) ? "Your ally" : "At peace with you";
            lines.Add($"{status}  ·  their opinion of you {opinion:+0;-0;0}");
            lines.Add(AimsCard(realmId, all: false));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Most aims shown on a realm's card; the rest on click.</summary>
    const int CardAims = 3;

    /// <summary>A realm's aims and why (Other realms: "Why they did that"): the first three, or all of them.</summary>
    string AimsCard(int realmId, bool all)
    {
        var aims = _map.AimsOf(realmId);
        if (aims.Count == 0)
            return "Your realm knows nothing of their aims.";
        var shown = all ? aims : aims.Take(CardAims).ToList();
        string dim = ThemeAncient.TextDim.ToHtml(false);
        var lines = new List<string> { all ? Title($"{_map.RealmName(realmId)}: all their aims") : "Their aims:" };
        lines.AddRange(shown.Select(a => $"•  {a.Text}\n    [color=#{dim}]{a.Reason}[/color]"));
        if (!all && aims.Count > CardAims)
            lines.Add($"[url=aims:{realmId}][u]Click for all {aims.Count} of their aims[/u][/url]");
        return string.Join("\n", lines);
    }

    string PeopleCard(string culture)
    {
        var provinces = _map.Game.Provinces.Where(p => p.Value.Culture == culture).Select(p => p.Key).ToList();
        int yours = provinces.Count(id => _map.Provinces!.Provinces.TryGetValue(id, out var p) && p.RealmId == _map.PlayerRealmId);
        var realms = provinces.Select(id => _map.Provinces!.Provinces.TryGetValue(id, out var p) ? p.RealmId : 0)
            .Where(r => r > 0 && r != MapView.SeaOwnerId).Distinct().Select(r => Link("realm", r.ToString(), _map.RealmName(r))).Take(6).ToList();
        return Title(_map.CultureName(culture)) + "\n" +
            $"In {provinces.Count} province{(provinces.Count == 1 ? "" : "s")}, {yours} of them yours" +
            (realms.Count > 0 ? $"\nLiving under {string.Join(", ", realms)}" : "");
    }

    string FaithCard(string religion)
    {
        var provinces = _map.Game.Provinces.Where(p => p.Value.Religion == religion).Select(p => p.Key).ToList();
        int yours = provinces.Count(id => _map.Provinces!.Provinces.TryGetValue(id, out var p) && p.RealmId == _map.PlayerRealmId);
        return Title(_map.ReligionName(religion)) + "\n" +
            $"Worshipped in {provinces.Count} province{(provinces.Count == 1 ? "" : "s")}, {yours} of them yours" +
            (_map.RealmPeople(_map.PlayerRealmId).Religion == religion ? "\nYour own gods" : "");
    }
}
