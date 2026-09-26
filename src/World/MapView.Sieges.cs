using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Sieges on the map (decision "Playable 12": sieges over years). Painting
/// enemy land and ending the turn sends the chosen army to besiege it;
/// every year each siege advances by the besiegers' strength against the
/// garrison and by the two realms' Might; the defender may march to relieve
/// it; when a siege is complete the land changes hands.
/// </summary>
public partial class MapView
{
    static bool SameTarget(Siege s, ConquestTarget t) =>
        s.Owner == t.Owner && (t.ProvinceId != 0 ? s.ProvinceId == t.ProvinceId : s.ProvinceId == 0 && s.Cells.Intersect(t.Cells).Any());

    /// <summary>The province a node lies in (by its centre cell), or 0.</summary>
    int ProvinceOfNode(int node)
    {
        if (Population == null || Provinces == null || node < 0)
            return 0;
        int nx = node % Population.Width, ny = node / Population.Width;
        int cx = (int)((nx + 0.5) * GridWidth / Population.Width), cy = (int)((ny + 0.5) * GridHeight / Population.Height);
        return Provinces.Cells[cy * GridWidth + cx];
    }

    internal int ProvinceOfNodeForTest(int node) => ProvinceOfNode(node);

    /// <summary>The defending garrison: the place's militia and any army of the owner standing in it.</summary>
    double Garrison(int owner, int provinceId, double people, int node)
    {
        double g = Conquest.Militia(people, provinceId != 0);
        if (provinceId != 0 && Game.Provinces.TryGetValue(provinceId, out var ps))
            g += BuildingCatalog.Instance.Effect(ps, "garrison");
        if (owner > 0)
            foreach (var a in Game.Realm(owner).Armies)
                if (a.Node >= 0 && (provinceId != 0 ? ProvinceOfNode(a.Node) == provinceId : a.Node == node))
                    g += Military.Might(a, Domain.Land);
        return g;
    }

    /// <summary>The army a defender can spare against one siege: part of its armies not already in the fight.</summary>
    double ReliefMight(int owner, int siegesAgainst)
    {
        if (owner <= 0)
            return 0;
        double free = Game.Realm(owner).Armies
            .Where(a => !Game.Sieges.Any(s => s.Attacker == owner && s.ArmyId == a.Id))
            .Sum(a => Military.Might(a, Domain.Land));
        return Conquest.DefenderCommit * free / Math.Max(1, siegesAgainst);
    }

    /// <summary>What the plan panel shows for each target: the two sides, the chance in battle, and the years of siege.</summary>
    internal void EstimateTargets(IReadOnlyList<ConquestTarget> targets)
    {
        var perArmy = targets.Where(t => t.Problem == null).GroupBy(t => (t.Attacker, t.ArmyId))
            .ToDictionary(g => g.Key, g => g.Count() + Game.Sieges.Count(s => s.Attacker == g.Key.Attacker && s.ArmyId == g.Key.ArmyId));
        foreach (var t in targets)
        {
            if (t.Problem != null)
                continue;
            var army = Game.Realm(t.Attacker).ArmyById(t.ArmyId)!;
            t.AttackerMight = Military.Might(army, Domain.Land) / perArmy[(t.Attacker, t.ArmyId)];
            double garrison = Garrison(t.Owner, t.ProvinceId, t.People, t.Node);
            int against = Game.Sieges.Count(s => s.Owner == t.Owner && t.Owner > 0) + 1;
            t.DefenderMight = garrison + ReliefMight(t.Owner, against);
            t.Chance = Conquest.WinChance(t.AttackerMight, t.DefenderMight);
            double rate = Conquest.SiegeRate(t.AttackerMight, garrison, Military.Might(Game.Realm(t.Attacker), Domain.Land),
                t.Owner > 0 ? Military.Might(Game.Realm(t.Owner), Domain.Land) : 0);
            t.Years = rate > 0 ? Math.Ceiling(1 / rate) : double.PositiveInfinity;
        }
    }

