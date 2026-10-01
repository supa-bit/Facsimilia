using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// The treasury between realms (the Ledger topic "Treasury and money"):
/// loans from one realm to another, war indemnities paid year by year, and
/// the grievances left by unpaid debts, which give a pretext for war.
/// </summary>
public partial class MapView
{
    /// <summary>An indemnity is this many years of the loser's income in all ...</summary>
    public const double IndemnityYearsOfIncome = 2;
    /// <summary>... paid over this many years (Carthage paid Rome over ten years after 241 BC, over fifty after 201 BC).</summary>
    public const int IndemnityYears = 10;
    /// <summary>A payer who misses this many years' payments has stopped paying.</summary>
    public const int IndemnityMissesAllowed = 2;

    /// <summary>Does one realm hold a grievance against another this year (an unpaid debt or indemnity)?</summary>
    public bool HasGrievance(int holder, int target) =>
        Game.Grievances.TryGetValue((holder, target), out int until) && DemoYear <= until;

    void AddGrievance(int holder, int target) => Game.Grievances[(holder, target)] = DemoYear + Finance.GrievanceYears;

    /// <summary>
    /// The year between realms, after each realm's own accounts: loans between
    /// realms pay their interest and part of their sum to the lender;
    /// indemnities are paid; defaults and stopped payments leave grievances.
    /// </summary>
    public List<ChronicleEvent> FinanceYear()
    {
        var events = new List<ChronicleEvent>();
        foreach (var s in Game.Realms.Values)
        {
            foreach (int wronged in s.Wronged)
            {
                AddGrievance(wronged, s.RealmId);
                string text = $"{RealmName(s.RealmId)} will not repay what it owes {RealmName(wronged)}.";
                events.Add(new ChronicleEvent(ChronicleKind.Economy, wronged, text));
                events.Add(new ChronicleEvent(ChronicleKind.Economy, s.RealmId, text));
            }
            s.Wronged.Clear();
            // A realm's loan: the interest (already in the borrower's accounts) and a tenth of the sum go to the lender.
            foreach (var loan in s.Loans.Where(l => l.LenderRealm != 0).ToList())
            {
                if (!Game.Realms.TryGetValue(loan.LenderRealm, out var lender))
                {
                    s.Loans.Remove(loan);
                    continue;
                }
                lender.Treasury += loan.Amount * loan.Rate;
                double repay = Math.Min(loan.Amount, Math.Min(s.Treasury, Math.Max(loan.Amount * Finance.RealmLoanRepay, 1)));
                s.Treasury -= repay;
                lender.Treasury += repay;
                loan.Amount -= repay;
            }
            s.Loans.RemoveAll(l => l.Amount < 0.01);
        }
        foreach (var ind in Game.Indemnities.ToList())
        {
            if (!Game.Realms.TryGetValue(ind.Payer, out var payer) || !Game.Realms.TryGetValue(ind.Payee, out var payee))
            {
                Game.Indemnities.Remove(ind);
                continue;
            }
            if (payer.Treasury >= ind.PerYear)
            {
                payer.Treasury -= ind.PerYear;
                payee.Treasury += ind.PerYear;
                ind.YearsLeft--;
            }
            else if (++ind.Missed >= IndemnityMissesAllowed)
            {
                StopIndemnity(ind, events, "cannot pay");
                continue;
            }
            if (ind.YearsLeft <= 0)
            {
                Game.Indemnities.Remove(ind);
                events.Add(new ChronicleEvent(ChronicleKind.Economy, ind.Payer, $"{RealmName(ind.Payer)} pays the last of its indemnity to {RealmName(ind.Payee)}."));
            }
        }
        foreach (var key in Game.Grievances.Where(g => g.Value < DemoYear).Select(g => g.Key).ToList())
            Game.Grievances.Remove(key);
        return events;
    }

    void StopIndemnity(Indemnity ind, List<ChronicleEvent> events, string why)
    {
        Game.Indemnities.Remove(ind);
        AddGrievance(ind.Payee, ind.Payer);
        string text = $"{RealmName(ind.Payer)} {why} and stops paying its indemnity to {RealmName(ind.Payee)}, who now has a pretext for war.";
        events.Add(new ChronicleEvent(ChronicleKind.Economy, ind.Payer, text));
        events.Add(new ChronicleEvent(ChronicleKind.Economy, ind.Payee, text));
    }

    /// <summary>You stop paying an indemnity (decision "War indemnities": stopping is a pretext for war).</summary>
    public List<ChronicleEvent> StopPayingIndemnity(Indemnity ind)
    {
        var events = new List<ChronicleEvent>();
        if (ind.Payer == PlayerRealmId && Game.Indemnities.Contains(ind))
            StopIndemnity(ind, events, "refuses");
        return events;
    }

    /// <summary>
    /// A peace with an indemnity (decision "War indemnities": yearly payments
    /// over many years): the loser owes two years of its income, paid over ten.
    /// Returns the yearly payment.
    /// </summary>
    public double ImposeIndemnity(int payer, int payee, double payerIncome)
    {
        double perYear = Math.Max(1, payerIncome * IndemnityYearsOfIncome / IndemnityYears);
        Game.Indemnities.Add(new Indemnity { Payer = payer, Payee = payee, PerYear = perYear, YearsLeft = IndemnityYears });
        return perYear;
    }

    /// <summary>What you could lend a realm now: up to half your treasury, at most two years of its income.</summary>
    public double LendLimit(int borrower)
    {
        var c = CensusOf(borrower);
        var (tax, trib) = Economy.Revenue(c, Game.Realm(borrower).Tax);
        return Math.Max(0, Math.Min(PlayerState.Treasury * 0.5, 2 * (tax + trib) - Finance.Owed(Game.Realm(borrower), PlayerRealmId.ToString())));
    }

    /// <summary>
    /// Lends silver to another realm at interest (decision "Lending to others"):
    /// it accepts if it is in debt or short of silver. Returns why not, or null.
    /// </summary>
    public string? Lend(int borrower, double amount)
    {
        if (borrower == PlayerRealmId || !Game.Realms.TryGetValue(borrower, out var b))
            return "No such realm.";
        if (Game.Wars.AtWar(PlayerRealmId, borrower))
            return "You are at war with them.";
        amount = Math.Min(amount, LendLimit(borrower));
        if (amount < 1)
            return "You have too little to lend, or they already owe you as much as they can bear.";
        if (!Remedies.InTrouble(b) && b.Treasury > amount)
            return $"{RealmName(borrower)} has no need of your silver.";
        PlayerState.Treasury -= amount;
        b.Treasury += amount;
        Finance.AddLoan(b, PlayerRealmId.ToString(), amount, Finance.RealmRate);
        return null;
    }
}
