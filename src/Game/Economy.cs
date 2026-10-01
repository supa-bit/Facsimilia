using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Beyond this many years of income, the realm defaults: lenders write off the rest.</summary>
    public const double DefaultYears = 5.0;
    /// <summary>Share of each unit type that deserts in a year of unpaid wages.</summary>
    public const double DesertionRate = 0.15;
    /// <summary>Manpower: the sustainable share of people who can serve (decision "Playable 11").</summary>
    public const double ManpowerShare = 0.01;
    public const double ManpowerRefill = 0.1;   // share of the gap to the sustainable pool refilled each year

    /// <summary>
    /// What a realm's people make in a year, in talents: the value of its
    /// goods (GoodsEngine) when known, else a flat amount per person.
    /// </summary>
    public static double Output(RealmCensus c) => c.Goods != null
        ? c.Goods.Value / 6000.0
        : OutputPerPerson * (c.People + (UrbanMultiplier - 1) * c.UrbanPeople);

    /// <summary>Tax and tribute a realm collects this year.</summary>
    public static (double Tax, double Tribute) Revenue(RealmCensus c, TaxRate rate)
    {
        double output = Output(c);
        double organized = c.People > 0 ? Math.Clamp(c.OrganizedPeople / c.People, 0, 1) : 0;
        // Provinces with their own rate pay it; the rest pay the realm's.
        double own = 0, ownTax = 0;
        for (int r = 0; r < 4; r++)
        {
            own += c.OrganizedAtRate[r];
            ownTax += c.OrganizedAtRate[r] * TaxShare[r];
        }
        double perPerson = c.People > 0 ? output / c.People : 0;
        double tax = c.People > 0
            ? perPerson * (Math.Max(0, c.OrganizedPeople - own) * TaxShare[(int)rate] + ownTax)
            : 0;
        return (tax, output * (1 - organized) * TributeShare);
    }

    /// <summary>
    /// A realm's yearly accounts at its present rates: tax (after its reach,
    /// remedies, techs and corruption), tribute, customs; then the court and
    /// public works, administration, the army and interest.
    /// </summary>
    public static (double Tax, double Tribute, double Customs, double Civil, double Admin, double Upkeep, double Interest)
        Accounts(RealmState r, RealmCensus c, double corruption = 0)
    {
        var (tax, tribute) = Revenue(c, r.Tax);
        // Tax farmers keep their cut but are not robbed by your officials (decision "Tax collectors").
        double kept = r.Collectors == Collectors.Farmers ? 1 - Finance.FarmersCut : 1 - corruption;
        double p = r.PriceLevel;   // all sums are in the realm's coin: they follow its prices (decision "How debasing works")
        tax *= r.TaxReach * Remedies.TaxKept(r) * (1 + TechCatalog.Instance.Effect(r, "tax")) * kept * p;
        tribute *= p;
        double customs = c.Goods != null ? Trade.Customs(c.Goods) * Remedies.CustomsKept(r) * Finance.Trust(r) * p : 0;
        customs *= 1 - Math.Clamp(r.BlockadeMonths / 12.0, 0, 0.8);   // enemy fleets off the coast
        double civil = Finance.Civil(r, tax, c.People) ;
        double admin = (AdminPerProvince * c.Provinces * (r.Collectors == Collectors.Farmers ? Finance.FarmersAdmin : 1) + c.BuildingUpkeep) * p;
        double techInterest = TechCatalog.Instance.Effect(r, "interest");
        double interest = r.Loans.Sum(l => l.Amount * Math.Max(0.02, l.Rate + techInterest));
        return (tax, tribute, customs, civil, admin, Upkeep(r) * p, interest);
    }

    /// <summary>The realm's own troops' upkeep (mercenary companies are paid by the month instead).</summary>
    public static double Upkeep(RealmState r) => r.Armies.Where(a => a.CompanyId == 0).Sum(Military.Upkeep) * r.UpkeepShare;

    /// <summary>Most people a realm can keep under arms without harming itself.</summary>
    public static double SustainableManpower(RealmCensus c, double multiplier = 1) =>
        ManpowerShare * multiplier * c.LevyShare * (1 + c.ManpowerBonus) * (c.OrganizedPeople + 0.5 * (c.People - c.OrganizedPeople));

    /// <summary>
    /// One year for one realm. Returns what happened worth a chronicle line
    /// (deserters), or null.
    /// </summary>
    public static string? Tick(RealmState r, RealmCensus c, double corruption = 0, int year = 0)
    {
        var (tax, tribute, customs, civil, admin, upkeep, interest) = Accounts(r, c, corruption);
        Remedies.Tick(r);
        r.LastCivil = civil;
        r.LastCustoms = customs;
        r.LastTax = tax;
        r.LastTribute = tribute;
        r.LastAdmin = admin;
        r.LastUpkeep = upkeep;
        r.LastInterest = interest;
        r.LastMercPay = r.MercPaidThisYear;
        r.MercPaidThisYear = 0;

        double mint = Finance.MintProfit(r, tax);
        r.LastMint = mint;
        Finance.PricesYear(r);
        r.RivalStrength = Math.Clamp(r.RivalStrength + Finance.RivalChange(r), 0, 100);
        if (r.TempleCurseYears > 0)
            r.TempleCurseYears--;
        if (r.ContractYears > 0)
            r.ContractYears--;

        double income = tax + tribute + customs;
        r.Treasury += income + mint - civil - admin - upkeep - interest;
        bool unpaid = false;
        if (r.Treasury < 0)
        {
            // The temples, then the bankers, lend the shortfall (decision "Who lends").
            double left = Finance.Borrow(r, -r.Treasury, income, year);
            r.Treasury = 0;
            unpaid = left > 0.5;
        }
        else if (r.Debt > 0)
            Finance.Repay(r, r.Treasury * 0.5);
        r.UnpaidYears = unpaid ? r.UnpaidYears + 1 : 0;

        double target = SustainableManpower(c, r.ManpowerMultiplier);
        r.Manpower += (target - r.Manpower) * ManpowerRefill;
        foreach (var a in r.Armies)
            a.Fatigue = Math.Max(0, a.Fatigue - (a.Resting ? 0.25 : 0.1));   // armies rest, sieges less so
        r.Armies.RemoveAll(a => a.IsEmpty && r.Armies.Count > 1);

        if (r.UnpaidYears >= 2)
        {
            // Two years unable to pay or borrow: the realm defaults, and each lender reacts its own way.
            r.Wronged.AddRange(Finance.Default(r, year));
            r.UnpaidYears = 0;
            return "The treasury defaults on its debts." + (r.TempleCurseYears > 0 ? " The temples curse the realm." : "");
        }
        if (unpaid)
        {
            int lost = 0;
            foreach (var a in r.Armies)
            {
                foreach (var reg in a.Regiments)
                {
                    int d = (int)Math.Ceiling(reg.Men * DesertionRate);
                    reg.Men -= d;
                    lost += d;
                }
                a.Tidy();
            }
            if (lost > 0)
                return $"Unpaid for too long, {lost:N0} men desert.";
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
                weighted[r] += p * (TaxBurden[(int)state.Tax] + Finance.Burden(state));
        }
        for (int r = 1; r <= n; r++)
            pop.SetFactor(r, GrowthFactor.Burden, total[r] > 0 ? weighted[r] / total[r] : 0);
    }
}
