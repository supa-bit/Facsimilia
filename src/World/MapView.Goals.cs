using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>Optional goals and the score on the map (data/goals.json).</summary>
public partial class MapView
{
    GoalsCatalog? _goalsCatalog;
    Dictionary<(int Region, int Realm), double>? _regionPeople;
    double[] _regionTotal = Array.Empty<double>();
    int _regionPeopleYear = int.MinValue;

    public GoalsCatalog? GoalsCatalog()
    {
        if (_goalsCatalog == null && Godot.FileAccess.FileExists(Facsimilia.Game.GoalsCatalog.Path))
            _goalsCatalog = Facsimilia.Game.GoalsCatalog.Parse(Godot.FileAccess.GetFileAsString(Facsimilia.Game.GoalsCatalog.Path));
        return _goalsCatalog;
    }

    public IReadOnlyList<GoalDef> GoalsOf(int realmId) =>
        GoalsCatalog()?.For(Game.Realm(realmId).CivKey) ?? (IReadOnlyList<GoalDef>)Array.Empty<GoalDef>();

    /// <summary>Share of a region's people living in a realm's land, 0-1.</summary>
    public double RegionShare(int realmId, string regionName)
    {
        if (Population == null)
            return 0;
        if (_regionPeople == null || _regionPeopleYear != DemoYear || TerritoryChanged)
        {
            _regionPeople = new Dictionary<(int, int), double>();
            _regionTotal = new double[Population.RegionCount + 1];
            foreach (int i in Population.LandNodes)
            {
                int r = Population.RegionOf(i);
                double p = Population.Pop[i];
                _regionTotal[r] += p;
                var key = (r, Population.NodeOwner[i]);
                _regionPeople[key] = _regionPeople.GetValueOrDefault(key) + p;
            }
            _regionPeopleYear = DemoYear;
        }
        var region = Population.Regions.FirstOrDefault(x => x.Name == regionName);
        if (region == null || _regionTotal[region.Id] <= 0)
            return 0;
        return _regionPeople.GetValueOrDefault((region.Id, realmId)) / _regionTotal[region.Id];
    }

    /// <summary>How far a realm is toward a goal (1 = reached), and a line saying where it stands.</summary>
    public (double Progress, string Detail) GoalProgress(int realmId, GoalDef g)
    {
        var s = Game.Realm(realmId);
        var c = CensusOf(realmId);
        switch (g.Kind)
        {
            case "region":
                var shares = g.Regions.Select(r => (Region: r, Share: RegionShare(realmId, r))).ToList();
                double least = shares.Count > 0 ? shares.Min(x => x.Share) : 0;
                return (Math.Min(1, least / g.Share),
                    string.Join(", ", shares.Select(x => $"{x.Region} {x.Share:P0}")) + $" of the people (need {g.Share:P0})");
            case "people_growth":
                double want = Math.Max(1, s.StartPeople) * g.Factor;
                return (Math.Min(1, c.People / want), $"{c.People:N0} of {want:N0} people");
            case "provinces":
                return (Math.Min(1, c.Provinces / g.Target), $"{c.Provinces} of {g.Target:0} provinces");
            case "treasury":
                return (s.Debt > 0.5 ? Math.Min(0.99, s.Treasury / g.Target) : Math.Min(1, s.Treasury / g.Target),
                    $"{s.Treasury:N0} of {g.Target:N0} talents" + (s.Debt > 0.5 ? $", but {s.Debt:N0} in debt" : ""));
            case "survive":
                bool alive = c.Nodes > 0;
                if (!alive)
                    return (0, "your realm has fallen");
                double t = (double)(DemoYear - StartYear) / (g.Year - StartYear);
                return (DemoYear >= g.Year ? 1 : Math.Clamp(t, 0, 0.99), DemoYear >= g.Year ? "it stands" : $"{g.Year - DemoYear} years to go");
            default:
                return (0, "");
        }
    }

    public double Score(int realmId)
    {
        var cat = GoalsCatalog();
        var s = Game.Realm(realmId);
        var c = CensusOf(realmId);
        double score = cat?.BaseScore(c.People, c.Provinces, s.Treasury, s.Debt) ?? 0;
        foreach (var g in GoalsOf(realmId))
            if (s.GoalsDone.ContainsKey(g.Id))
                score += g.Points;
        return score;
    }

    /// <summary>The yearly check: goals the player has reached are marked, with a line in the chronicle.</summary>
    internal List<ChronicleEvent> GoalsYear()
    {
        var events = new List<ChronicleEvent>();
        var s = Game.Realm(PlayerRealmId);
        foreach (var g in GoalsOf(PlayerRealmId))
        {
            if (s.GoalsDone.ContainsKey(g.Id) || GoalProgress(PlayerRealmId, g).Progress < 1)
                continue;
            s.GoalsDone[g.Id] = DemoYear;
            events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"Goal reached: {g.Text} (+{g.Points} points)."));
        }
        return events;
    }
}