    /// <summary>Sends each target's army to lay siege; the army marches to the first place it besieges.</summary>
    internal List<ChronicleEvent> BeginSieges(IEnumerable<ConquestTarget> targets)
    {
        var events = new List<ChronicleEvent>();
        foreach (var t in targets)
        {
            if (t.Problem != null || Game.Sieges.Any(s => s.Attacker == t.Attacker && SameTarget(s, t)))
                continue;
            var army = Game.Realm(t.Attacker).ArmyById(t.ArmyId);
            if (army == null)
                continue;
            var siege = new Siege
            {
                Attacker = t.Attacker, Owner = t.Owner, ProvinceId = t.ProvinceId, ArmyId = t.ArmyId, Name = t.Name,
                People = t.People, Node = t.Node, StartYear = DemoYear,
            };
            siege.Cells.AddRange(t.Cells);
            Game.Sieges.Add(siege);
            if (!Game.Sieges.Any(s => s != siege && s.Attacker == t.Attacker && s.ArmyId == army.Id))
                army.Node = t.Node;   // the army camps before the first place it besieges
            army.Resting = false;
            string text = $"{army.Name} of {RealmName(t.Attacker)} lays siege to {t.Name}.";
            events.Add(new ChronicleEvent(ChronicleKind.War, t.Attacker, text));
            if (t.Owner > 0)
                events.Add(new ChronicleEvent(ChronicleKind.War, t.Owner, text));
        }
        return events;
    }

