using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Why they did that (your answers "Hover a realm to see its aims and
/// reasons" and "Three, with more on click"): what another realm is about,
/// and why, most pressing first. Under the diplomacy fog you see only what
/// your realm would know: nothing of a realm it doesn't know; of one it
/// knows roughly, only what all can see (its wars, its old grudges); of one
/// it knows well (neighbours, trading partners), its designs and its purse
/// too. Spies and envoys will add to this when they come.
/// </summary>
public partial class MapView
{
    /// <summary>One aim: what the realm is doing or wants, and why.</summary>
    public sealed record Aim(string Text, string Reason);

    /// <summary>The grudge weight from which an old wrong counts among a realm's aims.</summary>
    const double AimGrudge = 15;

    /// <summary>A realm's aims as your realm knows them, most pressing first; empty if it knows nothing of them.</summary>
    public List<Aim> AimsOf(int realmId)
    {
        var aims = new List<Aim>();
        var insight = InsightInto(realmId);
        if (insight == Insight.Unknown && realmId != PlayerRealmId)
            return aims;
        bool close = insight == Insight.Exact;
        var state = Game.Realm(realmId);
        int year = DemoYear;

        // Its wars: all can see those.
        foreach (var war in Game.Wars.Of(realmId).OrderBy(w => w.Supports != null).ThenBy(w => w.Since))
        {
            int enemy = war.Enemy(realmId);
            string them = RealmName(enemy);
            string why = war.Why != "" ? war.Why : (war.CasusBelli == "" ? "no pretext given" : Pretexts.ById(war.CasusBelli).Name);
            if (war.Supports is { } ours && (ours.Attacker == realmId || ours.Defender == realmId))
            {
                int foe = ours.Attacker == realmId ? ours.Defender : ours.Attacker;
                aims.Add(new Aim($"Fighting {them} too", $"{them} came into its war on the side of {RealmName(foe)}."));
                continue;
            }
            if (war.Supports is { } main)
            {
                int friend = main.Attacker == enemy ? main.Defender : main.Attacker;
                aims.Add(new Aim($"Fighting {them} beside {RealmName(friend)}",
                    Game.Treaties.OverlordOf(realmId) == friend ? $"{RealmName(friend)} is its overlord." : $"It was called in by its ally, {RealmName(friend)}."));
                continue;
            }
            double score = war.ScoreFor(realmId);
            string standing = score <= -30 ? $" It is losing, and would take peace." : score >= 30 ? " It is winning." : "";
            aims.Add(war.Attacker == realmId
                ? new Aim($"War on {them}", Sentence(why) + standing)
                : new Aim($"Holding off {them}", $"{them} attacked it. {Sentence(why)}" + standing));
        }

        // History's campaigns now due (designs: only for a realm your realm knows well).
        if (close)
            foreach (var goal in HistoryGoals.Where(g => g.Realm == state.CivKey && g.ActiveIn(year)))
            {
                int target = RealmOfCiv(goal.Target);
                if (target == 0 || Game.Wars.AtWar(realmId, target))
                    continue;
                string regions = string.Join(" and ", goal.Regions);
                aims.Add(new Aim($"Has designs on {RealmName(target)} in {regions}",
                    Sentence(goal.Why) + (Diplomacy.CanDeclare(Game, realmId, target, year) is not { } bar ? " It waits until it is strong enough."
                        : bar.StartsWith("A truce") ? $" It can't strike yet: a{bar[1..]}" : " A treaty binds it to them for now.")));
            }

        // Old wrongs it remembers.
        foreach (var ((holder, target), _) in Game.Grudges.Where(g => g.Key.Holder == realmId).OrderBy(g => g.Key.Target))
        {
            double weight = Game.GrudgeNow(holder, target, year);
            if (weight >= AimGrudge && !Game.Wars.AtWar(realmId, target) && Registry.Realms.ContainsKey(target))
                aims.Add(new Aim($"Bears a grudge against {RealmName(target)}",
                    "It remembers old wrongs: wars, raids and insults. Grudges fade, by half in thirty years."));
        }

        // Its purse (only for a realm your realm knows well).
        if (close)
        {
            var (tax, tribute) = Economy.Revenue(CensusOf(realmId), TaxRate.Normal);
            double income = tax * state.TaxReach + tribute;
            if (state.Debt > income)
                aims.Add(new Aim("Paying off its debts", "It owes more than a year's income, so its taxes are heavy."));
            else if (state.Treasury > BotRichYears * income && income > 0)
                aims.Add(new Aim("Building temples, palaces and games", "Its treasury holds more than three years' income, so it lightens its taxes and spends the rest, as rulers did in good times."));
            if (Game.Wars.Of(realmId).Any())
                aims.Add(new Aim("Raising troops", "At war, it spends its war chest on soldiers."));
        }

        if (aims.Count == 0)
            aims.Add(new Aim("Keeping the peace", close ? "No war of its own, and none of history's campaigns is due." : "It is at war with no one that your realm has heard of."));
        return aims;
    }

    static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";
}
