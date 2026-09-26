using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Diplomacy on the map (decisions "Playable 15, 16, 17, 19"): pretexts for
/// war, what realms think of each other, alliances and vassals, war
/// exhaustion, peace terms, and the offers other realms make.
/// </summary>
public partial class MapView
{
    readonly HashSet<(int, int)> _rivals = new();

    public bool Rivals(int a, int b) => _rivals.Contains(a < b ? (a, b) : (b, a));

    /// <summary>Treaties and rivalries in force in 300 BC (data/start_realms.json).</summary>
    void StartTreaties(JsonElement root)
    {
        _rivals.Clear();
        if (root.TryGetProperty("alliances", out var al))
            foreach (var pair in al.EnumerateArray())
            {
                int a = RealmOfCiv(pair[0].GetString()!), b = RealmOfCiv(pair[1].GetString()!);
                if (a != 0 && b != 0)
                    Game.Treaties.Add(new Treaty(TreatyKind.Alliance, a, b, DemoYear));
            }
        LoadRivals(root);
    }

    void LoadRivals(JsonElement root)
    {
        if (root.TryGetProperty("rivals", out var rv))
            foreach (var pair in rv.EnumerateArray())
            {
                int a = RealmOfCiv(pair[0].GetString()!), b = RealmOfCiv(pair[1].GetString()!);
                if (a != 0 && b != 0)
                    _rivals.Add(a < b ? (a, b) : (b, a));
            }
    }

    // --- Opinion ----------------------------------------------------------------------

    /// <summary>What realm `of` thinks of realm `about`, -100..100, with the reasons.</summary>
    public (double Value, List<string> Reasons) Opinion(int of, int about)
    {
        var reasons = new List<string>();
        double v = 0;
        void Add(double x, string why)
        {
            if (Math.Abs(x) < 0.5)
                return;
            v += x;
            reasons.Add($"{(x > 0 ? "+" : "")}{x:0} {why}");
        }
        var mine = RealmPeople(of);
        var theirs = RealmPeople(about);
        var kin = Cultures().Kin;
        if (mine.Culture != "" && theirs.Culture != "")
        {
            var rel = Loyalty.Relation(mine.Culture, theirs.Culture, kin);
            Add(rel == Loyalty.Kinship.Same ? 20 : rel == Loyalty.Kinship.Kin ? 10 : 0, "kindred people");
        }
        if (mine.Religion != "" && theirs.Religion != "")
            Add(mine.Religion == theirs.Religion ? 10 : -5, mine.Religion == theirs.Religion ? "the same gods" : "other gods");
        if (Game.Treaties.Allied(of, about))
            Add(40, "allies");
        if (Game.Treaties.OverlordOf(of) == about || Game.Treaties.OverlordOf(about) == of)
            Add(20, "overlord and vassal");
        if (Game.Wars.AtWar(of, about))
            Add(-60, "at war");
        else if (Game.Wars.TruceUntil(of, about, DemoYear) != null)
            Add(-20, "a recent war");
        if (Rivals(of, about))
            Add(-30, "old rivals");
        if (RoyalTie(of, about))
            Add(15, "their houses are joined by marriage");
        Add(-0.8 * Game.Realm(about).Aggression, "fear of their conquests");
        return (Math.Clamp(v, -100, 100), reasons);
    }

    /// <summary>Whether the two ruling houses are joined by a living marriage (rulers or their children).</summary>
    bool RoyalTie(int a, int b)
    {
        if (!Registry.Realms.TryGetValue(a, out var ra) || !Registry.Realms.TryGetValue(b, out var rb))
            return false;
        if (!Registry.Characters.TryGetValue(ra.RulerId, out var ca) || !Registry.Characters.TryGetValue(rb.RulerId, out var cb))
            return false;
        bool Married(Character x, int dynasty) => x.SpouseId >= 0 && Registry.Characters.TryGetValue(x.SpouseId, out var s)
            && s.IsAlive && s.DynastyId == dynasty;
        if (Married(ca, cb.DynastyId) || Married(cb, ca.DynastyId))
            return true;
        foreach (int child in ca.ChildrenIds)
            if (Registry.Characters.TryGetValue(child, out var c) && c.IsAlive && Married(c, cb.DynastyId))
                return true;
        foreach (int child in cb.ChildrenIds)
            if (Registry.Characters.TryGetValue(child, out var c) && c.IsAlive && Married(c, ca.DynastyId))
                return true;
        return false;
    }

