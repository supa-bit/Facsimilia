using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// War's toll on growth (the growth drivers' Security factor): battles and
/// sieges ravage the region they are fought in, the ravage fades over the
/// years, and long peace helps a little. See Game/Security.cs.
/// </summary>
public partial class MapView
{
    /// <summary>War strikes the land at a population node: its region is ravaged by this much more.</summary>
    internal void RavageAtNode(int node, double blow)
    {
        if (Population == null || node < 0 || blow <= 0)
            return;
        int r = Population.RegionOf(node);
        if (r <= 0)
            return;
        Game.Ravaged[r] = Security.Add(Game.Ravaged.GetValueOrDefault(r), blow);
    }

    /// <summary>How ravaged a region is now (0: untouched, 1: laid waste).</summary>
    public double RavagedOf(int region) => Game.Ravaged.GetValueOrDefault(region);

    /// <summary>Years the realm has been at peace (0 while at war).</summary>
    public int YearsAtPeace(int realm)
    {
        var s = Game.Realm(realm);
        return s.LastWarYear == 0 ? 0 : Math.Max(0, DemoYear - s.LastWarYear);
    }

    /// <summary>
    /// Once a year: note who is at war, score each region's Security from
    /// its ravage or from the peace of the realm holding most of its people,
    /// then let the ravage fade a year.
    /// </summary>
    void SecurityYear()
    {
        if (Population == null)
            return;
        foreach (var (id, s) in Game.Realms)
            if (s.LastWarYear == 0 || Game.Wars.Of(id).Any())
                s.LastWarYear = DemoYear;   // the game opens in a world just out of the Successors' wars

        // The realm holding most of each region's people.
        var held = new Dictionary<(int Region, int Realm), double>();
        foreach (int i in Population.LandNodes)
        {
            int owner = Population.NodeOwner[i];
            if (owner > 0 && Population.Pop[i] > 0)
            {
                var key = (Population.RegionOf(i), owner);
                held[key] = held.GetValueOrDefault(key) + Population.Pop[i];
            }
        }
        var mainRealm = held.GroupBy(kv => kv.Key.Region)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(kv => kv.Value).First().Key.Realm);

        foreach (var info in Population.Regions)
        {
            int peace = mainRealm.TryGetValue(info.Id, out int realm) ? YearsAtPeace(realm) : 0;
            Population.SetFactor(info.Id, GrowthFactor.Security, Security.Score(RavagedOf(info.Id), peace));
        }

        foreach (int r in Game.Ravaged.Keys.ToList())
        {
            double left = Game.Ravaged[r] * Security.Fade;
            if (left < Security.Recovered)
                Game.Ravaged.Remove(r);
            else
                Game.Ravaged[r] = left;
        }
    }
}
