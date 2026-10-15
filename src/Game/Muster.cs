using Godot;
using GArray = Godot.Collections.Array;

namespace Facsimilia.Game;

/// <summary>
/// A unit being raised (War: armies, units and generals): your answers "In
/// provinces whose people make them, then marched to the army", "1 month
/// for levies, 3-6 for trained troops" and "They walk by themselves,
/// arriving after the travel time" (your note: they leave in sets, a
/// unit's men together). Paid for when ordered; paid upkeep while training.
/// </summary>
public sealed class Muster
{
    public int Type { get; init; }
    public int Men { get; init; }
    /// <summary>The province it is raised in (its men's home).</summary>
    public int Province { get; init; }
    /// <summary>The army it marches to (if that army is gone, the realm's largest).</summary>
    public int ArmyId { get; set; }
    public int TrainLeft { get; set; }
    public int TravelLeft { get; set; }
    public int Months => TrainLeft + TravelLeft;

    public GArray ToArray() => new() { Type, Men, Province, ArmyId, TrainLeft, TravelLeft };
    public static Muster FromArray(GArray a) => new()
    {
        Type = a[0].AsInt32(), Men = a[1].AsInt32(), Province = a[2].AsInt32(), ArmyId = a[3].AsInt32(),
        TrainLeft = a[4].AsInt32(), TravelLeft = a[5].AsInt32(),
    };

    /// <summary>Months to train a unit: levies a month; trained foot 3-4; horse 5; elephants and ships 6.</summary>
    public static int TrainingMonths(UnitDef u) =>
        u.Anyone && u.Raise <= 20 && u.Domain == Domain.Land && u.Role != UnitRoles.Elephants ? 1
        : u.Role switch
        {
            UnitRoles.LightInfantry or UnitRoles.Missile => 3,
            UnitRoles.HeavyInfantry => 4,
            UnitRoles.Cavalry or UnitRoles.HorseArchers => 5,
            _ => 6,
        };

    /// <summary>A day's march for recruits on their own: about 20 km, so 600 km a month.</summary>
    public const double KmPerMonth = 600;
}
