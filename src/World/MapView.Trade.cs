using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>Trade on the map: who can trade with whom, and where goods from beyond the map arrive.</summary>
public partial class MapView
{
    /// <summary>Realms that share a border on the population grid (index pairs, smaller first).</summary>
    HashSet<(int, int)> LandNeighbours()
    {
        var pairs = new HashSet<(int, int)>();
        if (Population == null)
            return pairs;
        int w = Population.Width, h = Population.Height;
        int[] owner = Population.NodeOwner;
        foreach (int i in Population.LandNodes)
        {
            int a = owner[i];
            if (a <= 0)
                continue;
            int x = i % w, y = i / w;
            if (x + 1 < w)
                Add(a, owner[i + 1]);
            if (y + 1 < h)
                Add(a, owner[i + w]);
        }
        return pairs;

        void Add(int a, int b)
        {
            if (b > 0 && b != a)
                pairs.Add(a < b ? (a, b) : (b, a));
        }
    }

    double[] RunTrade(GoodsCatalog cat, Dictionary<int, RealmGoods> goods, Dictionary<int, RealmCensus> census)
    {
        var neighbours = LandNeighbours();
        var coastal = census.Where(c => c.Value.Coastal).Select(c => c.Key).ToHashSet();
        IEnumerable<int> Partners(int realm)
        {
            foreach (int other in goods.Keys)
            {
                if (other == realm || Game.Wars.AtWar(realm, other))
                    continue;
                if (neighbours.Contains(realm < other ? (realm, other) : (other, realm))
                    || (coastal.Contains(realm) && coastal.Contains(other)))
                    yield return other;
            }
        }
        // Where goods from beyond the map arrive: each realm's share of the people in the entry regions.
        var entryShare = new Dictionary<string, Dictionary<int, double>>();
        if (Population != null)
            foreach (var g in cat.Goods.Where(g => g.Source == GoodSource.Beyond))
            {
                var regions = Population.Regions.Where(r => g.EntryRegions.Contains(r.Name)).Select(r => r.Id).ToHashSet();
                var byRealm = new Dictionary<int, double>();
                double total = 0;
                foreach (int i in Population.LandNodes)
                    if (regions.Contains(Population.RegionOf(i)))
                    {
                        total += Population.Pop[i];
                        if (Population.NodeOwner[i] > 0)
                            byRealm[Population.NodeOwner[i]] = byRealm.GetValueOrDefault(Population.NodeOwner[i]) + Population.Pop[i];
                    }
                entryShare[g.Id] = byRealm.ToDictionary(x => x.Key, x => total > 0 ? x.Value / total : 0);
            }
        return Trade.Run(cat, goods, Partners, (g, realm) =>
            entryShare.TryGetValue(g.Id, out var s) ? s.GetValueOrDefault(realm) : 0);
    }
}
