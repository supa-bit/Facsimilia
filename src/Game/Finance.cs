using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>The lines of a realm's spending you set (decision "Setting your spending").</summary>
public enum SpendingLine { Court, Temples, Games, Works, Grain }

/// <summary>Who collects the taxes (decision "Tax collectors").</summary>
public enum Collectors { Officials, Farmers }

/// <summary>A debt owed to one lender (decision "Who lends"): the temples, the bankers, or another realm.</summary>
public sealed class Loan
{
    public const string TemplesLender = "temples", BankersLender = "bankers";
    /// <summary>"temples", "bankers", or a realm id as text.</summary>
    public string Lender { get; set; } = BankersLender;
    public double Amount { get; set; }
    public double Rate { get; set; }
    /// <summary>The lending realm, or 0 for temples and bankers.</summary>
    public int LenderRealm => int.TryParse(Lender, out int id) ? id : 0;

    public GArray ToArray() => new() { Lender, Amount, Rate };
    public static Loan FromArray(GArray a) => new() { Lender = a[0].AsString(), Amount = a[1].AsDouble(), Rate = a[2].AsDouble() };
}

/// <summary>A war indemnity paid year by year (decision "War indemnities").</summary>
public sealed class Indemnity
{
    public int Payer { get; set; }
    public int Payee { get; set; }
    public double PerYear { get; set; }
    public int YearsLeft { get; set; }
    /// <summary>Years the payer could not pay.</summary>
    public int Missed { get; set; }

    public GArray ToArray() => new() { Payer, Payee, PerYear, YearsLeft, Missed };
    public static Indemnity FromArray(GArray a) => new()
    { Payer = a[0].AsInt32(), Payee = a[1].AsInt32(), PerYear = a[2].AsDouble(), YearsLeft = a[3].AsInt32(), Missed = a[4].AsInt32() };
}

/// <summary>
/// The treasury's own rules (the Ledger topic "Treasury and money"): your
/// spending lines, who collects the taxes, the silver in your coin and the
/// prices that follow it, the lenders, defaults, and how money is compared
/// through the ages.
/// </summary>
public static class Finance
{
    // --- Spending ---------------------------------------------------------------------------------

    public static readonly string[] LineNames = { "Court", "Temples and festivals", "Games", "Roads and works", "Grain" };
    public static readonly string[] LineHelp =
    {
        "The palace, household, envoys and gifts. Less, and your rival's faction grows; more, and it shrinks.",
        "Sacrifices, priests and festivals. More calms the people everywhere; less angers them.",
        "Games, races and shows for the towns. More calms the people; less angers them.",
        "Roads, aqueducts, walls and granaries. More helps your people grow.",
        "Grain handed out to the poor of your towns, like Rome's dole from 123 BC. Calms the people and helps them grow.",
    };
    /// <summary>What each line costs at full (100%) spending: a share of the tax collected (works: per 1,000 people).</summary>
    public static readonly double[] LineShare = { 0.10, 0.06, 0.04, 0, 0.04 };
    /// <summary>Roads and works: talents a year per 1,000 people at 100%.</summary>
    public const double WorksPerThousand = 0.1;
    /// <summary>Spending can be set from nothing to double.</summary>
    public const double MaxSpending = 2;

    public static double[] DefaultSpending() => new double[] { 1, 1, 1, 1, 0 };

    /// <summary>What one line costs this year.</summary>
    public static double LineCost(RealmState r, SpendingLine line, double tax, double people) =>
        line == SpendingLine.Works
            ? WorksPerThousand * people / 1000 * r.Spending[(int)line]
            : LineShare[(int)line] * tax * r.Spending[(int)line];

    /// <summary>The court, gods, games, works and grain together.</summary>
    public static double Civil(RealmState r, double tax, double people) =>
        Enum.GetValues<SpendingLine>().Sum(l => LineCost(r, l, tax, people));