    // --- Pretexts ---------------------------------------------------------------------

    /// <summary>The pretexts a realm has against another (always including none at all).</summary>
    public List<Pretext> PretextsAgainst(int attacker, int defender)
    {
        var list = new List<Pretext>();
        var year = DemoYear;
        string civ = Game.Realm(attacker).CivKey;
        if (HistoryGoals.Any(g => g.Realm == civ && g.ActiveIn(year) && RealmOfCiv(g.Target) == defender))
            list.Add(Pretexts.History);
        if (Game.Lost.TryGetValue(attacker, out var lost) && Provinces != null
            && lost.Any(kv => year - kv.Value <= 50 && Provinces.Provinces.TryGetValue(kv.Key, out var p) && p.RealmId == defender))
            list.Add(Pretexts.Reconquest);
        string mine = RealmPeople(attacker).Culture;
        if (mine != "" && Provinces != null && Provinces.Provinces.Values.Any(p => p.RealmId == defender && ProvinceStateOf(p.Id).Culture == mine)
            && RealmPeople(defender).Culture != mine)
            list.Add(Pretexts.Kin);
        bool neighbours = HasPretext(attacker, defender);
        if (neighbours)
        {
            list.Add(Pretexts.Border);
            if (Military.Might(Game.Realm(defender), Domain.Land) * 3 <= Military.Might(Game.Realm(attacker), Domain.Land))
                list.Add(Pretexts.Subjugation);
            var ra = RealmPeople(attacker).Religion;
            var rd = RealmPeople(defender).Religion;
            if (ra != "" && rd != "" && ra != rd)
                list.Add(Pretexts.Faith);
        }
        list.Add(Pretexts.None);
        return list;
    }

    /// <summary>
    /// Declares war for the player on a pretext; allies are called in on both
    /// sides. Returns the chronicle lines, or null if war can't be declared.
    /// </summary>
    public List<ChronicleEvent>? DeclareWar(int defender, string pretextId = "")
    {
        if (Diplomacy.CanDeclare(Game, PlayerRealmId, defender, DemoYear) != null)
            return null;
        var available = PretextsAgainst(PlayerRealmId, defender);
        var pretext = available.FirstOrDefault(p => p.Id == pretextId) ?? available[0];
        var events = StartWar(PlayerRealmId, defender, pretext);
        EmitSignal(SignalName.ConquestPlanChanged);
        return events;
    }

    /// <summary>Opens a war (anyone's): aggression, the war itself, and the allies called in.</summary>
    internal List<ChronicleEvent> StartWar(int attacker, int defender, Pretext pretext, string? why = null)
    {
        var events = new List<ChronicleEvent>();
        var war = Diplomacy.Declare(Game, attacker, defender, DemoYear, pretext != Pretexts.None, pretext.Id);
        var state = Game.Realm(attacker);
        state.Aggression = Math.Min(100, state.Aggression + pretext.AggressionOnDeclare);
        string text = $"{RealmName(attacker)} declares war on {RealmName(defender)}: {why ?? pretext.Name.ToLowerInvariant()}.";
        events.Add(new ChronicleEvent(ChronicleKind.War, attacker, text));
        events.Add(new ChronicleEvent(ChronicleKind.War, defender, text));
        events.AddRange(CallAllies(war));
        return events;
    }

