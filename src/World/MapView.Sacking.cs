using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Sacking and ruins (Cities and settlements): what happens to a city when
/// its siege ends, and resettling the ruins of a razed one. See Game/Sacking.cs.
/// </summary>
public partial class MapView
{
    /// <summary>
    /// The city falls: spared, plundered or razed by the siege's choice (the
    /// computer's realms choose for themselves). Returns lines for the chronicle.
    /// </summary>
    List<string> Sack(Siege siege, Province p, bool reconquest)
    {
        var lines = new List<string>();
        var ps = ProvinceStateOf(p.Id);
        var attacker = Game.Realm(siege.Attacker);
        var choice = siege.Attacker == PlayerRealmId
            ? siege.Choice
            : Sacking.ForComputer(ps.Culture != "" && ps.Culture == RealmPeople(siege.Attacker).Culture, reconquest,
                new Random(StableHash.Of(DemoYear, p.Id, 7717)));
        double people = ProvincePopulation(p.Id);
        string city = ProvinceSeat(p.Id) ?? siege.Name;
        switch (choice)
        {
            case SackChoice.Spare:
                ps.Spared = true;
                ps.Grudge = 0;
                lines.Add($"{city} is spared: its people come over to {RealmName(siege.Attacker)} more willingly.");
                break;

            case SackChoice.Plunder:
            {
                TakeCaptives(siege.Attacker, p);
                double loot = Sacking.LootPerThousand * people / 1000;
                attacker.Treasury += loot;
                KillInProvince(p.Id, Sacking.PlunderKilled);
                var damaged = DamageBuildings(p.Id, new Random(StableHash.Of(DemoYear, p.Id, 7703)));
                ps.Grudge = Math.Max(ps.Grudge, Sacking.PlunderGrudge);
                lines.Add($"{city} is plundered: {loot:N0} talents of loot, {people * Sacking.PlunderKilled:N0} killed" +
                    (damaged.Count > 0 ? $"; damaged: {string.Join(", ", damaged)}." : "."));
                break;
            }

            case SackChoice.Raze:
            {
                double captivesBefore = attacker.Captives;
                TakeCaptives(siege.Attacker, p);
                attacker.Captives += (attacker.Captives - captivesBefore) * (Sacking.RazeCaptives - 1);
                double loot = Sacking.LootPerThousand * people / 1000;
                attacker.Treasury += loot;
                KillInProvince(p.Id, Sacking.RazeKilled);
                ps.Buildings.Clear();
                ps.Works.Clear();
                ps.Grudge = Math.Max(ps.Grudge, Sacking.RazeGrudge);
                attacker.Aggression = Math.Min(100, attacker.Aggression + Sacking.RazeAggression);
                attacker.TerrorUntil = DemoYear + Sacking.TerrorYears;
                if (siege.Node >= 0)
                    RavageAtNode(siege.Node, 1);
                Game.Ruins.RemoveAll(r => r.ProvinceId == p.Id);
                Game.Ruins.Add(new Ruin(p.Id, city, DemoYear, siege.Attacker));
                ps.Log(DemoYear, $"{city} is razed by {RealmName(siege.Attacker)}.");
                lines.Add($"{city} is razed: {people * Sacking.RazeKilled:N0} killed, the rest enslaved or scattered, " +
                    $"{loot:N0} talents of loot. Other cities will fear {RealmName(siege.Attacker)}.");
                break;
            }
        }
        _census = null;
        return lines;
    }

    /// <summary>Kills this share of a province's people (a sack, a massacre).</summary>
    void KillInProvince(int provinceId, double share)
    {
        if (Population == null || share <= 0)
            return;
        foreach (int i in Population.LandNodes)
            if (Population.Pop[i] > 0 && ProvinceOfNode(i) == provinceId)
                Population.Pop[i] *= (float)(1 - share);
        InvalidateProvincePeople();
    }

    /// <summary>The ruin in a province, if a city there was razed and not yet resettled.</summary>
    public Ruin? RuinIn(int provinceId) => Game.Ruins.FirstOrDefault(r => r.ProvinceId == provinceId);

    /// <summary>Why the player can't resettle a ruin now, or null.</summary>
    public string? CanResettle(int provinceId)
    {
        if (RuinIn(provinceId) == null)
            return "No ruin here.";
        if (Provinces?.Provinces.TryGetValue(provinceId, out var p) != true || p!.RealmId != PlayerRealmId)
            return "Only the province's ruler can resettle it.";
        var s = PlayerState;
        if (s.Treasury < Sacking.ResettleCost)
            return $"Needs {Sacking.ResettleCost:0} talents.";
        if (s.Manpower < Sacking.ResettleMen)
            return $"Needs {Sacking.ResettleMen:N0} men to send as settlers.";
        return null;
    }

    /// <summary>Settlers go out to the ruins (Caesar's Carthage, 44 BC): the city stands again, its people's grudge eased.</summary>
    public bool Resettle(int provinceId)
    {
        if (CanResettle(provinceId) != null)
            return false;
        var ruin = RuinIn(provinceId)!;
        var s = PlayerState;
        s.Treasury -= Sacking.ResettleCost;
        s.Manpower -= Sacking.ResettleMen;
        Game.Ruins.Remove(ruin);
        var ps = ProvinceStateOf(provinceId);
        ps.Grudge *= 0.5;
        ps.Log(DemoYear, $"{ruin.Name} is resettled by {RealmName(PlayerRealmId)}.");
        return true;
    }
}
