using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>One of history's campaigns (data/history_goals.json).</summary>
public sealed record HistoryGoal(string Realm, string Target, string[] Regions, int From, int To, string Why)
{
    public bool ActiveIn(int year) => year >= From && year <= To;
}

/// <summary>
/// The other realms act (decisions "Playable 18" and "19", suggested
/// options): each year a realm keeps normal taxes, spends part of its
/// income on troops, and wages history's campaigns when their years come,
/// pressing into the named regions; at war with the player it fights back,
/// a much stronger neighbour may attack a weak player, and losing realms
/// make peace. The same conquest rules as the player's decide every fight.
/// </summary>
public partial class MapView
{
    public const string HistoryGoalsPath = "res://data/history_goals.json";

    /// <summary>Most targets a realm attacks in a year, and the least chance it accepts.</summary>
    public const int BotMaxTargets = 3;
    public const double BotMinChance = 0.45;
    /// <summary>At war, or with silver to spare, a realm spends up to this share of its income on its army.</summary>
    public const double BotWarArmyShare = 0.9;
    /// <summary>A realm opens a historical war only with at least this share of the target's strength.</summary>
    public const double BotMinStrengthToDeclare = 0.5;
    /// <summary>A yearly chance that a far stronger neighbour attacks a weak player.</summary>
    public const double BotOpportunism = 0.03;

    List<HistoryGoal>? _goals;