    /// <summary>Allies, coalition members and vassals join a war on their side.</summary>
    List<ChronicleEvent> CallAllies(War war)
    {
        var events = new List<ChronicleEvent>();
        var key = (war.Attacker, war.Defender);
        var defenders = Game.Treaties.AlliesOf(war.Defender)
            .Concat(Game.Treaties.CoalitionAgainst(war.Attacker).Where(m => m != war.Defender
                && Game.Treaties.CoalitionAgainst(war.Attacker).Contains(war.Defender)))
            .Concat(Game.Treaties.VassalsOf(war.Defender))
            .Append(Game.Treaties.OverlordOf(war.Defender))
            .Where(r => r > 0 && r != war.Attacker && !Game.Wars.AtWar(r, war.Attacker)).Distinct().ToList();
        foreach (int ally in defenders)
        {
            if (Game.Treaties.Allied(ally, war.Attacker))
                continue;   // allied to both: stays out
            // As history would: a far-off ally sends its regrets, unless it is the player's own treaty.
            if (ally != PlayerRealmId && war.Attacker != PlayerRealmId && !HasPretext(ally, war.Attacker))
                continue;
            Game.Wars.Declare(war.Attacker, ally, DemoYear, true, "ally", key);
            string text = $"{RealmName(ally)} honours its treaty and joins the war against {RealmName(war.Attacker)}.";
            events.Add(new ChronicleEvent(ChronicleKind.War, ally, text));
            events.Add(new ChronicleEvent(ChronicleKind.War, war.Attacker, text));
        }
        foreach (int vassal in Game.Treaties.VassalsOf(war.Attacker).Where(v => !Game.Wars.AtWar(v, war.Defender)))
        {
            Game.Wars.Declare(vassal, war.Defender, DemoYear, true, "ally", key);
            string text = $"{RealmName(vassal)} marches with its overlord {RealmName(war.Attacker)}.";
            events.Add(new ChronicleEvent(ChronicleKind.War, vassal, text));
            events.Add(new ChronicleEvent(ChronicleKind.War, war.Defender, text));
        }
        return events;
    }

    // --- Treaties for the player ------------------------------------------------------

    /// <summary>Opinion needed for a realm to accept an alliance.</summary>
    public const double AllianceOpinion = 25;

    public string ProposeAlliance(int other)
    {
        if (Game.Wars.AtWar(PlayerRealmId, other))
            return "You are at war with them.";
        var (opinion, _) = Opinion(other, PlayerRealmId);
        if (opinion < AllianceOpinion)
            return $"{RealmName(other)} declines: they don't think well enough of you ({opinion:+0;-0;0}, need {AllianceOpinion:+0}).";
        Game.Treaties.Add(new Treaty(TreatyKind.Alliance, PlayerRealmId, other, DemoYear));
        return $"{RealmName(PlayerRealmId)} and {RealmName(other)} swear an alliance.";
    }

    public string BreakAlliance(int other)
    {
        Game.Treaties.Remove(TreatyKind.Alliance, PlayerRealmId, other);
        PlayerState.Aggression = Math.Min(100, PlayerState.Aggression + 5);
        return $"{RealmName(PlayerRealmId)} breaks its alliance with {RealmName(other)}.";
    }

    /// <summary>A far weaker realm that thinks well enough of you may accept you as overlord without a war.</summary>
    public string DemandSubmission(int other)
    {
        double mine = Military.Might(PlayerState, Domain.Land), theirs = Military.Might(Game.Realm(other), Domain.Land);
        var (opinion, _) = Opinion(other, PlayerRealmId);
        if (Game.Treaties.OverlordOf(other) != 0)
            return $"{RealmName(other)} already has an overlord.";
        if (mine < 3 * Math.Max(theirs, 1) || opinion < 0)
            return $"{RealmName(other)} refuses to bow: you need three times their might and their good opinion ({opinion:+0;-0;0}).";
        Game.Treaties.Add(new Treaty(TreatyKind.Vassal, PlayerRealmId, other, DemoYear));
        return $"{RealmName(other)} submits to {RealmName(PlayerRealmId)} as a vassal.";
    }

