using System;
using Facsimilia.Dynasties;

namespace Facsimilia.Game;

public enum Domain { Land, Naval }

/// <summary>
/// One kind of unit (decision "Playable 10": seven types). A unit is a
/// block of men - 1,000 foot, 500 horse, 20 elephants with their crews, or
/// 10 warships - counted per realm, not moved on the map (decision
/// "Playable 9"). Costs are in silver talents: raising (arms, horses, ships)
/// and yearly upkeep (pay and food; a legionary drew about 120 denarii a
/// year in the 2nd century BC, so 1,000 men cost about 20 talents).
/// Needs: the resource a realm must hold (or import at a premium) to raise
/// it.
/// </summary>
public sealed record UnitType(int Id, string Name, Domain Domain, int Men, double Might, double Raise,
    double Upkeep, string? Needs, string Description);

public static class UnitTypes
{
    public const int HeavyInfantry = 0, LightInfantry = 1, Archers = 2, Cavalry = 3, HorseArchers = 4,
        Elephants = 5, Warships = 6;
    public const int Count = 7;

    /// <summary>Price multiplier for raising a unit whose resource the realm must import.</summary>
    public const double ImportPremium = 1.5;

    public static readonly UnitType[] All =
    {
        new(HeavyInfantry, "Heavy infantry", Domain.Land, 1000, 1.0, 25, 18, "res_iron",
            "Close-order spearmen and swordsmen: the phalanx, the legion, citizen hoplites. Need iron."),
        new(LightInfantry, "Light infantry", Domain.Land, 1000, 0.45, 8, 8, null,
            "Skirmishers and javelinmen: cheap, quick, weak in a stand-up fight."),
        new(Archers, "Archers and slingers", Domain.Land, 1000, 0.55, 10, 10, null,
            "Cretan archers, Balearic slingers, Kushite bowmen."),
        new(Cavalry, "Cavalry", Domain.Land, 500, 1.3, 30, 25, "res_horses",
            "Horsemen: Companions, Numidian riders, Celtic nobles. Need horses."),
        new(HorseArchers, "Horse archers", Domain.Land, 500, 1.3, 25, 18, "res_horses",
            "Mounted bowmen of the steppe; only steppe peoples raise them."),
        new(Elephants, "War elephants", Domain.Land, 100, 1.6, 60, 30, "res_elephants",
            "Twenty elephants with their crews: terrifying, costly, unreliable. Need elephant country."),
        new(Warships, "Warships", Domain.Naval, 2000, 1.0, 50, 40, "res_ship_timber",
            "Ten triremes or quinqueremes with rowers and marines. Need ship timber and a coast."),
    };

    /// <summary>What a realm calls a unit type, by its culture.</summary>
    public static string LocalName(int type, Culture culture) => (type, culture) switch
    {
        (HeavyInfantry, Culture.Latin) => "Legionaries",
        (HeavyInfantry, Culture.Greek) => "Phalangites",
        (HeavyInfantry, Culture.Punic) => "Libyan spearmen",
        (HeavyInfantry, Culture.Celtic) => "Warbands",
        (HeavyInfantry, Culture.Iberian) => "Scutarii",
        (LightInfantry, Culture.Latin) => "Velites",
        (LightInfantry, Culture.Greek) => "Peltasts",
        (LightInfantry, Culture.Iberian) => "Caetrati",
        (Archers, Culture.Punic) => "Balearic slingers",
        (Archers, Culture.Meroitic) => "Kushite bowmen",
        (Archers, Culture.Greek) => "Cretan archers",
        (Cavalry, Culture.Greek) => "Companion cavalry",
        (Cavalry, Culture.Punic) => "Numidian horse",
        (Cavalry, Culture.Nabataean) => "Camel riders",
        (Cavalry, Culture.Latin) => "Equites",
        (HorseArchers, Culture.Scythian) => "Scythian horse archers",
        (Warships, Culture.Latin or Culture.Punic) => "Quinqueremes",
        (Warships, _) => "Triremes",
        _ => All[type].Name,
    };

    /// <summary>Only the steppe peoples raise horse archers.</summary>
    public static bool CanRaise(int type, Culture culture) =>
        type != HorseArchers || culture == Culture.Scythian;
}