    /// <summary>
    /// The year's sieges: relief battles, progress, attrition, and the
    /// places that fall. Returns the chronicle lines.
    /// </summary>
    internal List<ChronicleEvent> SiegesYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Population == null)
            return events;
        foreach (var s in Game.Realms.Values)
            foreach (var a in s.Armies)
                a.Resting = !Game.Sieges.Any(x => x.Attacker == s.RealmId && x.ArmyId == a.Id);
        var census = RealmCensus();
        var fallen = new List<Siege>();
        foreach (var siege in Game.Sieges.ToList())
        {
            var attacker = Game.Realm(siege.Attacker);
            var army = attacker.ArmyById(siege.ArmyId);
            bool stillTheirs = siege.ProvinceId != 0
                ? Provinces != null && Provinces.Provinces.TryGetValue(siege.ProvinceId, out var p) && p.RealmId == siege.Owner
                : siege.Cells.Any(i => Grid.Cells[i] == siege.Owner);
            if (army == null || army.IsEmpty || (siege.Owner > 0 && !Game.Wars.AtWar(siege.Attacker, siege.Owner)) || !stillTheirs)
            {
                Game.Sieges.Remove(siege);
                continue;
            }
            int sharing = Game.Sieges.Count(x => x.Attacker == siege.Attacker && x.ArmyId == siege.ArmyId);
            double attack = Military.Might(army, Domain.Land) / sharing;
            // A relief army may come.
            if (siege.Owner > 0 && rng.NextDouble() < Conquest.ReliefChance)
            {
                int against = Game.Sieges.Count(x => x.Owner == siege.Owner);
                double relief = ReliefMight(siege.Owner, against);
                if (relief > 0.5)
                {
                    bool won = rng.NextDouble() < Conquest.WinChance(attack, relief);
                    var defender = Game.Realm(siege.Owner);
                    double reliefShare = relief / Math.Max(Military.Might(defender, Domain.Land), 1e-9);
                    double attLost = Military.TakeLosses(army, (won ? Conquest.WinnerLosses : Conquest.LoserLosses) / sharing, rng);
                    double defLost = 0;
                    foreach (var a in defender.Armies.Where(a => !Game.Sieges.Any(x => x.Attacker == siege.Owner && x.ArmyId == a.Id)))
                        defLost += Military.TakeLosses(a, Math.Clamp(reliefShare, 0, 1) * (won ? Conquest.LoserLosses : Conquest.WinnerLosses), rng);
                    army.Fatigue = Math.Min(1, army.Fatigue + Conquest.FatiguePerFight);
                    var war = Game.Wars.Between(siege.Attacker, siege.Owner);
                    if (war != null)
                    {
                        bool attackerStarted = war.Attacker == siege.Attacker;
                        double swing = won ? 5 : -8;
                        war.Score = Math.Clamp(war.Score + (attackerStarted ? swing : -swing), -100, 100);
                        war.AttackerLosses += attackerStarted ? attLost : defLost;
                        war.DefenderLosses += attackerStarted ? defLost : attLost;
                    }
                    string text = won
                        ? $"{RealmName(siege.Owner)} marches to relieve {siege.Name}, and is beaten by {army.Name}."
                        : $"{RealmName(siege.Owner)} relieves {siege.Name}: {army.Name} of {RealmName(siege.Attacker)} is beaten and the siege lifted.";
                    events.Add(new ChronicleEvent(ChronicleKind.War, siege.Attacker, text));
                    events.Add(new ChronicleEvent(ChronicleKind.War, siege.Owner, text));
                    if (!won)
                    {
                        Game.Sieges.Remove(siege);
                        continue;
                    }
                    attack = Military.Might(army, Domain.Land) / sharing;
                }
            }
            double garrison = Garrison(siege.Owner, siege.ProvinceId, siege.People, siege.Node);
            siege.Progress += Conquest.SiegeRate(attack, garrison, Military.Might(attacker, Domain.Land),
                siege.Owner > 0 ? Military.Might(Game.Realm(siege.Owner), Domain.Land) : 0);
            Military.TakeLosses(army, Conquest.SiegeAttrition / sharing, rng);
            army.Fatigue = Math.Min(1, army.Fatigue + Conquest.SiegeFatigue / sharing);
            if (siege.Progress >= 1)
                fallen.Add(siege);
        }
        foreach (var siege in fallen)
        {
            Game.Sieges.Remove(siege);
            events.AddRange(Capture(siege, census));
        }
        if (fallen.Count > 0)
        {
            _census = null;
            SyncPopulationOwnership();
            var left = RealmCensus();
            foreach (var owner in fallen.Select(f => f.Owner).Distinct())
                if (owner > 0 && !left.ContainsKey(owner) && Game.Wars.Of(owner).Any())
                {
                    Game.Wars.EndAllOf(owner);
                    Game.Sieges.RemoveAll(x => x.Owner == owner || x.Attacker == owner);
                    events.Add(new ChronicleEvent(ChronicleKind.War, owner, $"{RealmName(owner)} is no more."));
                }
        }
        return events;
    }

    /// <summary>A siege is complete: the land changes hands, captives are taken, and the war score moves.</summary>
    List<ChronicleEvent> Capture(Siege siege, Dictionary<int, RealmCensus> census)
    {
        var events = new List<ChronicleEvent>();
        var war = Game.Wars.Between(siege.Attacker, siege.Owner);
        if (war != null)
        {
            // The world fears conquerors, and each loss tires the loser.
            var pretext = Pretexts.ById(war.Supports != null ? Pretexts.Border.Id : war.CasusBelli);
            bool reconquest = siege.ProvinceId != 0 && Game.Lost.TryGetValue(siege.Attacker, out var lost) && lost.ContainsKey(siege.ProvinceId);
            var att = Game.Realm(siege.Attacker);
            att.Aggression = Math.Min(100, att.Aggression + (reconquest ? 1 : pretext.AggressionPerProvince));
            war.AddExhaustion(siege.Owner, Diplomacy.ExhaustionPerProvince);
        }
        Game.RecordLoss(siege.Owner, siege.ProvinceId, DemoYear);
        if (siege.ProvinceId != 0)
        {
            if (Provinces!.Provinces.TryGetValue(siege.ProvinceId, out var taken))
                TakeCaptives(siege.Attacker, taken);
            Provinces.SetRealm(siege.ProvinceId, siege.Attacker, Grid);
            MarkChanged(siege.ProvinceId);
            Game.Wars.Between(siege.Attacker, siege.Owner)?.RecordTaken(siege.Attacker, siege.ProvinceId);
        }
        else
        {
            foreach (int idx in siege.Cells)
                if (Grid.Cells[idx] == siege.Owner)
                    Grid.Cells[idx] = siege.Attacker;
            MarkChanged(0, siege.Cells);
        }
        TerritoryChanged = true;
        if (war != null)
        {
            double people = Math.Max(census.TryGetValue(siege.Owner, out var c) ? c.People : 0, 1);
            double swing = Math.Clamp(100 * siege.People / people * 2, 5, 40);
            war.Score = Math.Clamp(war.Score + (war.Attacker == siege.Attacker ? swing : -swing), -100, 100);
        }
        string what = siege.ProvinceId != 0 ? siege.Name : siege.Owner == 0 ? "new land" : $"land from {RealmName(siege.Owner)}";
        int years = DemoYear - siege.StartYear - (siege.StartYear < 0 && DemoYear > 0 ? 1 : 0);
        string text = $"{RealmName(siege.Attacker)} takes {what}" + (years > 1 ? $" after a siege of {years} years." : ".");
        events.Add(new ChronicleEvent(ChronicleKind.Conquest, siege.Attacker, text));
        if (siege.Owner > 0)
            events.Add(new ChronicleEvent(ChronicleKind.Conquest, siege.Owner, text));
        return events;
    }
}
