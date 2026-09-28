using System;
using System.Collections.Generic;
using System.Linq;

namespace Facsimilia.Game;

/// <summary>
/// Raising, disbanding and measuring armies (MECHANICS.md, "Might"). A
/// realm's strength is its named armies (decision "Playable 9"); Might is
/// their fighting value per domain, worn down by fatigue from campaigning.
/// </summary>
public static class Military
{
    /// <summary>Price multiplier for a unit whose resource the realm must import.</summary>
    public const double ImportPremium = 1.5;

    static UnitCatalog Cat => UnitCatalog.Instance;

    /// <summary>Too few men left to call up this unit (decision: mercenary companies replace hiring at twice the price).</summary>
    public static bool NeedsMercenaries(RealmState r, UnitDef u) => r.Manpower < u.Men;

    static string ThemeManpower(double men) => ((long)men).ToString("N0");

    /// <summary>Why a unit can't be raised now, or null if it can; and what it would cost.</summary>
    public static (string? Problem, double Cost) CanRecruit(RealmState r, RealmCensus c, IReadOnlyCollection<string> cultures,
        UnitDef u, bool elephantSource = false)
    {
        if (!u.Anyone && !u.Cultures.Any(cultures.Contains))
            return ("None of your peoples fight this way.", 0);
        if (u.Requires != null && !r.Techs.Contains(u.Requires))
            return ($"Needs the technology: {TechCatalog.Instance[u.Requires]?.Name ?? u.Requires}.", 0);
        if (u.Role == UnitRoles.Warships && !c.Coastal)
            return ("You have no coast to build ships on.", 0);
        if (u.Role == UnitRoles.Elephants && !c.Resources.Contains("res_elephants") && !elephantSource)
            return ("You hold no elephant country to capture them in.", 0);
        double cost = u.Raise * (u.Role == UnitRoles.Warships ? 1 - r.ShipDiscount : 1);
        if (u.Needs != null && !c.Resources.Contains(u.Needs) && !(u.Role == UnitRoles.Elephants && elephantSource))
            cost *= ImportPremium;
        if (NeedsMercenaries(r, u))
            return ($"Not enough men to call up ({ThemeManpower(r.Manpower)} left, {u.Men:N0} needed): hire a mercenary company instead.", cost);
        if (r.Treasury < cost)
            return ($"Not enough silver: {CoinCatalog.Instance.Kg(cost):N0} kg needed.", cost);
        return (null, cost);
    }

    public static bool Recruit(RealmState r, RealmCensus c, IReadOnlyCollection<string> cultures, UnitDef u, Army army,
        bool elephantSource = false)
    {
        var (problem, cost) = CanRecruit(r, c, cultures, u, elephantSource);
        if (problem != null)
            return false;
        r.Treasury -= cost;
        r.Manpower -= u.Men;
        army.AddRaw(u.Index);   // raw recruits dilute only their own kind
        return true;
    }

    /// <summary>Sends a unit home: its men return to the pool, the silver spent on it doesn't.</summary>
    public static bool Disband(RealmState r, Army army, UnitDef u)
    {
        if (army.Units[u.Index] <= 0)
            return false;
        army.Units[u.Index]--;
        r.Manpower += u.Men;
        return true;
    }

    /// <summary>Fighting value of an army in a domain, before fatigue.</summary>
    public static double RawMight(Army a, Domain domain)
    {
        double m = 0;
        for (int i = 0; i < a.Units.Length; i++)
            if (a.Units[i] > 0 && Cat[i].Domain == domain)
                m += a.Units[i] * Cat[i].Might * (1 + a.RoleBoost[Cat[i].Role]);
        return m;
    }

    /// <summary>Fighting value now: weariness lowers it, veterans raise it.</summary>
    public static double Might(Army a, Domain domain)
    {
        double m = 0;
        for (int i = 0; i < a.Units.Length; i++)
            if (a.Units[i] > 0 && Cat[i].Domain == domain)
                m += a.Units[i] * Cat[i].Might * (1 + a.RoleBoost[Cat[i].Role]) * (1 + Battle.VeteranBonus * a.UnitXp[i]);
        return m * (1 - 0.5 * a.Fatigue);
    }

    public static double RawMight(RealmState r, Domain domain) => r.Armies.Sum(a => RawMight(a, domain));

    /// <summary>Fighting value now: tired armies fight worse.</summary>
    public static double Might(RealmState r, Domain domain) => r.Armies.Sum(a => Might(a, domain));

    public static int Soldiers(Army a)
    {
        int s = 0;
        for (int i = 0; i < a.Units.Length; i++)
            s += a.Units[i] * Cat[i].Men;
        return s;
    }

    public static int Soldiers(RealmState r) => r.Armies.Sum(Soldiers);

    /// <summary>The army's average weariness, weighted by strength.</summary>
    public static double Fatigue(RealmState r)
    {
        double w = 0, f = 0;
        foreach (var a in r.Armies)
        {
            double m = RawMight(a, Domain.Land) + RawMight(a, Domain.Naval);
            w += m;
            f += m * a.Fatigue;
        }
        return w > 0 ? f / w : 0;
    }

    /// <summary>
    /// Losses in battle: a share of every unit (rounded so that small armies
    /// can lose units too). Returns the men lost.
    /// </summary>
    public static double TakeLosses(Army a, double share, Random rng, Domain? only = null)
    {
        double men = 0;
        for (int i = 0; i < a.Units.Length; i++)
        {
            if (a.Units[i] == 0 || (only != null && Cat[i].Domain != only))
                continue;
            double expected = a.Units[i] * share;
            int lost = (int)Math.Floor(expected);
            if (rng.NextDouble() < expected - lost)
                lost++;
            lost = Math.Min(lost, a.Units[i]);
            a.Units[i] -= lost;
            men += lost * Cat[i].Men;
        }
        return men;
    }

    /// <summary>Losses spread over all of a realm's armies.</summary>
    public static double TakeLosses(RealmState r, double share, Random rng) => r.Armies.Sum(a => TakeLosses(a, share, rng));

    public static double Upkeep(Army a)
    {
        double s = 0;
        for (int i = 0; i < a.Units.Length; i++)
            s += a.Units[i] * Cat[i].Upkeep;
        return s;
    }
}
