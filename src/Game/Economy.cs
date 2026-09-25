using System;
using System.Collections.Generic;
using Facsimilia.World;

namespace Facsimilia.Game;

/// <summary>
/// The yearly economy of every realm (MECHANICS.md, "Economy"): output from
/// its people, taxes from its organized provinces, tribute from its
/// unorganized land, then administration, army upkeep and interest; a
/// shortfall is borrowed at interest (decision "Playable 6": historical
/// remedies), and a realm too deep in debt sees its unpaid soldiers desert.
/// All money in silver talents.
/// </summary>
public static class Economy
{
    /// <summary>What a person produces in a year: about 100 drachmae (Roman estimates run 80-120 denarii).</summary>
    public const double OutputPerPerson = 100.0 / 6000.0;
    /// <summary>Townspeople - craftsmen, traders, officials - produce this many times more.</summary>
    public const double UrbanMultiplier = 2.0;
    /// <summary>Share of output taken by each tax rate (Low, Normal, Heavy, Crushing).</summary>
    public static readonly double[] TaxShare = { 0.06, 0.10, 0.16, 0.25 };
    /// <summary>
    /// The growth drivers' "burden" factor score of each tax rate: light
    /// taxes help families, crushing ones drive them off the land.
    /// </summary>
    public static readonly double[] TaxBurden = { 0.5, 0.0, -0.6, -1.0 };
    /// <summary>Unorganized land pays no tax, only tribute: this share of its output.</summary>
    public const double TributeShare = 0.03;
    /// <summary>Governors, scribes and garrisons per province, a year.</summary>
    public const double AdminPerProvince = 4.0;
    public const double InterestRate = 0.10;
    /// <summary>Beyond this many years of income in debt, lenders stop and soldiers go unpaid.</summary>
    public const double DebtLimitYears = 3.0;
    /// <summary>Share of each unit type that deserts in a year of unpaid wages.</summary>
    public const double DesertionRate = 0.15;
    /// <summary>Manpower: the sustainable share of people who can serve (decision "Playable 11").</summary>
    public const double ManpowerShare = 0.01;
    public const double ManpowerRefill = 0.1;   // share of the gap to the sustainable pool refilled each year

    public static double Output(RealmCensus c) =>
        OutputPerPerson * (c.People + (UrbanMultiplier - 1) * c.UrbanPeople);

    /// <summary>Tax and tribute a realm collects this year.</summary>
    public static (double Tax, double Tribute) Revenue(RealmCensus c, TaxRate rate)
    {
        double output = Output(c);
        double organized = c.People > 0 ? Math.Clamp(c.OrganizedPeople / c.People, 0, 1) : 0;
        return (output * organized * TaxShare[(int)rate], output * (1 - organized) * TributeShare);
    }

    public static double Upkeep(RealmState r)
    {
        double s = 0;
        for (int t = 0; t < UnitTypes.Count; t++)
            s += r.Units[t] * UnitTypes.All[t].Upkeep;
        return s;
    }

    /// <summary>Most people a realm can keep under arms without harming itself.</summary>
    public static double SustainableManpower(RealmCensus c) =>
        ManpowerShare * (c.OrganizedPeople + 0.5 * (c.People - c.OrganizedPeople));

    /// <summary>
    /// One year for one realm. Returns what happened worth a chronicle line
    /// (deserters), or null.
    /// </summary>
    public static string? Tick(RealmState r, RealmCensus c)
    {
        var (tax, tribute) = Revenue(c, r.Tax);
        double admin = AdminPerProvince * c.Provinces;
        double upkeep = Upkeep(r);
        double interest = r.Debt * InterestRate;
        r.LastTax = tax;
        r.LastTribute = tribute;
        r.LastAdmin = admin;
        r.LastUpkeep = upkeep;
        r.LastInterest = interest;

        r.Treasury += tax + tribute - admin - upkeep - interest;
        if (r.Treasury < 0)
        {
            r.Debt += -r.Treasury;   // temples and bankers lend the shortfall
            r.Treasury = 0;
        }
        else if (r.Debt > 0)
        {
            double repay = Math.Min(r.Debt, r.Treasury * 0.5);
            r.Debt -= repay;
            r.Treasury -= repay;
        }

        double target = SustainableManpower(c);
        r.Manpower += (target - r.Manpower) * ManpowerRefill;
        r.Fatigue = Math.Max(0, r.Fatigue - 0.25);   // armies rest

        double income = tax + tribute;
        if (r.Debt > DebtLimitYears * Math.Max(income, 1))
        {
            int lost = 0;
            for (int t = 0; t < UnitTypes.Count; t++)
            {
                int d = (int)Math.Ceiling(r.Units[t] * DesertionRate);
                r.Units[t] -= d;
                lost += d;
            }
            if (lost > 0)
                return $"Unpaid for too long, {lost} units desert.";
        }
        return null;
    }

    /// <summary>
    /// The growth drivers' burden factor per geographic region: each region's
    /// people weighted by the tax rate of the realm that rules them.
    /// </summary>
    public static void ApplyBurden(PopulationEngine pop, IReadOnlyDictionary<int, RealmState> realms)
    {
        int n = pop.RegionCount;
        var weighted = new double[n + 1];
        var total = new double[n + 1];
        foreach (int i in pop.LandNodes)
        {
            int r = pop.RegionOf(i);
            double p = pop.Pop[i];
            total[r] += p;
            if (realms.TryGetValue(pop.NodeOwner[i], out var state))
                weighted[r] += p * TaxBurden[(int)state.Tax];
        }
        for (int r = 1; r <= n; r++)
            pop.SetFactor(r, GrowthFactor.Burden, total[r] > 0 ? weighted[r] / total[r] : 0);
    }
}
