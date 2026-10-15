using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Military reforms (War: armies, units and generals): offered to the
/// player's realm when history's cause is present and the technology known;
/// taken by other realms in history's year. See Game/Reforms.cs and
/// data/military_reforms.json.
/// </summary>
public partial class MapView
{
    /// <summary>Whether a reform's cause from history is present for a realm now.</summary>
    public bool ReformCause(int realmId, ReformDef d)
    {
        var s = Game.Realm(realmId);
        int recent = DemoYear - ReformCatalog.RecentYears;
        int RealmOf(string civ) => CivRealmIds.GetValueOrDefault(civ, -1);
        bool ManpowerLow() =>
            RealmCensus().TryGetValue(realmId, out var c) && s.Manpower < 0.25 * Economy.SustainableManpower(c, s.ManpowerMultiplier);
        bool Defeat() => s.LastHeavyDefeat != 0 && s.LastHeavyDefeat >= recent;
        bool OwnProvince(Func<ProvinceState, bool> test) =>
            Provinces?.Provinces.Values.Any(p => p.RealmId == realmId && test(ProvinceStateOf(p.Id))) == true;
        return d.Cause switch
        {
            "at_war_years" => Game.Wars.Of(realmId).Any(w => DemoYear - w.Since >= d.CauseYears),
            "at_war_with" => Game.Wars.AtWar(realmId, RealmOf(d.CauseArg)),
            "manpower_low" => ManpowerLow(),
            "defeat" => Defeat(),
            "defeat_by" => s.DefeatedBy.TryGetValue(RealmOf(d.CauseArg), out int y) && y >= recent,
            "defeat_or_manpower" => Defeat() || ManpowerLow(),
            "unrest_culture" => OwnProvince(ps => ps.Culture == d.CauseArg && ps.Unrest > 0.3),
            "pirates" => ActiveHavens().Any(),
            "after_civil_war" => s.LastMutinyYear != 0 && DemoYear - s.LastMutinyYear <= 20,
            "holds_culture" => OwnProvince(ps => ps.Culture == d.CauseArg),
            "fought_culture" => s.FoughtCultures.TryGetValue(d.CauseArg, out int f) && f >= recent,
            "no_war_years" => YearsAtPeace(realmId) >= d.CauseYears,
            _ => false,
        };
    }

    /// <summary>Why the player can't begin this reform now, or null.</summary>
    public string? CanEnactReform(string id)
    {
        var s = PlayerState;
        var d = ReformCatalog.Instance[id];
        if (d == null || !s.ReformsOffered.Contains(id))
            return "Not offered.";
        if (s.ReformsPending.ContainsKey(id))
            return "Already under way.";
        if (s.Treasury < d.Cost)
            return $"Needs {d.Cost:0} talents.";
        return null;
    }

    /// <summary>Begins a reform: the silver now, the rival's anger now, the effect in its years.</summary>
    public bool EnactReform(string id)
    {
        if (CanEnactReform(id) != null)
            return false;
        var d = ReformCatalog.Instance[id]!;
        var s = PlayerState;
        s.Treasury -= d.Cost;
        s.RivalStrength = Math.Clamp(s.RivalStrength + d.Rival, 0, 100);
        s.ReformsPending[id] = d.Years;
        return true;
    }

    /// <summary>
    /// Once a year: reforms under way come into force; the player is offered
    /// those whose cause is present; other realms take theirs in history's year.
    /// </summary>
    internal List<ChronicleEvent> ReformsYear()
    {
        var events = new List<ChronicleEvent>();
        var cat = ReformCatalog.Instance;
        bool changed = false;
        foreach (var (id, s) in Game.Realms)
            foreach (var (rid, left) in s.ReformsPending.ToList())
            {
                if (left > 1)
                {
                    s.ReformsPending[rid] = left - 1;
                    continue;
                }
                if (cat[rid] is not { } d)
                    continue;
                ReformCatalog.Enact(s, d, DemoYear);
                changed = true;
                if (id == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.War, id, $"The reform is done: {d.Name}. {d.Text}"));
            }

        foreach (var d in cat.All)
        {
            if (!CivRealmIds.TryGetValue(d.Civ, out int realmId) || !Game.Realms.TryGetValue(realmId, out var s))
                continue;
            if (s.Reforms.ContainsKey(d.Id) || s.ReformsPending.ContainsKey(d.Id))
                continue;
            if (realmId == PlayerRealmId)
            {
                if (s.ReformsOffered.Contains(d.Id) || (d.Tech != null && !s.Techs.Contains(d.Tech)) || !ReformCause(realmId, d))
                    continue;
                s.ReformsOffered.Add(d.Id);
                events.Add(new ChronicleEvent(ChronicleKind.War, realmId,
                    $"A reform is proposed: {d.Name}. {d.Text} (See the Armies panel: {d.Cost:0} talents, {d.Years} years.)"));
            }
            else if (DemoYear >= d.Year)
            {
                ReformCatalog.Enact(s, d, DemoYear);   // history's own year
                changed = true;
            }
        }
        if (changed)
            RefreshRoleBoosts();
        return events;
    }

    /// <summary>After a battle: what history's causes remember (heavy defeats, who beat whom, which peoples were fought).</summary>
    void RememberBattle(BattleReport report, RealmState winner, RealmState loser)
    {
        bool loserAttacked = report.AttackerRealm == loser.RealmId;
        int lost = loserAttacked ? report.AttackerLost : report.DefenderLost;
        int men = loserAttacked ? report.AttackerMen : report.DefenderMen;
        int winnerLost = loserAttacked ? report.DefenderLost : report.AttackerLost;
        if (lost >= ReformCatalog.HeavyDefeatDead || TraditionCatalog.Crushing(men, lost, winnerLost))
            loser.LastHeavyDefeat = DemoYear;
        loser.DefeatedBy[winner.RealmId] = DemoYear;
        string wc = RealmPeople(winner.RealmId).Culture, lc = RealmPeople(loser.RealmId).Culture;
        if (lc != "")
            winner.FoughtCultures[lc] = DemoYear;
        if (wc != "")
            loser.FoughtCultures[wc] = DemoYear;
    }
}
