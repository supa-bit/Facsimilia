using System;
using System.Collections.Generic;
using System.Linq;

namespace Facsimilia.Game;

/// <summary>
/// Historical remedies for an empty treasury (decision "Playable 6", with
/// the designer's note: the options offered depend on the realm's
/// situation). Each brings silver now at a price paid over the following
/// years.
/// </summary>
public sealed record Remedy(string Id, string Name, string Description, double IncomeShare, int Years);

public static class Remedies
{
    public static readonly Remedy Debase = new("debase", "Debase the coin",
        "Strike more coins from the same silver, as Rome did with the denarius: a year's income now, but for ten years " +
        "merchants trust your coin less (customs -20%) and prices bite (+5% unrest everywhere).", 1.0, 10);
    public static readonly Remedy Temples = new("temples", "Borrow the temple treasures",
        "Take the gods' silver 'as a loan', as Athens did from Athena in the Peloponnesian War: half a year's income now; " +
        "the people call it sacrilege (+15% unrest for five years).", 0.5, 5);
    public static readonly Remedy TaxFarms = new("tax_farms", "Sell the tax farms",
        "Let contractors (the publicani) buy the right to collect your taxes: 40% of a year's income now, " +
        "but they keep a cut for five years (taxes -10%). Needs at least three provinces.", 0.4, 5);
    public static readonly Remedy RichLevy = new("rich_levy", "Levy on the rich",
        "An emergency tax on the wealthy (the Athenian eisphora): 30% of a year's income now; the great families " +
        "grumble (+10% unrest for a year).", 0.3, 1);
    public static readonly Remedy AllyGift = new("ally_gift", "Ask a friend for silver",
        "Ask an ally or your overlord who thinks well of you for help: they may send silver.", 0.3, 0);

    public static readonly Remedy[] All = { Debase, Temples, TaxFarms, RichLevy, AllyGift };

    /// <summary>The treasury is in trouble: in debt, or with less than a year's upkeep and administration.</summary>
    public static bool InTrouble(RealmState r) => r.Debt > 0.5 || r.Treasury < r.LastUpkeep + r.LastAdmin;

    /// <summary>Whether a remedy's effects are still being felt.</summary>
    public static bool Active(RealmState r, string id) => r.RemedyYears.TryGetValue(id, out int y) && y > 0;

    /// <summary>Unrest the remedies still add in every province of the realm.</summary>
    public static double Unrest(RealmState r) =>
        (Active(r, Debase.Id) ? 0.05 : 0) + (Active(r, Temples.Id) ? 0.15 : 0) + (Active(r, RichLevy.Id) ? 0.1 : 0);

    /// <summary>Share of taxes left after the tax farmers' cut.</summary>
    public static double TaxKept(RealmState r) => Active(r, TaxFarms.Id) ? 0.9 : 1;

    /// <summary>Share of customs left when merchants distrust the coin.</summary>
    public static double CustomsKept(RealmState r) => Active(r, Debase.Id) ? 0.8 : 1;

    /// <summary>A year passes: the remedies' effects wear off.</summary>
    public static void Tick(RealmState r)
    {
        foreach (var id in r.RemedyYears.Keys.ToList())
            if (--r.RemedyYears[id] <= 0)
                r.RemedyYears.Remove(id);
    }
}
