using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Cultures, religions and loyalty on the map (data/cultures.json): who
/// lives in each province, how settled it is under its ruler, and revolts.
/// </summary>
public partial class MapView
{
    public const string CulturesPath = "res://data/cultures.json";

    sealed class CultureData
    {
        public Dictionary<string, string> CultureNames = new(), ReligionNames = new(), RealmCulture = new();
        public Dictionary<string, (string Culture, string Religion)> Regions = new(), Provinces = new();
        public List<(string, string)> Kin = new();
    }

    CultureData? _cultures;

    CultureData Cultures()
    {
        if (_cultures != null)
            return _cultures;
        var d = new CultureData();
        if (Godot.FileAccess.FileExists(CulturesPath))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(CulturesPath));
            var root = doc.RootElement;
            foreach (var p in root.GetProperty("cultures").EnumerateObject())
                d.CultureNames[p.Name] = p.Value.GetString()!;
            foreach (var p in root.GetProperty("religions").EnumerateObject())
                d.ReligionNames[p.Name] = p.Value.GetString()!;
            foreach (var spec in RealCivs)   // each realm's people, from data/realms_bc300.json
                d.RealmCulture[spec.Key] = spec.CultureKey;
            foreach (var p in root.GetProperty("regions").EnumerateObject())
                d.Regions[p.Name] = (p.Value[0].GetString()!, p.Value[1].GetString()!);
            foreach (var p in root.GetProperty("provinces").EnumerateObject())
                d.Provinces[p.Name] = (p.Value[0].GetString()!, p.Value[1].GetString()!);
            foreach (var k in root.GetProperty("kin").EnumerateArray())
                d.Kin.Add((k[0].GetString()!, k[1].GetString()!));
        }
        return _cultures = d;
    }

    public string CultureName(string key) => Cultures().CultureNames.GetValueOrDefault(key, key);
    public string ReligionName(string key) => Cultures().ReligionNames.GetValueOrDefault(key, key);

    /// <summary>A realm's own culture and religion: from data/cultures.json, or else its capital province's people.</summary>
    public (string Culture, string Religion) RealmPeople(int realmId)
    {
        var c = Cultures();
        string culture = "";
        if (Game.Realms.TryGetValue(realmId, out var s) && c.RealmCulture.TryGetValue(s.CivKey, out var rc))
            culture = rc;
        var own = Provinces?.Provinces.Values.Where(p => p.RealmId == realmId).OrderByDescending(p => p.AreaKm2).ToList();
        string religion = "";
        if (own != null)
        {
            // The ruler's religion: that of their own people's largest province, else their largest.
            var match = own.FirstOrDefault(p => ProvinceStateOf(p.Id).Culture == culture) ?? own.FirstOrDefault();
            if (match != null)
            {
                var ps = ProvinceStateOf(match.Id);
                religion = ps.Religion;
                if (culture == "")
                    culture = ps.Culture;
            }
        }
        return (culture, religion);
    }

    /// <summary>The province's people as history had them in 300 BC.</summary>
    (string Culture, string Religion) HistoricalPeople(Province p)
    {
        var c = Cultures();
        if (c.Provinces.TryGetValue(p.Name, out var own))
            return own;
        if (Population != null)
        {
            int nx = (int)(p.LabelCell.X * Population.Width / GridWidth), ny = (int)(p.LabelCell.Y * Population.Height / GridHeight);
            int node = Math.Clamp(ny * Population.Width + nx, 0, Population.Pop.Length - 1);
            int region = Population.RegionOf(node);
            var info = Population.Regions.FirstOrDefault(r => r.Id == region);
            if (info != null && c.Regions.TryGetValue(info.Name, out var r))
                return r;
        }
        return ("", "");
    }

    public ProvinceState ProvinceStateOf(int provinceId)
    {
        if (!Game.Provinces.TryGetValue(provinceId, out var s))
        {
            s = new ProvinceState();
            if (Provinces != null && Provinces.Provinces.TryGetValue(provinceId, out var p))
            {
                (s.Culture, s.Religion) = HistoricalPeople(p);
                s.Owner = p.RealmId;
            }
            Game.Provinces[provinceId] = s;
        }
        return s;
    }

    /// <summary>Start of a game: every province's people; those held in 300 BC are mostly settled.</summary>
    internal void StartLoyalty()
    {
        Game.Provinces.Clear();
        if (Provinces == null)
            return;
        foreach (var p in Provinces.Provinces.Values)
            ProvinceStateOf(p.Id);
        foreach (var p in Provinces.Provinces.Values)
        {
            var s = Game.Provinces[p.Id];
            var ruler = RealmPeople(p.RealmId);
            s.Integration = Loyalty.Relation(s.Culture, ruler.Culture, Cultures().Kin) == Loyalty.Kinship.Same ? 1 : 0.8;
        }
    }

    /// <summary>Tax and levy share of a province (for the census).</summary>
    double ProvinceYield(int provinceId) =>
        Game.Provinces.TryGetValue(provinceId, out var s) ? Loyalty.Yield(s.Integration) : 1;

    /// <summary>
    /// The yearly loyalty step: conquered provinces start over, every
    /// province integrates and gathers unrest, and the player's restless
    /// provinces may rise and break away.
    /// </summary>
    internal List<ChronicleEvent> LoyaltyYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Provinces == null)
            return events;
        var counts = Provinces.Provinces.Values.GroupBy(p => p.RealmId).ToDictionary(g => g.Key, g => g.Count());
        var people = new Dictionary<int, (string Culture, string Religion)>();
        var revolts = new List<Province>();
        foreach (var p in Provinces.Provinces.Values)
        {
            var s = ProvinceStateOf(p.Id);
            if (!people.TryGetValue(p.RealmId, out var ruler))
                people[p.RealmId] = ruler = RealmPeople(p.RealmId);
            var rel = Loyalty.Relation(s.Culture, ruler.Culture, Cultures().Kin);
            if (s.Owner != p.RealmId)
            {
                s.Integration = rel == Loyalty.Kinship.Same ? Loyalty.ConqueredSameCulture : 0;
                s.Owner = p.RealmId;
            }
            if (p.RealmId <= 0)
                continue;
            var state = Game.Realm(p.RealmId);
            var bc = BuildingCatalog.Instance;
            var distance = DistanceEffects(p);
            Loyalty.Tick(s, rel, s.Religion == ruler.Religion || s.Religion == "", Game.Wars.Of(p.RealmId).Any(),
                s.Tax ?? state.Tax, counts[p.RealmId], CensusOf(p.RealmId).Goods?.Satisfaction ?? 1,
                EnslavedShareOfRegion(RegionOfProvince(p)), bc.Effect(s, "integration") + TechCatalog.Instance.Effect(state, "integration") + distance.Integration,
                bc.Effect(s, "unrest") + Remedies.Unrest(state) + TechCatalog.Instance.Effect(state, "unrest") + distance.Unrest
                    + Corruption(p.RealmId) * CorruptionUnrest,
                AdminCapacity(p.RealmId));
            if (p.RealmId == PlayerRealmId && rng.NextDouble() < Loyalty.ChanceOfRevolt(s.Unrest))
                revolts.Add(p);
        }
        foreach (var p in revolts)
        {
            string ruler = RealmName(p.RealmId);
            Game.RecordLoss(p.RealmId, p.Id, DemoYear);
            Provinces.SetRealm(p.Id, 0, Grid);   // it throws off its ruler and stands alone
            MarkChanged(p.Id);
            var s = Game.Provinces[p.Id];
            s.Owner = 0;
            s.Unrest = 0;
            TerritoryChanged = true;
            events.Add(new ChronicleEvent(ChronicleKind.Revolt, PlayerRealmId,
                $"{p.Name} rises against {ruler} and breaks away."));
        }
        if (revolts.Count > 0)
        {
            _census = null;
            if (Population != null)
                SyncPopulationOwnership();
        }
        return events;
    }
}