    public string ReleaseVassal(int other)
    {
        Game.Treaties.Remove(TreatyKind.Vassal, PlayerRealmId, other);
        return $"{RealmName(PlayerRealmId)} releases {RealmName(other)} from vassalage.";
    }

    // --- Peace ------------------------------------------------------------------------

    /// <summary>The war score a set of terms costs the loser.</summary>
    public double TermsCost(War war, int winner, PeaceTerms terms)
    {
        int loser = war.Enemy(winner);
        double people = Math.Max(CensusOf(loser).People, 1);
        double cost = 0;
        foreach (int prov in terms.Provinces)
        {
            bool besieged = Game.Sieges.Any(s => s.Attacker == winner && s.ProvinceId == prov && s.Progress >= 0.5);
            bool ours = Game.Lost.TryGetValue(winner, out var lost) && lost.ContainsKey(prov);
            cost += Diplomacy.ProvinceCost(ProvincePopulation(prov), people, besieged) * (ours ? 0.5 : 1);
        }
        if (terms.Tribute)
            cost += PeaceTerms.TributeCost;
        if (terms.Vassal)
            cost += PeaceTerms.VassalCost;
        return cost;
    }

    /// <summary>The player offers peace on terms; returns whether it was accepted and the chronicle line.</summary>
    public (bool Accepted, string Text) OfferPeace(int enemy, PeaceTerms terms)
    {
        var war = Game.Wars.Between(PlayerRealmId, enemy);
        if (war == null)
            return (false, "");
        double cost = TermsCost(war, PlayerRealmId, terms);
        if (!Diplomacy.AcceptsTerms(war, PlayerRealmId, DemoYear, cost))
            return (false, $"{RealmName(enemy)} refuses peace on those terms.");
        return (true, ApplyPeace(war, PlayerRealmId, terms));
    }

    /// <summary>Old two-button form: peace keeping what each holds, or with tribute.</summary>
    public (bool Accepted, string Text) OfferPeace(int enemy, bool demandTribute) =>
        OfferPeace(enemy, new PeaceTerms { Tribute = demandTribute });

    /// <summary>Makes peace on terms: provinces change hands, silver is paid, a vassal submits; a truce follows.</summary>
    internal string ApplyPeace(War war, int winner, PeaceTerms terms)
    {
        int loser = war.Enemy(winner);
        var parts = new List<string>();
        foreach (int prov in terms.Provinces)
            if (Provinces != null && Provinces.Provinces.TryGetValue(prov, out var p) && p.RealmId == loser)
            {
                Game.RecordLoss(loser, prov, DemoYear);
                Provinces.SetRealm(prov, winner, Grid);
                MarkChanged(prov);
                TerritoryChanged = true;
                parts.Add(p.Name);
            }
        double paid = 0;
        if (terms.Tribute)
        {
            var (tax, trib) = Economy.Revenue(CensusOf(loser), Game.Realm(loser).Tax);
            paid = Diplomacy.MakePeace(Game, war, winner, DemoYear, true, tax + trib);
        }
        else
            Game.Wars.MakePeace(war, DemoYear);
        if (terms.Vassal)
            Game.Treaties.Add(new Treaty(TreatyKind.Vassal, winner, loser, DemoYear));
        Game.Sieges.RemoveAll(s => (s.Attacker == winner && s.Owner == loser) || (s.Attacker == loser && s.Owner == winner));
        Game.Offers.RemoveAll(o => o.From == loser || o.From == winner);
        if (parts.Count > 0)
        {
            _census = null;
            SyncPopulationOwnership();
        }
        string text = $"Peace between {RealmName(winner)} and {RealmName(loser)}";
        var what = new List<string>();
        if (parts.Count > 0)
            what.Add($"{RealmName(loser)} cedes {string.Join(", ", parts)}");
        if (paid > 0)
            what.Add($"pays {Money(paid)}");
        if (terms.Vassal)
            what.Add($"{RealmName(loser)} becomes a vassal of {RealmName(winner)}");
        return text + (what.Count > 0 ? ": " + string.Join("; ", what) + "." : ". Each keeps what it holds.");
    }

