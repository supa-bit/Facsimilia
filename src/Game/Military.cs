using System;
using System.Collections.Generic;
using Facsimilia.Dynasties;

namespace Facsimilia.Game;

/// <summary>
/// Raising, disbanding and measuring armies (MECHANICS.md, "Might").
/// Strength is a realm-wide pool of units (decision "Playable 9"); Might is
/// its fighting value per domain, worn down by fatigue from campaigning.
/// </summary>
public static class Military
{
    /// <summary>Why a unit can't be raised now, or null if it can; and what it would cost.</summary>
    public static (string? Problem, double Cost) CanRecruit(RealmState r, RealmCensus c, Culture culture, int type,
        bool elephantSource = false)
    {
        var u = UnitTypes.All[type];
        if (!UnitTypes.CanRaise(type, culture))
            return ("Only steppe peoples raise horse archers.", 0);
        if (type == UnitTypes.Warships && !c.Coastal)
            return ("You have no coast to build ships on.", 0);
        if (type == UnitTypes.Elephants && !c.Resources.Contains("res_elephants") && !elephantSource)
            return ("You hold no elephant country to capture them in.", 0);
        double cost = u.Raise;
        if (u.Needs != null && !c.Resources.Contains(u.Needs) && !(type == UnitTypes.Elephants && elephantSource))
            cost *= UnitTypes.ImportPremium;
        if (r.Manpower < u.Men)
            return ($"Not enough men: {u.Men:N0} needed, {r.Manpower:N0} available.", cost);
        if (r.Treasury < cost)
            return ($"Not enough silver: {cost:0} talents needed.", cost);
        return (null, cost);
    }

    public static bool Recruit(RealmState r, RealmCensus c, Culture culture, int type, bool elephantSource = false)
    {
        var (problem, cost) = CanRecruit(r, c, culture, type, elephantSource);
        if (problem != null)
            return false;
        r.Treasury -= cost;
        r.Manpower -= UnitTypes.All[type].Men;
        r.Units[type]++;
        return true;
    }

    /// <summary>Sends a unit home: its men return to the pool, the silver spent on it doesn't.</summary>
    public static bool Disband(RealmState r, int type)
    {
        if (r.Units[type] <= 0)
            return false;
        r.Units[type]--;
        r.Manpower += UnitTypes.All[type].Men;
        return true;
    }

    /// <summary>Fighting value in a domain, before fatigue.</summary>
    public static double RawMight(RealmState r, Domain domain)
    {
        double m = 0;
        for (int t = 0; t < UnitTypes.Count; t++)
            if (UnitTypes.All[t].Domain == domain)
                m += r.Units[t] * UnitTypes.All[t].Might;
        return m;
    }

    /// <summary>Fighting value now: tired armies fight worse.</summary>
    public static double Might(RealmState r, Domain domain) => RawMight(r, domain) * (1 - 0.5 * r.Fatigue);

    public static int Soldiers(RealmState r)
    {
        int s = 0;
        for (int t = 0; t < UnitTypes.Count; t++)
            s += r.Units[t] * UnitTypes.All[t].Men;
        return s;
    }

    /// <summary>
    /// Losses in battle: a share of every unit type (rounded so that small
    /// armies can lose units too). Returns the men lost.
    /// </summary>
    public static double TakeLosses(RealmState r, double share, Random rng)
    {
        double men = 0;
        for (int t = 0; t < UnitTypes.Count; t++)
        {
            double expected = r.Units[t] * share;
            int lost = (int)Math.Floor(expected);
            if (rng.NextDouble() < expected - lost)
                lost++;
            lost = Math.Min(lost, r.Units[t]);
            r.Units[t] -= lost;
            men += lost * UnitTypes.All[t].Men;
        }
        return men;
    }
}
