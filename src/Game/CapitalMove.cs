using System;

namespace Facsimilia.Game;

/// <summary>
/// Moving the capital (Cities and settlements): your answers "Allowed at a
/// cost of years, silver and loyalty" and "Administrative reach centres on
/// the new capital at once". Constantine took six years to make
/// Constantinople his seat (324-330); the old capital's people and the
/// court's old families resent it.
/// </summary>
public static class CapitalMove
{
    /// <summary>Years to make a town the seat of government.</summary>
    public const int Years = 5;
    /// <summary>Talents: a base, and more for a bigger realm (a bigger court and treasury to move).</summary>
    public const double CostBase = 100, CostPerThousand = 0.2;
    /// <summary>The old capital's people resent losing the court (a grudge, as after a sack).</summary>
    public const double OldCapitalGrudge = 0.3;
    /// <summary>The court's old families take it badly: the rival faction grows this many points.</summary>
    public const double RivalGain = 15;

    /// <summary>What moving the capital costs a realm of this many people.</summary>
    public static double Cost(double people) => CostBase + CostPerThousand * Math.Max(0, people) / 1000;
}
