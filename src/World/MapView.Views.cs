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
        WealthView = "wealth", PeopleView = "people", DiplomacyView = "diplomacy",
        RoadsView = "roads", TerrainView = "terrain", SupplyView = "supply", GoodsView = "goods";

    public static readonly (string Key, string Label)[] ProvinceViews =
    {
        (CultureView, "Culture"), (ReligionView, "Religion"), (LoyaltyView, "Loyalty"),
        (WealthView, "Wealth"), (PeopleView, "People"), (DiplomacyView, "Diplomacy"),
        (RoadsView, "Roads"), (TerrainView, "Terrain: going"), (SupplyView, "Supply"), (GoodsView, "Trade goods"),
    };

    public static bool IsProvinceView(string view) => ProvinceViews.Any(v => v.Key == view);

    /// <summary>The key for the view shown: colours and their meaning (for the legend).</summary>
    public List<(Color Color, string Label)> ViewKey { get; private set; } = new();
    /// <summary>For ramp views (loyalty, wealth, people): the low and high ends.</summary>
    public (Color Low, Color High, string LowText, string HighText)? ViewRamp { get; private set; }

    Func<int, Color>? _ownerColorOverride;

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
        // The views your answer "More map views" added: roads, the going, supply and trade goods, each province's own.
        var (bestRoad, going, chiefGood) = ProvinceGroundFacts(view);
        IndexKeys(view switch
        {
            CultureView => Provinces.Provinces.Keys.Select(id => ProvinceStateOf(id).Culture).Concat(cultures.Regions.Values.Select(r => r.Item1)),
            ReligionView => Provinces.Provinces.Keys.Select(id => ProvinceStateOf(id).Religion).Concat(cultures.Regions.Values.Select(r => r.Item2)),
            GoodsView => chiefGood.Values,
            _ => Enumerable.Empty<string>(),
        });
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
                RoadsView => bestRoad.TryGetValue(p.Id, out double pace) ? WithAlpha(Ramp(0.35 + (1 - pace) * 1.3)) : new Color(0.35f, 0.32f, 0.3f, 0.75f),
                TerrainView => WithAlpha(Ramp(1 - (going.GetValueOrDefault(p.Id, 1) - 1) / 2.5)),
                SupplyView => WithAlpha(Ramp(Math.Log10(1 + ProvincePopulation(p.Id) / ForagePeoplePer1000 * 1000) / 5.5)),
                GoodsView => chiefGood.TryGetValue(p.Id, out var good) ? Named(good, good) : new Color(0, 0, 0, 0),
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
        if (view == RoadsView)
            ViewKey = new() { (Ramp(1), "A paved road"), (Ramp(0.6), "A road or track"), (new Color(0.35f, 0.32f, 0.3f), "No road") };
        ViewRamp = view switch
        {
            LoyaltyView => (RampLow, RampHigh, "Restless, newly won", "Loyal"),
            WealthView => (RampLow, RampHigh, "Poor", "Rich (output per person)"),
            PeopleView => (RampLow, RampHigh, "Empty", "Crowded (people per km²)"),
            TerrainView => (RampLow, RampHigh, "Hard going (mountains, marsh, forest)", "Easy going"),
            SupplyView => (RampLow, RampHigh, "Bare: an army starves", "Rich: feeds a great army"),
            _ => null,
        };
        EmitSignal(SignalName.MapViewChanged, CurrentView);   // the legend reads the key built here
    }

    static Color WithAlpha(Color c) => new(c.R, c.G, c.B, 0.8f);

    /// <summary>
    /// For the roads, going and goods views: each province's best finished
    /// road (its pace: lower is faster), its average going (1 open ground,
    /// higher harder), and the good its land yields most.
    /// </summary>
    (Dictionary<int, double> Road, Dictionary<int, double> Going, Dictionary<int, string> Good) ProvinceGroundFacts(string? view)
    {
        var road = new Dictionary<int, double>();
        var going = new Dictionary<int, double>();
        var good = new Dictionary<int, string>();
        if (Population == null || view is not (RoadsView or TerrainView or GoodsView))
            return (road, going, good);
        var np = NodeProvinces();
        if (view == RoadsView)
            foreach (int i in Population.LandNodes)
            {
                double pace = RoadPaceNear(i);
                if (np[i] != 0 && pace < 1 && (!road.TryGetValue(np[i], out double best) || pace < best))
                    road[np[i]] = pace;
            }
        if (view == TerrainView)
        {
            EnsureMoveCost();
            var count = new Dictionary<int, int>();
            foreach (int i in Population.LandNodes)
                if (np[i] != 0 && _moveCost![i] > 0)
                {
                    going[np[i]] = going.GetValueOrDefault(np[i]) + _moveCost[i];
                    count[np[i]] = count.GetValueOrDefault(np[i]) + 1;
                }
            foreach (var (id, n) in count)
                going[id] /= n;
        }
        if (view == GoodsView && Land != null && GoodsCatalog() is { } cat)
        {
            var fields = cat.Goods.Where(g => g.Source == GoodSource.Land && g.Field.StartsWith("res_") && Land.Has(g.Field)).ToList();
            var sums = new Dictionary<(int, string), double>();
            foreach (int i in Population.LandNodes)
                if (np[i] != 0)
                    foreach (var g in fields)
                    {
                        double v = Land.Value(g.Field, i);
                        if (v >= Census.ResourceThreshold)
                            sums[(np[i], g.Name)] = sums.GetValueOrDefault((np[i], g.Name)) + v;
                    }
            foreach (var group in sums.GroupBy(kv => kv.Key.Item1))
                good[group.Key] = group.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item2).First().Key.Item2;
        }
        return (road, going, good);
    }

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
