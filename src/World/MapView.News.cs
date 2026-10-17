using System;
using System.Collections.Generic;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// How news reaches you (your answers "News that travels": news of far
/// events arrives late, by road and sea time; and "Fog in the chronicle":
/// only news that would reach you). News of another realm sets out from its
/// capital and travels with merchants and travellers; declarations of war
/// and peace come at once, brought by heralds and envoys.
/// </summary>
public partial class MapView
{
    /// <summary>How far news travels in a month: about 30 km a day by merchants, ships and travellers.</summary>
    public const double NewsKmPerMonth = 900;
    /// <summary>Roads and coasts wind: the way news takes is this much longer than the straight line.</summary>
    const double NewsRouteFactor = 1.3;
    /// <summary>With full diplomacy fog, news of a realm you have no dealings with and have never seen comes only from this near.</summary>
    public const double NewsReachKm = 2500;

    int MonthNow => DemoYear * 12 + Game.MonthsMarched;

    /// <summary>Km as the crow flies between two map nodes.</summary>
    double KmBetween(int a, int b)
    {
        EnsureMoveCost();
        int w = Population!.Width;
        double dx = (a % w - b % w) * _stepKmX[(a / w + b / w) / 2], dy = (a / w - b / w) * _stepKmY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Months the news of a realm takes to reach you (0: at once). Along a
    /// road it goes as much faster as marching does (your answer "How fast
    /// news travels": roads and posts), and your posts, couriers and later
    /// telegraph speed all of it (the techs' "news_speed").
    /// </summary>
    public int NewsMonths(int realmId)
    {
        if (Game.DiplomacyFog == FogLevel.Off || realmId == PlayerRealmId || Population == null)
            return 0;
        int from = CapitalNode(realmId), to = CapitalNode(PlayerRealmId);
        if (from < 0 || to < 0)
            return 0;
        double km = KmBetween(from, to);
        int steps = Math.Max(1, (int)Math.Ceiling(km / NewsStepKm));
        int w = Population.Width;
        double way = 0;
        for (int k = 0; k < steps; k++)
        {
            double t = (k + 0.5) / steps;
            int x = (int)Math.Round(from % w + (to % w - from % w) * t), y = (int)Math.Round(from / w + (to / w - from / w) * t);
            way += km / steps * NewsRouteFactor * RoadPaceNear(y * w + x);
        }
        double speed = NewsKmPerMonth * (1 + Tech.Effect(PlayerState, "news_speed"));
        return (int)Math.Round(way / speed);
    }

    /// <summary>How finely the way news takes is looked at for roads.</summary>
    const double NewsStepKm = 50;

    Dictionary<int, double>? _roadPace;
    int _roadPaceYear = int.MinValue, _roadPaceCount = -1;

    /// <summary>The best pace of a finished road on or next to this node (1: no road).</summary>
    double RoadPaceNear(int node)
    {
        if (_roadPace == null || _roadPaceYear != DemoYear || _roadPaceCount != Game.Roads.Count)
        {
            _roadPace = new Dictionary<int, double>();
            _roadPaceYear = DemoYear;
            _roadPaceCount = Game.Roads.Count;
            foreach (var road in Game.Roads)
            {
                var kind = RoadCatalog.Instance[road.Kind];
                if (kind == null || kind.Wall || kind.Water || road.Left > 0 || road.Broken)
                    continue;
                foreach (int n in road.Nodes)
                    if (!_roadPace.TryGetValue(n, out double p) || kind.Pace < p)
                        _roadPace[n] = kind.Pace;
            }
        }
        if (_roadPace.Count == 0)
            return 1;
        int w = Population!.Width;
        double best = 1;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (_roadPace.TryGetValue(node + dy * w + dx, out double p) && p < best)
                    best = p;
        return best;
    }

    /// <summary>Whether news of a realm reaches you at all.</summary>
    public bool NewsReaches(int realmId)
    {
        if (Game.DiplomacyFog != FogLevel.Full || realmId == PlayerRealmId || InsightInto(realmId) != Insight.Unknown)
            return true;
        int from = CapitalNode(realmId), to = CapitalNode(PlayerRealmId);
        return from < 0 || to < 0 || ExploredNode(from) || KmBetween(from, to) <= NewsReachKm;
    }

    static bool ComesAtOnce(ChronicleEvent e) => e.Kind is ChronicleKind.War or ChronicleKind.Peace;

    /// <summary>
    /// The news you hear now: of these events, those that reach you at once,
    /// and whatever earlier news has just arrived. The rest is sent on the
    /// road (or, if it would never reach you, dropped).
    /// </summary>
    public List<ChronicleEvent> NewsArriving(IReadOnlyList<ChronicleEvent> events)
    {
        int now = MonthNow;
        var heard = new List<ChronicleEvent>();
        foreach (var e in events)
        {
            if (!e.IsNewsFor(PlayerRealmId))
                continue;
            if (e.RealmId <= 0 || ComesAtOnce(e))
            {
                heard.Add(e);
                continue;
            }
            if (!NewsReaches(e.RealmId))
                continue;
            int months = NewsMonths(e.RealmId);
            if (months <= 0)
                heard.Add(e);
            else
                Game.NewsOnTheRoad.Add((now + months, now, (int)e.Kind, e.RealmId, e.Text, e.Cue ?? ""));
        }
        for (int i = 0; i < Game.NewsOnTheRoad.Count; i++)
        {
            var n = Game.NewsOnTheRoad[i];
            if (n.Due > now)
                continue;
            int months = Math.Max(1, n.Due - n.Sent);
            heard.Add(new ChronicleEvent((ChronicleKind)n.Kind, n.Realm,
                $"{n.Text} (word took {months} month{(months == 1 ? "" : "s")} to reach us)", n.Cue == "" ? null : n.Cue));
            Game.NewsOnTheRoad.RemoveAt(i--);
        }
        return heard;
    }
}
