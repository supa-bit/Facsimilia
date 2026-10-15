using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Where units are raised and how they reach their army (War: armies, units
/// and generals): in the nearest province whose people fight that way, then
/// on foot to the army. Every realm raises its troops this way. See Game/Muster.cs.
/// </summary>
public partial class MapView
{
    /// <summary>
    /// Where a realm would raise this unit for this army: the nearest of its
    /// provinces whose people make it (any province for units anyone raises),
    /// else its capital's. Returns the province and the months on the road.
    /// </summary>
    public (int Province, int TravelMonths) MusterPlace(int realmId, UnitDef u, Army army)
    {
        int target = army.Node >= 0 ? army.Node : CapitalNode(realmId);
        if (Provinces == null || Population == null || target < 0)
            return (RecruitOrigin(realmId, army), 0);
        var (tlon, tlat) = NodeLonLat(target);
        int best = 0;
        double bestKm = double.MaxValue;
        foreach (var p in Provinces.Provinces.Values)
        {
            if (p.RealmId != realmId)
                continue;
            var ps = ProvinceStateOf(p.Id);
            if (!u.Anyone && !u.Cultures.Contains(ps.Culture))
                continue;
            int node = ProvinceNode(p.Id);
            if (node < 0)
                continue;
            var (lon, lat) = NodeLonLat(node);
            double km = DisasterCatalog.Km(lon, lat, tlon, tlat);
            if (km < bestKm)
            {
                bestKm = km;
                best = p.Id;
            }
        }
        if (best == 0)
            return (RecruitOrigin(realmId, army), 0);   // none of its provinces' people fight so: raised where the army stands
        return (best, (int)Math.Ceiling(bestKm / Muster.KmPerMonth));
    }

    /// <summary>A population node in a province (its most populous), or -1.</summary>
    int ProvinceNode(int provinceId)
    {
        if (_provinceNodeCache == null || _provinceNodeYear != DemoYear)
        {
            _provinceNodeCache = new Dictionary<int, (int Node, float Pop)>();
            _provinceNodeYear = DemoYear;
            foreach (int i in Population!.LandNodes)
            {
                int p = ProvinceOfNode(i);
                if (p != 0 && (!_provinceNodeCache.TryGetValue(p, out var b) || Population.Pop[i] > b.Pop))
                    _provinceNodeCache[p] = (i, Population.Pop[i]);
            }
        }
        return _provinceNodeCache.TryGetValue(provinceId, out var n) ? n.Node : -1;
    }
    Dictionary<int, (int Node, float Pop)>? _provinceNodeCache;
    int _provinceNodeYear = int.MinValue;

    /// <summary>Orders a unit raised for an army: paid now; it trains in its province, then walks to the army.</summary>
    public bool RaiseUnit(RealmState s, UnitDef u, Army army)
    {
        var (province, travel) = MusterPlace(s.RealmId, u, army);
        return Military.Muster(s, CensusOf(s.RealmId), CulturesOf(s.RealmId), u, army, province, travel, s.ElephantSource);
    }

    /// <summary>
    /// Each month: recruits train, then march; when they arrive they join
    /// their army (or the realm's largest, if it is gone; or a new army in
    /// their province, if the realm has none).
    /// </summary>
    internal void MustersMonth(List<ChronicleEvent> events)
    {
        var cat = UnitCatalog.Instance;
        foreach (var (id, s) in Game.Realms)
        {
            if (s.Musters.Count == 0)
                continue;
            foreach (var m in s.Musters.ToList())
            {
                if (m.TrainLeft > 0)
                    m.TrainLeft--;
                else if (m.TravelLeft > 0)
                    m.TravelLeft--;
                if (m.Months > 0)
                    continue;
                s.Musters.Remove(m);
                var army = s.ArmyById(m.ArmyId) ?? s.Armies.Where(a => a.CompanyId == 0).OrderByDescending(a => a.Count).FirstOrDefault();
                if (army == null)
                {
                    int node = ProvinceNode(m.Province);
                    army = s.NewArmy(NextArmyName(id), node >= 0 ? node : CapitalNode(id));
                    RefreshRoleBoosts();
                }
                army.AddRegiment(m.Type, m.Men, origin: m.Province);
                army.Even();
                if (id == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.War, id,
                        $"New {cat[m.Type].Name.ToLowerInvariant()} from {ProvinceName(m.Province)} join {army.Name}."));
            }
        }
    }
}
