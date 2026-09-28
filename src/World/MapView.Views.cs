using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Province map views (decision "Next 44"): culture, religion, loyalty,
/// wealth and people colour each province; diplomacy colours every realm by
/// how it stands with you. Land outside provinces (the tribal peoples)
/// shows its region's culture and religion.
/// </summary>
public partial class MapView
{
    public const string CultureView = "culture", ReligionView = "religion", LoyaltyView = "loyalty",
        WealthView = "wealth", PeopleView = "people", DiplomacyView = "diplomacy";

    public static readonly (string Key, string Label)[] ProvinceViews =
    {
        (CultureView, "Culture"), (ReligionView, "Religion"), (LoyaltyView, "Loyalty"),
        (WealthView, "Wealth"), (PeopleView, "People"), (DiplomacyView, "Diplomacy"),
    };

    public static bool IsProvinceView(string view) => ProvinceViews.Any(v => v.Key == view);

    /// <summary>The key for the view shown: colours and their meaning (for the legend).</summary>
    public List<(Color Color, string Label)> ViewKey { get; private set; } = new();
    /// <summary>For ramp views (loyalty, wealth, people): the low and high ends.</summary>
    public (Color Low, Color High, string LowText, string HighText)? ViewRamp { get; private set; }

    Func<int, Color>? _ownerColorOverride;

    /// <summary>A steady, distinct colour for a name (a culture or religion), the same every game.</summary>
    static Color KeyColor(string key)
    {
        uint h = 2166136261;
        foreach (char ch in key)
            h = (h ^ ch) * 16777619;
        float hue = (h % 360) / 360f;
        float sat = 0.32f + (h / 360 % 22) / 100f;
        float val = 0.66f + (h / 10800 % 20) / 100f;
        return Color.FromHsv(hue, sat, val);
    }

    static readonly Color RampLow = new(0.72f, 0.22f, 0.18f), RampMid = new(0.85f, 0.75f, 0.3f), RampHigh = new(0.25f, 0.6f, 0.3f);

    static Color Ramp(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? RampLow.Lerp(RampMid, (float)(t * 2)) : RampMid.Lerp(RampHigh, (float)((t - 0.5) * 2));
    }

    /// <summary>Turns a province view on (or every one off with null).</summary>
    async void ShowProvinceView(string? view)
    {
        if (MapSprite?.Material is not ShaderMaterial material || Provinces == null)
            return;
        ViewKey = new();
        ViewRamp = null;
        bool diplomacy = view == DiplomacyView;
        if (diplomacy != (_ownerColorOverride != null))
        {
            _ownerColorOverride = diplomacy ? DiplomacyColor : null;
            if (diplomacy)
                DiplomacyKey();
            await BuildFullMapImage(loadLand: false);
        }
        if (view == null || diplomacy)
        {
            material.SetShaderParameter("province_view_strength", 0f);
            if (diplomacy)
                EmitSignal(SignalName.MapViewChanged, CurrentView);
            return;
        }
        int maxId = Provinces.Provinces.Keys.DefaultIfEmpty(0).Max();
        int rows = maxId / 256 + 1;
        var palette = Image.CreateEmpty(256, rows, false, Image.Format.Rgba8);
        var regionPalette = Image.CreateEmpty(256, 1, false, Image.Format.Rgba8);
        var seen = new Dictionary<string, Color>();
        Color Named(string key, string label)
        {
            if (key == "")
                return new Color(0, 0, 0, 0);
            var c = KeyColor(key);
            seen[label] = c;
            return new Color(c.R, c.G, c.B, 0.85f);
        }
        var cultures = Cultures();
        // Output per person of each realm (its people's goods, in the realm's coin).
        var perHead = Game.Realms.Keys.ToDictionary(id => id, id =>
        {
            var c = CensusOf(id);
            return c.Goods != null && c.People > 0 ? c.Goods.Value / c.People : 0;
        });
        double maxHead = Math.Max(1, perHead.Values.DefaultIfEmpty(1).Max());
        foreach (var p in Provinces.Provinces.Values)
        {
            var st = ProvinceStateOf(p.Id);
            Color c = view switch
            {
                CultureView => Named(st.Culture, CultureName(st.Culture)),
                ReligionView => Named(st.Religion, ReligionName(st.Religion)),
                LoyaltyView => WithAlpha(Ramp(st.Integration * (1 - st.Unrest))),
                WealthView => WithAlpha(Ramp(perHead.GetValueOrDefault(p.RealmId) / maxHead)),
                PeopleView => WithAlpha(Ramp(Math.Log10(1 + ProvincePopulation(p.Id) / Math.Max(1, p.AreaKm2)) / 2.5)),
                _ => new Color(0, 0, 0, 0),
            };
            palette.SetPixel(p.Id % 256, p.Id / 256, c);
        }
        // Land outside provinces: its region's people, for culture and religion.
        if ((view == CultureView || view == ReligionView) && Population != null)
            foreach (var r in Population.Regions)
                if (r.Id is > 0 and < 256 && cultures.Regions.TryGetValue(r.Name, out var people))
                {
                    string key = view == CultureView ? people.Item1 : people.Item2;
                    regionPalette.SetPixel(r.Id, 0, Named(key, view == CultureView ? CultureName(key) : ReligionName(key)));
                }
        material.SetShaderParameter("province_palette", ImageTexture.CreateFromImage(palette));
        material.SetShaderParameter("region_view_palette", ImageTexture.CreateFromImage(regionPalette));
        material.SetShaderParameter("province_view_strength", 1f);
        ViewKey = seen.OrderBy(kv => kv.Key).Select(kv => (kv.Value, kv.Key)).ToList();
        ViewRamp = view switch
        {
            LoyaltyView => (RampLow, RampHigh, "Restless, newly won", "Loyal"),
            WealthView => (RampLow, RampHigh, "Poor", "Rich (output per person)"),
            PeopleView => (RampLow, RampHigh, "Empty", "Crowded (people per km²)"),
            _ => null,
        };
        EmitSignal(SignalName.MapViewChanged, CurrentView);   // the legend reads the key built here
    }

