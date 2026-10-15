using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Army traditions and guards (War: armies, units and generals): history's
/// traditions at the start, traditions earned by victories and cities
/// taken, forgotten after 50 years unused or broken by a crushing defeat;
/// famous veterans made the realm's guard. See Game/Traditions.cs.
/// </summary>
public partial class MapView
{
    /// <summary>Each realm starts with its people's traditions from history.</summary>
    void StartTraditions()
    {
        var cat = TraditionCatalog.Instance;
        foreach (var (id, s) in Game.Realms)
            foreach (var t in cat.HistoryFor(RealmPeople(id).Culture, s.CivKey))
                s.Traditions[t.Id] = DemoYear;
        RefreshRoleBoosts();
    }

    /// <summary>The arm (a UnitRoles index) that did most of an army's fighting.</summary>
    static int MainArm(Army a)
    {
        var cat = UnitCatalog.Instance;
        var weight = new double[UnitRoles.Count];
        foreach (var r in a.Regiments)
            weight[cat[r.Type].Role] += r.Men / (double)cat[r.Type].Men * cat[r.Type].Might;
        return Array.IndexOf(weight, weight.Max());
    }

    /// <summary>The arms present in an army.</summary>
    static IEnumerable<int> ArmsOf(Army a) =>
        a.Regiments.Where(r => r.Men > 0).Select(r => UnitCatalog.Instance[r.Type].Role).Distinct();

    /// <summary>
    /// After a battle: the traditions of the arms that fought are used again;
    /// the winner's main arm counts toward a tradition; a crushing defeat
    /// breaks the loser's tradition of the arm that was routed.
    /// </summary>
    internal void TraditionsAfterBattle(BattleReport report, RealmState winner, Army winArmy, RealmState loser, Army loseArmy, List<ChronicleEvent> events)
    {
        var cat = TraditionCatalog.Instance;
        foreach (var (s, a) in new[] { (winner, winArmy), (loser, loseArmy) })
        {
            foreach (int arm in ArmsOf(a))
                foreach (var t in cat.OfArm(s, UnitRoles.Keys[arm]))
                    s.Traditions[t.Id] = DemoYear;
            // Traditions that shape generals are kept alive by any battle.
            foreach (var id in s.Traditions.Keys.ToList())
                if (cat[id] is { } t && t.Effects.Keys.All(k => k.StartsWith("skill_")))
                    s.Traditions[id] = DemoYear;
        }

        int main = MainArm(winArmy);
        if (++winner.ArmWins[main] >= TraditionCatalog.VictoriesToEarn && cat.EarnedFor(UnitRoles.Keys[main]) is { } earned
            && !winner.Traditions.ContainsKey(earned.Id))
        {
            winner.Traditions[earned.Id] = DemoYear;
            winner.ArmWins[main] = 0;
            RefreshRoleBoosts();
            events.Add(new ChronicleEvent(ChronicleKind.War, winner.RealmId,
                $"{RealmName(winner.RealmId)} earns a tradition: {earned.Name} ({UnitRoles.Names[main].ToLowerInvariant()} fight 10% better)."));
        }

        bool loserAttacked = report.AttackerRealm == loser.RealmId;
        int loserMen = loserAttacked ? report.AttackerMen : report.DefenderMen;
        int loserLost = loserAttacked ? report.AttackerLost : report.DefenderLost;
        int winnerLost = loserAttacked ? report.DefenderLost : report.AttackerLost;
        if (TraditionCatalog.Crushing(loserMen, loserLost, winnerLost))
        {
            int routed = MainArm(loseArmy);
            var broken = cat.OfArm(loser, UnitRoles.Keys[routed]).OrderBy(t => t.Earned ? 0 : 1).FirstOrDefault();
            if (broken != null)
            {
                loser.Traditions.Remove(broken.Id);
                loser.ArmWins[routed] = 0;
                RefreshRoleBoosts();
                events.Add(new ChronicleEvent(ChronicleKind.War, loser.RealmId,
                    $"After the disaster at {report.Place}, {RealmName(loser.RealmId)} loses a tradition: {broken.Name}."));
            }
        }
    }

    /// <summary>A city taken: siegecraft is used, and many cities taken earn its tradition.</summary>
    internal void TraditionsAfterCapture(RealmState attacker, List<ChronicleEvent> events)
    {
        var cat = TraditionCatalog.Instance;
        if (cat.EarnedFor("siege") is not { } siege)
            return;
        if (attacker.Traditions.ContainsKey(siege.Id))
        {
            attacker.Traditions[siege.Id] = DemoYear;
            return;
        }
        if (++attacker.CitiesTaken >= TraditionCatalog.CitiesToEarn)
        {
            attacker.Traditions[siege.Id] = DemoYear;
            attacker.CitiesTaken = 0;
            events.Add(new ChronicleEvent(ChronicleKind.War, attacker.RealmId,
                $"{RealmName(attacker.RealmId)} earns a tradition: {siege.Name} (sieges 15% faster)."));
        }
    }

    /// <summary>Once a year: traditions unused for fifty years are forgotten.</summary>
    internal List<ChronicleEvent> TraditionsYear()
    {
        var events = new List<ChronicleEvent>();
        var cat = TraditionCatalog.Instance;
        bool changed = false;
        foreach (var (id, s) in Game.Realms)
            foreach (var (tid, last) in s.Traditions.ToList())
                if (DemoYear - last > TraditionCatalog.FadeYears)
                {
                    s.Traditions.Remove(tid);
                    changed = true;
                    if (id == PlayerRealmId)
                        events.Add(new ChronicleEvent(ChronicleKind.War, id,
                            $"Fifty years without use: the tradition of {cat[tid]?.Name ?? tid} is forgotten."));
                }
        if (changed)
            RefreshRoleBoosts();
        return events;
    }

    /// <summary>Why a unit can't be made the realm's guard now, or null.</summary>
    public string? CanMakeGuard(Army army, Regiment reg)
    {
        var u = UnitCatalog.Instance[reg.Type];
        if (u.Guard)
            return "Already a guard.";
        if (u.Domain != Domain.Land)
            return "Only soldiers on land can be a guard.";
        if (reg.Xp < Guards.FamousXp)
            return $"Only famous veterans ({Guards.FamousXp:P0} experience) can be made the guard.";
        if (Guards.Count(PlayerState) >= Guards.Max)
            return $"You keep at most {Guards.Max} guard units.";
        return null;
    }

    /// <summary>Famous veterans become the realm's guard (Alexander's hypaspists became the Silver Shields).</summary>
    public bool MakeGuard(Army army, Regiment reg)
    {
        if (CanMakeGuard(army, reg) != null)
            return false;
        var guard = UnitCatalog.Instance.GuardFor(UnitCatalog.Instance[reg.Type].Role, CulturesOf(PlayerRealmId));
        if (guard == null)
            return false;
        reg.Type = guard.Index;
        return true;
    }
}
