using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Treasury and money (your answers): spending lines with their effects;
/// tax farmers pay up front and squeeze; a debased coin brings mint profit
/// that fades as prices catch up, costs trust, and is dear to restore;
/// the temples then the bankers lend; a default angers each lender in its
/// own way; realms lend to each other; indemnities are paid year by year and
/// stopping them gives a pretext for war; all of it survives a save.
/// </summary>
public partial class FinanceTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.PlayerRealmId, carthage = map.CivRealmIds["carthage"];
        var r = map.PlayerState;
        var c = map.CensusOf(rome);

        // Spending lines: the usual spending is unchanged; each line has its effect.
        var (tax, _, _, civil, _, _, _) = Economy.Accounts(r, c);
        Check(Math.Abs(civil - (0.2 * tax + 0.1 * c.People / 1000)) < 1e-6, "the usual spending should match the old court and works");
        Check(Finance.Unrest(r) == 0 && Finance.Burden(r) == 0, "the usual spending changes nothing");
        r.Spending[(int)SpendingLine.Temples] = 2;
        r.Spending[(int)SpendingLine.Grain] = 1;
        Check(Finance.Unrest(r) < 0 && Finance.Burden(r) > 0, "temples and grain should calm and help growth");
        double rival = r.RivalStrength;
        r.Spending[(int)SpendingLine.Court] = 0;
        Check(Finance.RivalChange(r) > 0, "a starved court should let the rival grow");
        for (int i = 0; i < r.Spending.Length; i++) r.Spending[i] = Finance.DefaultSpending()[i];

        // Tax farmers.
        r.LastTax = 1000;
        double t0 = r.Treasury;
        Check(Finance.Switch(r, Collectors.Farmers) == 300 && r.Treasury == t0 + 300 && r.Collectors == Collectors.Farmers, "tax farmers should pay 30% up front");
        Check(Finance.CanSwitch(r) != null, "a tax-farming contract can't be broken at once");
        Check(Finance.Unrest(r) > 0 && Finance.Burden(r) < 0, "tax farmers squeeze the people");
        r.ContractYears = 0;
        Finance.Switch(r, Collectors.Officials);

        // The coin.
        Check(Finance.SetPurity(r, 0.5) == null && r.CoinPurity == 0.5, "debasing the coin");
        double mint0 = Finance.MintProfit(r, 1000);
        for (int y = 0; y < 10; y++) Finance.PricesYear(r);
        Check(mint0 > 900 && Finance.MintProfit(r, 1000) < mint0 * 0.3 && r.PriceLevel > 1.6, $"mint profit should fade as prices rise ({mint0:0} -> {Finance.MintProfit(r, 1000):0}, prices x{r.PriceLevel:0.00})");
        Check(Finance.Trust(r) < 1, "merchants should trust a debased coin less");
        r.Treasury = 0;
        Check(Finance.SetPurity(r, 1) != null && r.CoinPurity == 0.5, "restoring the coin costs silver");
        r.Treasury = 1e6;
        Check(Finance.SetPurity(r, 1) == null && r.Treasury < 1e6, "restoring the coin with silver in hand");
        r.PriceLevel = 1;

        // Lenders, and a default.
        r.Debt = 0;
        double left = Finance.Borrow(r, 2500, 1000, map.DemoYear);
        Check(left == 0 && Math.Abs(Finance.Owed(r, Loan.TemplesLender) - 1000) < 1e-6 && Math.Abs(Finance.Owed(r, Loan.BankersLender) - 1500) < 1e-6,
            "the temples lend first, up to a year's income, then the bankers");
        Check(Finance.Borrow(r, 1000, 1000, map.DemoYear) == 500, "beyond their limits nobody lends");
        Finance.AddLoan(r, carthage.ToString(), 200, Finance.RealmRate);
        var wronged = Finance.Default(r, map.DemoYear);
        Check(r.Debt == 0 && r.TempleCurseYears > 0 && r.BankersRefuseUntil > map.DemoYear && wronged.SequenceEqual(new[] { carthage }),
            "a default: the temples curse, the bankers refuse, the lending realm is wronged");
        Check(Finance.Borrow(r, 100, 1000, map.DemoYear + 1) == 0 && Finance.Owed(r, Loan.BankersLender) == 0, "after a default only the temples lend");
        r.Wronged.AddRange(wronged);
        map.FinanceYear();
        Check(map.HasGrievance(carthage, rome) && map.Game.Realm(carthage).CivKey != "" &&
              map.PretextsAgainst(carthage, rome).Contains(Pretexts.Debt), "an unpaid debt should give the lender a pretext");
        r.Debt = 0;
        r.TempleCurseYears = 0;

        // Lending to another realm.
        var carth = map.Game.Realm(carthage);
        carth.Treasury = 0;
        carth.Debt = 500;
        r.Treasury = 10000;
        Check(map.Lend(carthage, 100) == null && Finance.Owed(carth, rome.ToString()) == 100 && r.Treasury == 9900, "lending to a realm short of silver");
        double before = r.Treasury;
        map.FinanceYear();
        Check(r.Treasury > before, "a borrower pays interest and part of the sum to its lender");

        // Indemnities.
        double perYear = map.ImposeIndemnity(carthage, rome, 1000);
        Check(Math.Abs(perYear - 200) < 1e-6 && map.Game.Indemnities.Count == 1, "an indemnity of two years' income over ten years");
        carth.Treasury = 1000;
        before = r.Treasury;
        map.FinanceYear();
        Check(r.Treasury >= before + 200 && map.Game.Indemnities[0].YearsLeft == 9, "the indemnity is paid each year");
        var mine = map.ImposeIndemnity(rome, carthage, 1000);
        var ind = map.Game.Indemnities.First(x => x.Payer == rome);
        map.StopPayingIndemnity(ind);
        Check(!map.Game.Indemnities.Contains(ind) && map.HasGrievance(carthage, rome), "stopping an indemnity gives a pretext for war");

        Check(Finance.Basis(-300) == "silver" && Finance.Basis(1900) == "gold" && Finance.Basis(2000) == "a basket of goods", "money compared by the ages");

        // Saves.
        r.Spending[(int)SpendingLine.Games] = 1.5;
        r.CoinPurity = 0.8;
        Finance.AddLoan(r, Loan.TemplesLender, 123, Finance.TempleRate);
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var r2 = map2.PlayerState;
        Check(r2.Spending[(int)SpendingLine.Games] == 1.5 && r2.CoinPurity == 0.8 && Math.Abs(r2.Debt - 123) < 0.01 && r2.Loans[0].Lender == Loan.TemplesLender,
            "spending, coin and loans should survive a save");
        Check(map2.Game.Indemnities.Count == map.Game.Indemnities.Count && map2.HasGrievance(carthage, rome), "indemnities and grievances should survive a save");

        Finish($"Finance tests passed: spending lines; tax farmers; mint profit {mint0:0} fading as prices rise; lenders and default; lending; indemnities; saves.");
        map.Free();
        map2.Free();
    }
}