    /// <summary>
    /// Unrest added everywhere in the realm by its choices: temples, games and
    /// grain (each step below normal angers, above calms); tax farmers squeeze;
    /// a temple curse after defaulting on the gods' silver.
    /// </summary>
    public static double Unrest(RealmState r) =>
        -0.05 * (r.Spending[(int)SpendingLine.Temples] - 1)
        - 0.03 * (r.Spending[(int)SpendingLine.Games] - 1)
        - 0.03 * r.Spending[(int)SpendingLine.Grain]
        + (r.Collectors == Collectors.Farmers ? 0.05 : 0)
        + (r.TempleCurseYears > 0 ? 0.1 : 0)
        + Math.Max(0, r.PriceLevel - 1) * 0.1;   // rising prices bite

    /// <summary>
    /// The growth drivers' burden, added to the tax rate's: works and grain
    /// help families, tax farmers and a temple curse weigh on them.
    /// </summary>
    public static double Burden(RealmState r) =>
        0.15 * (r.Spending[(int)SpendingLine.Works] - 1)
        + 0.1 * r.Spending[(int)SpendingLine.Grain]
        - (r.Collectors == Collectors.Farmers ? 0.2 : 0)
        - (r.TempleCurseYears > 0 ? 0.3 : 0);

    /// <summary>How the court's spending moves the rival faction each year (points of strength).</summary>
    public static double RivalChange(RealmState r) => 4 * (1 - r.Spending[(int)SpendingLine.Court]);

    // --- Tax collectors ---------------------------------------------------------------------------

    /// <summary>Tax farmers keep this share of what they collect.</summary>
    public const double FarmersCut = 0.15;
    /// <summary>Tax farmers pay this share of a year's tax up front for each contract.</summary>
    public const double FarmersUpFront = 0.3;
    /// <summary>A tax-farming contract runs this many years.</summary>
    public const int ContractYears = 5;
    /// <summary>Without salaried collectors, administration costs this much of its usual price.</summary>
    public const double FarmersAdmin = 0.6;

    /// <summary>Why the collectors can't change now, or null.</summary>
    public static string? CanSwitch(RealmState r) =>
        r.ContractYears > 0 ? $"The tax farmers' contract runs {r.ContractYears} more year(s)." : null;

    /// <summary>Changes who collects; tax farmers pay up front. Returns the silver received.</summary>
    public static double Switch(RealmState r, Collectors to)
    {
        if (to == r.Collectors || CanSwitch(r) != null)
            return 0;
        r.Collectors = to;
        if (to != Collectors.Farmers)
            return 0;
        r.ContractYears = ContractYears;
        double paid = FarmersUpFront * r.LastTax;
        r.Treasury += paid;
        return paid;
    }

    // --- The coin ---------------------------------------------------------------------------------

    /// <summary>The lowest silver content you can set.</summary>
    public const double MinPurity = 0.3;
    /// <summary>Each year prices close this share of the gap to what the coin's silver says they should be.</summary>
    public const double PriceCatchUp = 0.15;

    /// <summary>
    /// The mint's profit this year: while prices still lag behind a debased
    /// coin, each talent of tax re-struck yields more coins than it took.
    /// </summary>
    public static double MintProfit(RealmState r, double tax) => tax * Math.Max(0, 1 / r.CoinPurity - r.PriceLevel);

    /// <summary>Merchants value a coin by its real silver: customs fall with it (decision "Bad coin").</summary>
    public static double Trust(RealmState r) => r.CoinPurity;

    /// <summary>What restoring the coin to a purity costs: re-striking a year's worth of the coins in use.</summary>
    public static double RestoreCost(RealmState r, double purity) =>
        purity <= r.CoinPurity ? 0 : (purity - r.CoinPurity) / purity * r.LastTax * r.PriceLevel;

    /// <summary>Sets the coin's silver content. Restoring it costs silver; returns why not, or null.</summary>
    public static string? SetPurity(RealmState r, double purity)
    {
        purity = Math.Clamp(Math.Round(purity, 2), MinPurity, 1);
        double cost = RestoreCost(r, purity);
        if (cost > r.Treasury)
            return $"Restoring the coin needs {cost:0} talents to strike it anew.";
        r.Treasury -= cost;
        r.CoinPurity = purity;
        return null;
    }

