using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

public enum Domain { Land, Naval }

/// <summary>What a unit does in battle.</summary>
public static class UnitRoles
{
    public const int HeavyInfantry = 0, LightInfantry = 1, Missile = 2, Cavalry = 3, HorseArchers = 4,
        Elephants = 5, Warships = 6;
    public const int Count = 7;
    public static readonly string[] Keys =
        { "heavy_infantry", "light_infantry", "missile", "cavalry", "horse_archers", "elephants", "warships" };
    public static readonly string[] Names =
        { "Heavy infantry", "Light infantry", "Archers and slingers", "Cavalry", "Horse archers", "War elephants", "Warships" };
    /// <summary>The unit anyone can raise for each role (and what old saves' role counts become).</summary>
    public static readonly string[] Generic =
        { "levy_spearmen", "skirmishers", "slingers", "light_horse", "scythian_horse_archers", "war_elephants", "triremes" };

    public static Domain DomainOf(int role) => role == Warships ? Domain.Naval : Domain.Land;
}

/// <summary>
/// One kind of unit (data/units.json; decision "Playable 10": every
/// culture's own units). A unit is a block of men - 1,000 foot, 500 horse,
/// 20 elephants with their crews, or 10 warships. Costs in silver talents:
/// raising (arms, horses, ships) and yearly upkeep (pay and food; a
/// legionary drew about 120 denarii a year, so 1,000 men cost about 20).
/// </summary>
public sealed record UnitDef(int Index, string Id, string Name, int Role, double Might, int Men, double Raise,
    double Upkeep, string? Needs, string[] Cultures, string Description, string? Requires = null)
{
    public Domain Domain => UnitRoles.DomainOf(Role);
    public bool Anyone => Cultures.Contains("*");
}

/// <summary>All unit types, loaded once from data/units.json.</summary>
public sealed class UnitCatalog
{
    public const string Path = "res://data/units.json";
    static UnitCatalog? _instance;

    public List<UnitDef> Units { get; } = new();
    readonly Dictionary<string, UnitDef> _byId = new();

    public static UnitCatalog Instance =>
        _instance ??= Parse(Godot.FileAccess.GetFileAsString(Path));

    public int Count => Units.Count;
    public UnitDef this[int index] => Units[index];
    public UnitDef this[string id] => _byId[id];
    public bool Has(string id) => _byId.ContainsKey(id);

    public static UnitCatalog Parse(string json)
    {
        var c = new UnitCatalog();
        using var doc = JsonDocument.Parse(json);
        foreach (var u in doc.RootElement.GetProperty("units").EnumerateArray())
        {
            var def = new UnitDef(c.Units.Count, u.GetProperty("id").GetString()!, u.GetProperty("name").GetString()!,
                Array.IndexOf(UnitRoles.Keys, u.GetProperty("role").GetString()!), u.GetProperty("might").GetDouble(),
                u.GetProperty("men").GetInt32(), u.GetProperty("raise_cost").GetDouble(), u.GetProperty("upkeep").GetDouble(),
                u.TryGetProperty("needs", out var n) ? n.GetString() : null,
                u.GetProperty("cultures").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                u.GetProperty("description").GetString()!,
                u.TryGetProperty("requires", out var rq) ? rq.GetString() : null);
            c.Units.Add(def);
            c._byId[def.Id] = def;
        }
        return c;
    }

    /// <summary>Units a realm can raise: its own peoples' and anyone's (cultures = its culture and its provinces' peoples).</summary>
    public IEnumerable<UnitDef> Available(IReadOnlyCollection<string> cultures) =>
        Units.Where(u => u.Anyone || u.Cultures.Any(cultures.Contains));

    /// <summary>The best unit of a role a people can raise (its own first, else anyone's, else hired from a people who fight that way).</summary>
    public UnitDef BestFor(int role, IReadOnlyCollection<string> cultures) =>
        Units.Where(u => u.Role == role && !u.Anyone && u.Cultures.Any(cultures.Contains))
            .OrderByDescending(u => u.Might).FirstOrDefault()
        ?? Units.FirstOrDefault(u => u.Role == role && u.Anyone)
        ?? Units.Where(u => u.Role == role).OrderBy(u => u.Might).First();   // hired from a neighbouring people
}