    // --- Offers to the player ---------------------------------------------------------

    public string? AnswerOffer(Offer offer, bool accept)
    {
        Game.Offers.Remove(offer);
        if (!accept)
            return offer.Kind == "ransom" ? null : $"You turn down {RealmName(offer.From)}'s offer.";
        switch (offer.Kind)
        {
            case "peace":
            {
                var war = Game.Wars.Between(PlayerRealmId, offer.From);
                if (war == null)
                    return null;
                var terms = new PeaceTerms();
                terms.Provinces.AddRange(offer.Provinces);
                string text = ApplyPeace(war, PlayerRealmId, terms);
                if (offer.Silver > 0)
                {
                    PlayerState.Treasury += offer.Silver;
                    Game.Realm(offer.From).Treasury = Math.Max(0, Game.Realm(offer.From).Treasury - offer.Silver);
                    text += $" They pay {Money(offer.Silver)}.";
                }
                return text;
            }
            case "ransom":
            {
                Game.Sieges.RemoveAll(s => s.Attacker == PlayerRealmId && s.ProvinceId == offer.SiegeProvince);
                PlayerState.Treasury += offer.Silver;
                Game.Realm(offer.From).Treasury = Math.Max(0, Game.Realm(offer.From).Treasury - offer.Silver);
                string name = Provinces?.Provinces.TryGetValue(offer.SiegeProvince, out var p) == true ? p.Name : "the city";
                return $"You take {Money(offer.Silver)} from {name} and lift the siege.";
            }
            case "alliance":
                Game.Treaties.Add(new Treaty(TreatyKind.Alliance, PlayerRealmId, offer.From, DemoYear));
                return $"{RealmName(PlayerRealmId)} and {RealmName(offer.From)} swear an alliance.";
            default:
                return null;
        }
    }

    // --- The yearly step ------------------------------------------------------------