    /// <summary>A year for the coin: prices move toward what its silver says.</summary>
    public static void PricesYear(RealmState r) => r.PriceLevel += (1 / r.CoinPurity - r.PriceLevel) * PriceCatchUp;

    // --- Lenders ----------------------------------------------------------------------------------

    /// <summary>The temples lend cheaply but little; the bankers dear but more.</summary>
    public const double TempleRate = 0.06, BankerRate = 0.12, RealmRate = 0.10;
    /// <summary>How many years of income each will lend at most.</summary>
    public const double TempleLimitYears = 1, BankerLimitYears = 2;
    /// <summary>Bankers refuse a realm that defaulted for this many years.</summary>
    public const int BankersMemory = 30;
    /// <summary>The gods' curse on a realm that kept their silver lasts this many years.</summary>
    public const int CurseYears = 10;
    /// <summary>A realm's grievance over an unpaid debt or indemnity lasts this many years.</summary>
    public const int GrievanceYears = 20;
    /// <summary>A borrower repays this share of a realm's loan each year.</summary>
    public const double RealmLoanRepay = 0.1;

    public static double Owed(RealmState r, string lender) => r.Loans.Where(l => l.Lender == lender).Sum(l => l.Amount);

    /// <summary>
    /// Borrows a shortfall from the temples first (cheaper), then the bankers,
    /// within their limits. Returns what could not be borrowed.
    /// </summary>
    public static double Borrow(RealmState r, double amount, double income, int year)
    {
        income = Math.Max(income, 1);
        foreach (var (lender, rate, limit) in new[]
                 {
                     (Loan.TemplesLender, TempleRate, TempleLimitYears * income),
                     (Loan.BankersLender, BankerRate, year < r.BankersRefuseUntil ? 0 : BankerLimitYears * income),
                 })
        {
            if (amount <= 0)
                break;
            double room = Math.Max(0, limit - Owed(r, lender));
            double take = Math.Min(room, amount);
            if (take <= 0)
                continue;
            AddLoan(r, lender, take, rate);
            amount -= take;
        }
        return amount;
    }

    public static void AddLoan(RealmState r, string lender, double amount, double rate)
    {
        var loan = r.Loans.FirstOrDefault(l => l.Lender == lender);
        if (loan == null)
            r.Loans.Add(loan = new Loan { Lender = lender, Rate = rate });
        loan.Amount += amount;
    }

    /// <summary>Repays from a surplus, the dearest loans first. Realms' loans are repaid on their own schedule.</summary>
    public static void Repay(RealmState r, double amount)
    {
        foreach (var loan in r.Loans.Where(l => l.LenderRealm == 0).OrderByDescending(l => l.Rate).ToList())
        {
            if (amount <= 0)
                break;
            double pay = Math.Min(loan.Amount, amount);
            loan.Amount -= pay;
            amount -= pay;
            r.Treasury -= pay;
        }
        r.Loans.RemoveAll(l => l.Amount < 0.01);
    }

    /// <summary>
    /// A default (decision "Defaulting": each lender reacts in its own way):
    /// the temples' silver is kept and the gods' curse falls; the bankers write
    /// off theirs and refuse the realm for a generation; realms that lent are
    /// left with a grievance (the map's FinanceYear turns it into a pretext).
    /// Returns the lending realms wronged.
    /// </summary>
    public static List<int> Default(RealmState r, int year)
    {
        var wronged = new List<int>();
        foreach (var loan in r.Loans)
        {
            if (loan.Lender == Loan.TemplesLender)
                r.TempleCurseYears = CurseYears;
            else if (loan.Lender == Loan.BankersLender)
                r.BankersRefuseUntil = year + BankersMemory;
            else
                wronged.Add(loan.LenderRealm);
        }
        r.Loans.Clear();
        return wronged;
    }

    // --- Money through the ages -------------------------------------------------------------------

    /// <summary>
    /// What money is compared in (decision "Money in the modern age"):
    /// silver until the gold standards of the 1870s, gold until 1971, then a
    /// basket of goods.
    /// </summary>
    public static string Basis(int year) => year < 1870 ? "silver" : year < 1971 ? "gold" : "a basket of goods";
}
