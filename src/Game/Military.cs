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
        bool elephantSource = false, int origin = 0)
    {
        var (problem, cost) = CanRecruit(r, c, cultures, u, elephantSource);
        if (problem != null)
            return false;
        r.Treasury -= cost;
        r.Manpower -= u.Men;
        army.AddRegiment(u.Index, origin: origin);   // a new unit of raw recruits
        army.Even();
        return true;
    }

    /// <summary>Sends a unit home: its men return to the pool, the silver spent on it doesn't.</summary>
    public static bool Disband(RealmState r, Army army, UnitDef u)
    {
        int men = army.RemoveUnits(u.Index);
        if (men <= 0)
            return false;
        army.Even();
        r.Manpower += men;
        return true;
    }

    /// <summary>Sends one unit home: its men and its wounded return to the pool.</summary>
    public static bool Disband(RealmState r, Army army, Regiment reg)
    {
        if (!army.Regiments.Remove(reg))
            return false;
        r.Manpower += reg.Men + reg.Wounded;
        army.Even();
        return true;
    }

    /// <summary>Fighting value of an army in a domain, before fatigue.</summary>
    public static double RawMight(Army a, Domain domain)
    {
        double m = 0;
        foreach (var r in a.Regiments)
        {
            var u = Cat[r.Type];
            if (u.Domain == domain)
                m += r.Men / (double)u.Men * u.Might * (1 + a.RoleBoost[u.Role]);
        }
        return m;
    }

    /// <summary>Fighting value now: weariness lowers it, veterans raise it.</summary>
    public static double Might(Army a, Domain domain)
    {
        double m = 0;
        foreach (var r in a.Regiments)
        {
            var u = Cat[r.Type];
            if (u.Domain == domain)
                m += r.Men / (double)u.Men * u.Might * (1 + a.RoleBoost[u.Role]) * (1 + Battle.VeteranBonus * r.Xp);
        }
        return m * (1 - 0.5 * a.Fatigue);
    }

    public static double RawMight(RealmState r, Domain domain) => r.Armies.Sum(a => RawMight(a, domain));

    /// <summary>Fighting value now: tired armies fight worse.</summary>
    public static double Might(RealmState r, Domain domain) => r.Armies.Sum(a => Might(a, domain));

    public static int Soldiers(Army a) => a.Men;

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
    /// Losses in battle: a share of every unit's men, deadShare of them killed,
    /// the rest wounded (your answer: the wounded are separate from the dead).
    /// Returns the men lost to the line.
    /// </summary>
    public static double TakeLosses(Army a, double share, Random rng, Domain? only = null, double deadShare = WinnerDead) =>
        a.TakeLosses(share, deadShare, rng, only);

    /// <summary>
    /// Of the men a beaten army loses, half are killed (the slaughter of the
    /// rout); of a victor's, a quarter. The rest are wounded.
    /// </summary>
    public const double LoserDead = 0.5, WinnerDead = 0.25;
    /// <summary>Men lost with sunken ships mostly drown; of hunger and the sickness of siege camps, most die.</summary>
    public const double SeaDead = 0.8, HungerDead = 0.8, SickDead = 0.6;

    /// <summary>Losses spread over all of a realm's armies.</summary>
    public static double TakeLosses(RealmState r, double share, Random rng) => r.Armies.Sum(a => TakeLosses(a, share, rng));

    public static double Upkeep(Army a)
    {
        double s = 0;
        foreach (var r in a.Regiments)
            s += r.Men / (double)Cat[r.Type].Men * Cat[r.Type].Upkeep;
        return s;
    }
}