    /// <summary>
    /// The year's diplomacy: war exhaustion grows and may force peace;
    /// vassals pay tribute; aggression fades; realms that fear a conqueror
    /// band together; other realms make offers to the player.
    /// </summary>
    internal List<ChronicleEvent> DiplomacyYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        var census = RealmCensus();
        // Exhaustion.
        foreach (var war in Game.Wars.All.ToList())
        {
            foreach (int side in new[] { war.Attacker, war.Defender })
            {
                double pool = census.TryGetValue(side, out var c) ? Math.Max(Economy.SustainableManpower(c, Game.Realm(side).ManpowerMultiplier), 1000) : 1000;
                double lost = side == war.Attacker ? war.AttackerLosses - war.CountedAttackerLosses : war.DefenderLosses - war.CountedDefenderLosses;
                double besieged = Game.Sieges.Count(s => s.Owner == side && s.Attacker == war.Enemy(side));
                war.AddExhaustion(side, Diplomacy.ExhaustionPerYear + Diplomacy.ExhaustionPerManpowerLost * lost / pool + 2 * besieged);
            }
            war.CountedAttackerLosses = war.AttackerLosses;
            war.CountedDefenderLosses = war.DefenderLosses;
            if (war.Supports != null)
                continue;
            // Peace forced by exhaustion.
            int? spent = war.AttackerExhaustion >= Diplomacy.ForcedPeace ? war.Attacker
                : war.DefenderExhaustion >= Diplomacy.ForcedPeace ? war.Defender : null;
            if (spent is int tired)
            {
                int other = war.Enemy(tired);
                var terms = new PeaceTerms { Tribute = war.ScoreFor(other) >= Diplomacy.TributeScore };
                foreach (var s in Game.Sieges.Where(s => s.Attacker == other && s.Owner == tired && s.Progress >= 0.5 && s.ProvinceId != 0))
                    terms.Provinces.Add(s.ProvinceId);
                string text = $"{RealmName(tired)} can fight no more. " + ApplyPeace(war, other, terms);
                events.Add(new ChronicleEvent(ChronicleKind.Peace, tired, text));
                events.Add(new ChronicleEvent(ChronicleKind.Peace, other, text));
            }
        }
        // Vassals' tribute; aggression fades.
        foreach (var s in Game.Realms.Values)
        {
            s.LastVassalTribute = 0;
            s.Aggression = Math.Max(0, s.Aggression - Pretexts.AggressionDecay);
        }
        foreach (var t in Game.Treaties.All.Where(t => t.Kind == TreatyKind.Vassal).ToList())
        {
            if (!census.TryGetValue(t.B, out var vc))
                continue;
            var (tax, trib) = Economy.Revenue(vc, Game.Realm(t.B).Tax);
            double pay = (tax + trib) * Treaties.VassalTribute;
            Game.Realm(t.B).Treasury -= pay;
            Game.Realm(t.B).LastVassalTribute -= pay;
            Game.Realm(t.A).Treasury += pay;
            Game.Realm(t.A).LastVassalTribute += pay;
        }
        // Treaties with realms that are gone.
        Game.Treaties.RemoveWhere(t => !census.ContainsKey(t.A) || !census.ContainsKey(t.B));
        events.AddRange(Coalitions(rng, census));
        events.AddRange(OffersToPlayer(rng, census));
        return events;
    }

    /// <summary>Realms that fear a conqueror band together; a strong enough coalition may strike first.</summary>
    List<ChronicleEvent> Coalitions(Random rng, Dictionary<int, RealmCensus> census)
    {
        var events = new List<ChronicleEvent>();
        foreach (var (id, s) in Game.Realms)
        {
            if (!census.ContainsKey(id))
                continue;
            if (s.Aggression < Pretexts.CoalitionAggression)
            {
                Game.Treaties.RemoveWhere(t => t.Kind == TreatyKind.Coalition && t.Against == id && s.Aggression < Pretexts.CoalitionAggression / 2);
                continue;
            }
            var afraid = census.Keys.Where(r => r != id && Game.Treaties.OverlordOf(r) != id && !Game.Treaties.Allied(r, id)
                    && Opinion(r, id).Value <= -20 && HasPretext(r, id)).ToList();
            if (afraid.Count < 2)
                continue;
            bool isNew = !Game.Treaties.CoalitionAgainst(id).Any();
            for (int i = 0; i < afraid.Count; i++)
                for (int j = i + 1; j < afraid.Count; j++)
                    Game.Treaties.Add(new Treaty(TreatyKind.Coalition, afraid[i], afraid[j], DemoYear, id));
            if (isNew)
            {
                string text = $"Fearing {RealmName(id)}'s conquests, {string.Join(", ", afraid.Select(RealmName))} band together against it.";
                events.Add(new ChronicleEvent(ChronicleKind.War, id, text));
            }
            // A coalition well stronger than its enemy strikes.
            double theirs = afraid.Sum(r => Military.Might(Game.Realm(r), Domain.Land));
            double conqueror = Military.Might(s, Domain.Land);
            int leader = afraid.OrderByDescending(r => Military.Might(Game.Realm(r), Domain.Land)).First();
            if (theirs > 1.5 * conqueror && s.Aggression >= 70 && leader != PlayerRealmId && !Game.Wars.AtWar(leader, id)
                && Diplomacy.CanDeclare(Game, leader, id, DemoYear) == null && rng.NextDouble() < 0.3)
                events.AddRange(StartWar(leader, id, Pretexts.Border, $"the coalition against {RealmName(id)} strikes"));
        }
        return events;
    }

    /// <summary>Beaten enemies sue for peace with appeasement; besieged cities offer ransom; friends offer alliance.</summary>
    List<ChronicleEvent> OffersToPlayer(Random rng, Dictionary<int, RealmCensus> census)
    {
        var events = new List<ChronicleEvent>();
        int me = PlayerRealmId;
        Game.Offers.RemoveAll(o => DemoYear - o.Year > 2 || !census.ContainsKey(o.From)
            || (o.Kind is "peace" or "ransom" && !Game.Wars.AtWar(me, o.From)));
        foreach (var war in Game.Wars.Of(me).Where(w => w.Supports == null).ToList())
        {
            int enemy = war.Enemy(me);
            if (Game.Offers.Any(o => o.From == enemy && o.Kind == "peace"))
                continue;
            double score = war.ScoreFor(enemy);
            if (score > -25 && war.ExhaustionFor(enemy) < 60)
                continue;
            // Appeasement (decision "Playable 16": when the attacker is the player): silver, and the places already falling.
            var es = Game.Realm(enemy);
            var (tax, trib) = census.TryGetValue(enemy, out var ec) ? Economy.Revenue(ec, es.Tax) : (0, 0);
            var offer = new Offer
            {
                Kind = "peace", From = enemy, Year = DemoYear,
                Silver = Math.Round(Math.Min(es.Treasury * 0.4, (tax + trib) * 0.8)),
                Text = $"{RealmName(enemy)} sues for peace",
            };
            foreach (var s in Game.Sieges.Where(s => s.Attacker == me && s.Owner == enemy && s.Progress >= 0.5 && s.ProvinceId != 0))
                offer.Provinces.Add(s.ProvinceId);
            Game.Offers.Add(offer);
            events.Add(new ChronicleEvent(ChronicleKind.Peace, me,
                $"{RealmName(enemy)} sues for peace" + (offer.Silver > 0 ? $", offering {Money(offer.Silver)}" : "") +
                (offer.Provinces.Count > 0 ? " and the places you besiege" : "") + ". (Diplomacy: accept or fight on.)"));
        }
        // A city under siege may buy you off.
        foreach (var s in Game.Sieges.Where(s => s.Attacker == me && s.Owner > 0 && s.ProvinceId != 0 && s.Progress >= 0.3 && s.Progress < 0.9))
        {
            if (Game.Offers.Any(o => o.Kind == "ransom" && o.SiegeProvince == s.ProvinceId) || rng.NextDouble() > 0.3)
                continue;
            double ransom = Math.Round(s.People / 6000.0 * 20);   // about 20 drachmae a head
            if (ransom < 10 || Game.Realm(s.Owner).Treasury < ransom)
                continue;
            Game.Offers.Add(new Offer { Kind = "ransom", From = s.Owner, Year = DemoYear, Silver = ransom, SiegeProvince = s.ProvinceId,
                Text = $"The people of {s.Name} offer {Money(ransom)} if you lift the siege" });
            events.Add(new ChronicleEvent(ChronicleKind.War, me, $"The people of {s.Name} offer {Money(ransom)} if you lift the siege. (Diplomacy.)"));
        }
        // Friends facing a common enemy offer an alliance.
        foreach (int other in census.Keys.Where(r => r != me && !Game.Treaties.Allied(r, me) && !Game.Wars.AtWar(r, me)))
        {
            if (Game.Offers.Any(o => o.From == other && o.Kind == "alliance") || rng.NextDouble() > 0.05)
                continue;
            bool commonEnemy = Game.Wars.EnemiesOf(other).Any(e => Game.Wars.AtWar(me, e));
            if (Opinion(other, me).Value >= AllianceOpinion + 10 && (commonEnemy || rng.NextDouble() < 0.2))
            {
                Game.Offers.Add(new Offer { Kind = "alliance", From = other, Year = DemoYear, Text = $"{RealmName(other)} proposes an alliance" });
                events.Add(new ChronicleEvent(ChronicleKind.Peace, me, $"{RealmName(other)} proposes an alliance. (Diplomacy.)"));
            }
        }
        return events;
    }
}