    static Color WithAlpha(Color c) => new(c.R, c.G, c.B, 0.8f);

    static readonly Color DipYou = new(0.95f, 0.8f, 0.3f), DipAlly = new(0.3f, 0.7f, 0.35f), DipVassal = new(0.55f, 0.8f, 0.5f),
        DipWar = new(0.8f, 0.2f, 0.18f), DipTruce = new(0.55f, 0.62f, 0.75f), DipRival = new(0.9f, 0.5f, 0.2f), DipOther = new(0.55f, 0.53f, 0.5f);

    /// <summary>How a realm stands with the player, as a colour for the diplomacy view.</summary>
    Color DiplomacyColor(int realm)
    {
        int me = PlayerRealmId;
        if (realm == me) return DipYou;
        if (Game.Wars.AtWar(me, realm)) return DipWar;
        if (Game.Treaties.OverlordOf(realm) == me || Game.Treaties.OverlordOf(me) == realm) return DipVassal;
        if (Game.Treaties.Allied(me, realm)) return DipAlly;
        if (Game.Wars.TruceUntil(me, realm, DemoYear) != null) return DipTruce;
        if (Rivals(me, realm)) return DipRival;
        return DipOther;
    }

    void DiplomacyKey() => ViewKey = new()
    {
        (DipYou, "You"), (DipAlly, "Allies"), (DipVassal, "Your vassals, or your overlord"), (DipWar, "At war with you"),
        (DipTruce, "Truce"), (DipRival, "Rivals"), (DipOther, "Others"),
    };

    /// <summary>A realm's map colour.</summary>
    public Color ColorForRealm(int realmId) => Registry.Realms.TryGetValue(realmId, out var r) ? r.Color : WildColor;

    /// <summary>Every realm's land in km², from the population grid (tribal lands included).</summary>
    public Dictionary<int, double> RealmLandKm2()
    {
        var land = new Dictionary<int, double>();
        if (Population == null)
            return land;
        double kmY = (LatMax - LatMin) / Population.Height * 111.2;
        foreach (int i in Population.LandNodes)
        {
            int o = Population.NodeOwner[i];
            if (o <= 0)
                continue;
            double lat = LatMax - (i / Population.Width + 0.5) / Population.Height * (LatMax - LatMin);
            land[o] = land.GetValueOrDefault(o) + kmY * (LonMax - LonMin) / Population.Width * 111.2 * Math.Cos(lat * Math.PI / 180);
        }
        return land;
    }
}
