using System;

namespace Facsimilia.Game;

/// <summary>
/// Armies loyal to their general, and veterans' land (War: armies, units
/// and generals). Your answers: loyalty grows with service, full in 5 years,
/// faster with victories and loot; a loyal army fights better and may follow
/// its general against you (Sulla, Caesar). Veterans expect land when
/// discharged, in a province you choose (conquered land is best); denied, they
/// stir unrest and may follow a rival, though sometimes they take silver
/// instead, depending on how many they are, how hardened, and whether a
/// general leads them (your note).
/// </summary>
public static class ArmyLoyalty
{
    // --- Loyalty to the general ------------------------------------------------------------
    /// <summary>A year under the same general (full in 5 years).</summary>
    public const double PerYear = 0.2;
    /// <summary>A victory won together, and a city plundered or razed (the loot shared out).</summary>
    public const double PerVictory = 0.1, PerLoot = 0.1;
    /// <summary>A fully loyal army fights this much better.</summary>
    public const double MoraleBonus = 0.15;
    /// <summary>Below this loyalty an army never turns on you.</summary>
    public const double MutinyFrom = 0.6;
    /// <summary>The most likely a mutiny can be in one year.</summary>
    public const double MutinyMax = 0.25;

    /// <summary>The army's morale from its loyalty to its general (1 = none).</summary>
    public static double Morale(Army a) => 1 + MoraleBonus * (a.GeneralId != 0 ? Math.Clamp(a.GeneralLoyalty, 0, 1) : 0);

    /// <summary>
    /// What pushes a loyal army against its own realm: pay owed, veterans
    /// left without land, and a strong rival faction at court.
    /// </summary>
    public static double Pressure(RealmState r) =>
        (r.UnpaidYears > 0 ? 0.5 : 0) + (r.VeteransWaiting >= 1 ? 0.4 : 0) + r.RivalStrength / 200;

    /// <summary>The chance this year that an army follows its general against the realm.</summary>
    public static double MutinyChance(double loyalty, double pressure) =>
        loyalty < MutinyFrom ? 0 : Math.Min(MutinyMax, (loyalty - MutinyFrom) / (1 - MutinyFrom) * pressure * 0.2);

    // --- Veterans ----------------------------------------------------------------------------
    /// <summary>Units this hardened (experience) count as veterans when sent home.</summary>
    public const double VeteranXp = 0.3;
    /// <summary>Settled on conquered land: integration gained per 1,000 veterans, at most VeteranIntegrationMax.</summary>
    public const double IntegrationPerThousand = 0.03, VeteranIntegrationMax = 0.3;
    /// <summary>Settled on your own people's land: a grudge (farms taken from their owners).</summary>
    public const double HomeLandGrudge = 0.1;
    /// <summary>Waiting veterans stir unrest everywhere: this much per 1,000, at most WaitingUnrestMax.</summary>
    public const double WaitingUnrestPerThousand = 0.01, WaitingUnrestMax = 0.2;
    /// <summary>Each year they wait, the rival faction grows (they may follow a rival).</summary>
    public const double WaitingRivalPerYear = 2;
    /// <summary>Paying them off instead: talents per veteran.</summary>
    public const double PayOffPerVeteran = 0.05;

    /// <summary>Unrest everywhere in a realm from its veterans waiting for land.</summary>
    public static double WaitingUnrest(RealmState r) =>
        Math.Min(WaitingUnrestMax, r.VeteransWaiting / 1000 * WaitingUnrestPerThousand);

    /// <summary>
    /// The chance, each year they wait, that veterans offer to take silver
    /// instead of land: less the more of them, the more hardened, and if a
    /// general leads them.
    /// </summary>
    public static double PayOffChance(double men, double xp, bool generalLed) =>
        Math.Clamp(0.6 - men / 40_000 - 0.4 * xp - (generalLed ? 0.3 : 0), 0.05, 0.6);

    /// <summary>What paying off the waiting veterans costs.</summary>
    public static double PayOffCost(double men) => PayOffPerVeteran * men;

    /// <summary>Veterans sent home: they join those already waiting for land.</summary>
    public static void Discharge(RealmState r, double men, double xp, bool generalLed, int year)
    {
        if (men <= 0)
            return;
        double total = r.VeteransWaiting + men;
        r.VeteransXp = (r.VeteransXp * r.VeteransWaiting + xp * men) / total;
        r.VeteransWaiting = total;
        r.VeteransLed |= generalLed;
        if (r.VeteransSince == 0)
            r.VeteransSince = year;
    }

    /// <summary>The veterans are dealt with (settled or paid).</summary>
    public static void Clear(RealmState r)
    {
        r.VeteransWaiting = 0;
        r.VeteransXp = 0;
        r.VeteransLed = false;
        r.VeteransSince = 0;
        r.VeteransOfferSilver = false;
    }
}