    public IReadOnlyList<HistoryGoal> HistoryGoals
    {
        get
        {
            if (_goals != null)
                return _goals;
            _goals = new List<HistoryGoal>();
            if (!Godot.FileAccess.FileExists(HistoryGoalsPath))
                return _goals;
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(HistoryGoalsPath));
            foreach (var g in doc.RootElement.GetProperty("goals").EnumerateArray())
                _goals.Add(new HistoryGoal(g.GetProperty("realm").GetString()!, g.GetProperty("target").GetString()!,
                    g.GetProperty("regions").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                    g.GetProperty("from").GetInt32(), g.GetProperty("to").GetInt32(), g.GetProperty("why").GetString()!));
            return _goals;
        }
    }

    public int RealmOfCiv(string key) => Game.Realms.Values.FirstOrDefault(r => r.CivKey == key)?.RealmId ?? 0;

    /// <summary>People, region and owner of every province, counted in one pass over the nodes.</summary>
    Dictionary<int, (int Owner, int Region, double People, int Node)> ProvinceSummaries()
    {
        var result = new Dictionary<int, (int, int, double, int)>();
        if (Provinces == null || Population == null)
            return result;
        var people = new Dictionary<int, double>();
        foreach (int node in Population.LandNodes)
        {
            int nx = node % Population.Width, ny = node / Population.Width;
            int cx = (int)((nx + 0.5) * GridWidth / Population.Width), cy = (int)((ny + 0.5) * GridHeight / Population.Height);
            int prov = Provinces.Cells[cy * GridWidth + cx];
            if (prov != ProvinceMap.None)
                people[prov] = people.GetValueOrDefault(prov) + Population.Pop[node];
        }
        foreach (var p in Provinces.Provinces.Values)
        {
            int node = Population.NodeAtCell((int)p.LabelCell.X, (int)p.LabelCell.Y, GridWidth, GridHeight);
            result[p.Id] = (p.RealmId, Population.RegionOf(node), people.GetValueOrDefault(p.Id), node);
        }
        return result;
    }

    /// <summary>Every other realm's year: war and peace, recruiting, then all their attacks fought out together.</summary>
    internal List<ChronicleEvent> BotsYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Population == null || Provinces == null)
            return events;
        var census = RealmCensus();
        var bots = Registry.Realms.Values.Where(r => r.Id != PlayerRealmId && census.ContainsKey(r.Id)).ToList();
        int year = DemoYear;

        // War and peace.
        foreach (var realm in bots)
        {
            var state = Game.Realm(realm.Id);
            var (rtax, rtrib) = Economy.Revenue(census[realm.Id], TaxRate.Normal);
            // Rich realms remit taxes, as rulers did in good times.
            state.Tax = state.Treasury > 5 * (rtax + rtrib) ? TaxRate.Low : TaxRate.Normal;
            foreach (var goal in HistoryGoals.Where(g => g.Realm == state.CivKey && g.ActiveIn(year)))
            {
                int target = RealmOfCiv(goal.Target);
                if (target == 0 || !census.ContainsKey(target) || Diplomacy.CanDeclare(Game, realm.Id, target, year) != null)
                    continue;
                // History pulls, it doesn't force suicide: too weak, it builds up and tries later in the window.
                if (Military.Might(state, Domain.Land) < BotMinStrengthToDeclare * Military.Might(Game.Realm(target), Domain.Land))
                    continue;
                Diplomacy.Declare(Game, realm.Id, target, year, pretext: true);
                events.Add(new ChronicleEvent(ChronicleKind.War, target,
                    $"{realm.Name} declares war on {RealmName(target)}. {goal.Why}"));
            }
            // A far stronger neighbour may fall on a weak player.
            int player = PlayerRealmId;
            if (player != 0 && census.ContainsKey(player) && !Game.Wars.AtWar(realm.Id, player)
                && Diplomacy.CanDeclare(Game, realm.Id, player, year) == null
                && Military.Might(state, Domain.Land) > 2.5 * Math.Max(Military.Might(Game.Realm(player), Domain.Land), 1)
                && !Game.Wars.Of(realm.Id).Any()
                && rng.NextDouble() < BotOpportunism && HasPretext(realm.Id, player))
            {
                Diplomacy.Declare(Game, realm.Id, player, year, pretext: true);
                events.Add(new ChronicleEvent(ChronicleKind.War, player,
                    $"{realm.Name} sees your weakness and declares war on {RealmName(player)}."));
            }
        }
        events.AddRange(BotsMakePeace(census));

        // Recruiting and attacks.
        var provinces = ProvinceSummaries();
        var allTargets = new List<ConquestTarget>();
        foreach (var realm in bots)
        {
            var state = Game.Realm(realm.Id);
            var wars = Game.Wars.Of(realm.Id).ToList();
            BotRecruit(realm, state, census[realm.Id], needsFleet: false);
            if (wars.Count == 0)
                continue;
            var (reach, bySea) = ComputeReach(realm.Id);
            var candidates = new List<ConquestTarget>();
            foreach (var war in wars)
            {
                int enemy = war.Enemy(realm.Id);
                bool vsPlayer = enemy == PlayerRealmId;
                // Content: an opportunist well ahead of the player stops pressing.
                if (vsPlayer && war.ScoreFor(realm.Id) >= 50)
                    continue;
                // Both sides of a historical war fight over the contested regions,
                // not across each other's whole realms; against the player, anywhere.
                string attackerCiv = Game.Realm(war.Attacker).CivKey;
                var goal = vsPlayer ? null : HistoryGoals.FirstOrDefault(g => g.Realm == attackerCiv && g.ActiveIn(year)
                    && RealmOfCiv(g.Target) == war.Defender)
                    ?? HistoryGoals.Where(g => g.Realm == attackerCiv && RealmOfCiv(g.Target) == war.Defender)
                        .OrderByDescending(g => g.From).FirstOrDefault();
                var regions = goal == null ? null : new HashSet<int>(goal.Regions
                    .Select(n => Population.Regions.FirstOrDefault(r => r.Name == n)?.Id ?? 0).Where(id => id != 0));
                bool defending = !vsPlayer && war.Defender == realm.Id;
                if (defending)
                {
                    // The side attacked in a historical war retakes what it lost; it
                    // doesn't march on its attacker's homeland.
                    var lost = new HashSet<int>(war.LostBy(realm.Id));
                    candidates.AddRange(BotCandidates(realm.Id, enemy, regions, reach, bySea, provinces)
                        .Where(t => t.ProvinceId != 0 && lost.Contains(t.ProvinceId)));
                }
                else
                    candidates.AddRange(BotCandidates(realm.Id, enemy, regions, reach, bySea, provinces));
            }
            foreach (var t in candidates)
                t.Problem = Conquest.CheckTarget(t, Game, RealmName(t.Owner));
            if (candidates.Any(t => t.BySea && t.Problem != null))
                BotRecruit(realm, state, census[realm.Id], needsFleet: true);
            var usable = candidates.Where(t => t.Problem == null).ToList();
            if (usable.Count == 0)
                continue;
            // Estimate with the army split over the best few, and keep those worth the risk.
            var chosen = usable.OrderByDescending(t => t.People).Take(BotMaxTargets * 2).ToList();
            Conquest.Estimate(chosen, Game);
            chosen = chosen.OrderByDescending(t => t.Chance).Take(BotMaxTargets).ToList();
            Conquest.Estimate(chosen, Game);
            allTargets.AddRange(chosen.Where(t => t.Chance >= BotMinChance));
        }
        events.AddRange(ResolveConquests(allTargets, rng));
        return events;
    }

    /// <summary>What a realm could attack of an enemy's: its provinces, and its unorganized land, within reach (and the goal's regions).</summary>
    List<ConquestTarget> BotCandidates(int attacker, int enemy, HashSet<int>? regions, bool[] reach, bool[] bySea,
        Dictionary<int, (int Owner, int Region, double People, int Node)> provinces)
    {
        var list = new List<ConquestTarget>();
        foreach (var (id, p) in provinces)
        {
            if (p.Owner != enemy || !reach[p.Node] || (regions != null && !regions.Contains(p.Region)))
                continue;
            list.Add(new ConquestTarget
            {
                Attacker = attacker, Owner = enemy, ProvinceId = id, People = p.People, BySea = bySea[p.Node],
                Name = $"{Provinces!.Provinces[id].Name} ({RealmName(enemy)})",
            });
        }
        // Unorganized land (the tribal peoples have no provinces): the most
        // populous reachable stretch, up to a few dozen nodes.
        var pop = Population!;
        var nodes = pop.LandNodes.Where(i => pop.NodeOwner[i] == enemy && reach[i]
                && (regions == null || regions.Contains(pop.RegionOf(i))))
            .OrderByDescending(i => pop.Pop[i]).Take(30).ToList();
        if (nodes.Count > 0)
        {
            var t = new ConquestTarget
            {
                Attacker = attacker, Owner = enemy, BySea = nodes.All(i => bySea[i]),
                Name = $"Unorganized land of {RealmName(enemy)}",
            };
            int cw = GridWidth / pop.Width + 1, ch = GridHeight / pop.Height + 1;
            foreach (int node in nodes)
            {
                int nx = node % pop.Width, ny = node / pop.Width;
                int x0 = nx * GridWidth / pop.Width, y0 = ny * GridHeight / pop.Height;
                bool any = false;
                for (int y = y0; y < Math.Min(y0 + ch, GridHeight); y++)
                    for (int x = x0; x < Math.Min(x0 + cw, GridWidth); x++)
                    {
                        int idx = y * GridWidth + x;
                        if (Grid.Cells[idx] != enemy)
                            continue;
                        int prov = Provinces?.Cells[idx] ?? 0;
                        if (prov != 0 && Provinces!.Provinces.TryGetValue(prov, out var owned) && owned.RealmId == enemy)
                            continue;   // organized: taken as a province instead
                        t.Cells.Add(idx);
                        any = true;
                    }
                if (any)
                    t.People += pop.Pop[node];
            }
            if (t.Cells.Count > 0)
                list.Add(t);
        }
        return list;
    }

    /// <summary>Recruits up to the army a realm can pay for; builds ships when a campaign needs to cross the sea.</summary>
    /// <summary>With this many years' income saved, a realm lowers its taxes.</summary>
    const double BotRichYears = 3;
    /// <summary>At war, a realm spends its savings over about this many years.</summary>
    const double BotWarChestYears = 10;
    /// <summary>Share of a rich realm's savings above BotRichYears spent each year on building and largesse.</summary>
    const double BotLargesse = 0.2;

    void BotRecruit(Realm realm, RealmState s, RealmCensus c, bool needsFleet)
    {
        // Rulers didn't hoard without end: a full treasury lightens the taxes, debt raises them.
        var (normalTax, normalTribute) = Economy.Revenue(c, TaxRate.Normal);
        double normalIncome = normalTax + normalTribute;
        s.Tax = s.Debt > normalIncome ? TaxRate.Heavy
            : s.Treasury > BotRichYears * normalIncome ? TaxRate.Low : TaxRate.Normal;
        var (tax, tribute) = Economy.Revenue(c, s.Tax);
        double income = tax + tribute;
        // Beyond that, the silver goes on temples, palaces, games and gifts, as it did.
        double excess = s.Treasury - BotRichYears * normalIncome;
        if (excess > 0)
            s.Treasury -= BotLargesse * excess;
        bool atWar = Game.Wars.Of(realm.Id).Any();
        double share = atWar || s.Treasury > 2 * income ? Math.Max(BotWarArmyShare, s.ArmyShare) : s.ArmyShare;
        double budget = share * income - Economy.AdminPerProvince * c.Provinces;
        if (atWar)
            budget += s.Treasury / BotWarChestYears;   // a war chest is spent in war
        var culture = realm.Culture;
        int[] mix = culture == Culture.Scythian
            ? new[] { UnitTypes.HorseArchers, UnitTypes.HorseArchers, UnitTypes.Cavalry, UnitTypes.LightInfantry }
            : culture == Culture.Nabataean
            ? new[] { UnitTypes.Cavalry, UnitTypes.Cavalry, UnitTypes.Archers, UnitTypes.LightInfantry }
            : new[] { UnitTypes.HeavyInfantry, UnitTypes.HeavyInfantry, UnitTypes.Cavalry, UnitTypes.LightInfantry, UnitTypes.Archers };
        if (needsFleet && c.Coastal)
            mix = new[] { UnitTypes.Warships, UnitTypes.Warships };
        for (int k = 0; k < 3; k++)
        {
            int type = mix[(s.Units.Sum() + k) % mix.Length];
            var u = UnitTypes.All[type];
            if (Economy.Upkeep(s) + u.Upkeep * s.UpkeepShare > budget || s.Treasury < u.Raise * 2)
                break;
            if (!Military.Recruit(s, c, culture, type, s.ElephantSource))
                break;
        }
    }

    /// <summary>Wars between other realms end when history's years are over and a side is losing or tired; beaten realms pay tribute.</summary>
    List<ChronicleEvent> BotsMakePeace(Dictionary<int, RealmCensus> census)
    {
        var events = new List<ChronicleEvent>();
        int year = DemoYear;
        foreach (var war in Game.Wars.All.ToList())
        {
            if (war.Involves(PlayerRealmId))
            {
                // A beaten enemy of the player sues for peace.
                int enemy = war.Enemy(PlayerRealmId);
                if (war.ScoreFor(enemy) <= -40 && !Game.PeaceOffers.Contains(enemy))
                {
                    Game.PeaceOffers.Add(enemy);
                    events.Add(new ChronicleEvent(ChronicleKind.Peace, PlayerRealmId,
                        $"{RealmName(enemy)} sues for peace. (Diplomacy: accept or fight on.)"));
                }
                continue;
            }
            var attacker = Game.Realm(war.Attacker);
            bool goalOver = !HistoryGoals.Any(g => g.Realm == attacker.CivKey && g.ActiveIn(year)
                && RealmOfCiv(g.Target) == war.Defender);
            int years = year - war.Since;
            bool loserBeaten = Math.Abs(war.Score) >= 50;
            if (!(goalOver && years >= 2) && !loserBeaten && years < 25)
                continue;
            int winner = war.Score >= 0 ? war.Attacker : war.Defender;
            int loser = war.Enemy(winner);
            bool tribute = Math.Abs(war.Score) >= Diplomacy.TributeScore;
            var (tax, trib) = census.TryGetValue(loser, out var lc) ? Economy.Revenue(lc, Game.Realm(loser).Tax) : (0, 0);
            double paid = Diplomacy.MakePeace(Game, war, winner, year, tribute, tax + trib);
            events.Add(new ChronicleEvent(ChronicleKind.Peace, winner,
                $"Peace between {RealmName(war.Attacker)} and {RealmName(war.Defender)}" +
                (paid > 0 ? $": {RealmName(loser)} pays {paid:N0} talents." : ".")));
        }
        return events;
    }

    /// <summary>The player accepts an enemy's offer of peace: each keeps what it holds.</summary>
    public string? AcceptPeaceOffer(int enemy)
    {
        var war = Game.Wars.Between(PlayerRealmId, enemy);
        Game.PeaceOffers.Remove(enemy);
        if (war == null)
            return null;
        Diplomacy.MakePeace(Game, war, PlayerRealmId, DemoYear, false, 0);
        return $"Peace between {RealmName(PlayerRealmId)} and {RealmName(enemy)}. Each keeps what it holds.";
    }
}
