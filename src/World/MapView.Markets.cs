using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Markets, merchants, price limits, embargoes, trade treaties and pirates
/// (the Ledger topic "Trade"): the great markets set prices for the land
/// nearest them; merchants you send to a market take a cut of its trade;
/// an edict can limit grain prices at the cost of shortages and black
/// markets; embargoes cut trade and treaties open it; pirate havens raid
/// shipping and coasts until suppressed.
/// </summary>
public partial class MapView
{
    /// <summary>Each merchant takes this share of its market's trade, shared with the other merchants there.</summary>
    public const double MerchantCut = 0.02;
    /// <summary>A grain price this many times last year's at a market is a shock worth a chronicle line.</summary>
    public const double ShockRatio = 1.4;
    /// <summary>Pirates cut this share of the customs of a realm whose sea trade lies in their reach (all of it).</summary>
    public const double PiracyCustoms = 0.3;
    /// <summary>Each year pirates carry off this share of the people of coasts in their reach.</summary>
    public const double RaidLoss = 0.002;
    /// <summary>Warships within this distance of a haven patrol it; this many months of patrols suppress it.</summary>
    public const double PatrolKm = 200;
    public const int PatrolMonthsToSuppress = 6;
    /// <summary>A shortage under a price limit adds this much unrest (black markets and hunger).</summary>
    public const double ShortageUnrest = 0.05;

    static readonly string[] StapleIds = { "wheat", "barley", "emmer", "millet", "spelt", "rice" };

    MarketContext? _markets;
    int _marketsYear = int.MinValue;
    double[,]? _lastMarketPrices;
    readonly List<ChronicleEvent> _pendingTradeNews = new();

    /// <summary>The markets open this year and which land each serves (worked out once a year).</summary>
    public MarketContext Markets()
    {
        if (_markets != null && _marketsYear == DemoYear)
            return _markets;
        var cat = MarketCatalog.Instance;
        int m = cat.Markets.Count;
        var share = new Dictionary<int, double[]>();
        if (Population != null)
        {
            var open = Enumerable.Range(0, m).Where(i => cat.Markets[i].OpenIn(DemoYear)).ToList();
            foreach (int node in Population.LandNodes)
            {
                int owner = Population.NodeOwner[node];
                if (owner <= 0 || Population.Pop[node] <= 0 || open.Count == 0)
                    continue;
                var (lon, lat) = NodeLonLat(node);
                int best = open[0];
                double bestD = double.MaxValue;
                foreach (int i in open)
                {
                    double dx = (cat.Markets[i].Lon - lon) * Math.Cos(lat * Math.PI / 180), dy = cat.Markets[i].Lat - lat;
                    double d = dx * dx + dy * dy;
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (!share.TryGetValue(owner, out var s))
                    share[owner] = s = new double[m];
                s[best] += Population.Pop[node];
            }
            foreach (var s in share.Values)
            {
                double total = s.Sum();
                for (int i = 0; i < s.Length; i++)
                    s[i] = total > 0 ? s[i] / total : 0;
            }
        }
        var goods = GoodsCatalog();
        var limited = new HashSet<int>();
        if (goods != null)
            foreach (var id in StapleIds)
                if (goods.Goods.FirstOrDefault(g => g.Id == id) is { } g)
                    limited.Add(g.Index);
        _markets = new MarketContext
        {
            Count = m, Share = share, LimitedGoods = limited,
            PriceLimits = Game.Realms.Values.Where(r => r.PriceLimit).Select(r => r.RealmId).ToHashSet(),
        };
        _marketsYear = DemoYear;
        return _markets;
    }

    /// <summary>How many merchants a realm can send: one, plus one for each open great market in its land (at most five).</summary>
    public int MaxMerchants(int realmId)
    {
        if (Population == null)
            return 1;
        int held = MarketCatalog.Instance.Markets.Count(mk => mk.OpenIn(DemoYear)
            && Population.NodeOwner[Population.NodeAtLonLat(mk.Lon, mk.Lat, LonMin, LonMax, LatMin, LatMax)] == realmId);
        return Math.Min(5, 1 + held);
    }

    /// <summary>Sends one of your merchants to a market, or calls one home. Returns why not, or null.</summary>
    public string? SendMerchant(string marketId, bool send = true)
    {
        var s = PlayerState;
        if (!send)
        {
            s.Merchants.Remove(marketId);
            return null;
        }
        var mk = MarketCatalog.Instance.Markets.FirstOrDefault(x => x.Id == marketId);
        if (mk == null || !mk.OpenIn(DemoYear))
            return "That market is not open.";
        if (s.Merchants.Count >= MaxMerchants(PlayerRealmId))
            return $"All your merchants are out ({MaxMerchants(PlayerRealmId)}). Each great market in your land lets you send one more.";
        s.Merchants.Add(marketId);
        return null;
    }

    /// <summary>
    /// After the year's trade: merchants take their cut, pirates their toll,
    /// the edict's shortages are counted, and price shocks are told.
    /// </summary>
    void AfterTrade(Dictionary<int, RealmGoods> goods, GoodsCatalog cat)
    {
        var mc = _markets;
        if (mc == null || mc.Volume.Length == 0)
            return;
        var markets = MarketCatalog.Instance.Markets;
        // Other realms send their merchants to the busiest markets where their people trade.
        foreach (var s in Game.Realms.Values.Where(x => x.RealmId != PlayerRealmId))
        {
            s.Merchants.Clear();
            if (!mc.Share.TryGetValue(s.RealmId, out var sh))
                continue;
            s.Merchants.AddRange(Enumerable.Range(0, mc.Count).Where(i => sh[i] > 0.05 && markets[i].OpenIn(DemoYear))
                .OrderByDescending(i => mc.Volume[i]).Take(MaxMerchants(s.RealmId)).Select(i => markets[i].Id));
        }
        var counts = new int[mc.Count];
        foreach (var s in Game.Realms.Values)
            foreach (var id in s.Merchants)
                if (MarketCatalog.Instance.IndexOf(id) is int i && i >= 0)
                    counts[i]++;
        foreach (var s in Game.Realms.Values)
        {
            if (!goods.TryGetValue(s.RealmId, out var rg))
                continue;
            foreach (var id in s.Merchants)
            {
                int i = MarketCatalog.Instance.IndexOf(id);
                if (i >= 0 && counts[i] > 0)
                    rg.TransitIncome += MerchantCut * mc.Volume[i] / counts[i];
            }
            rg.TradeBonus -= PiracyCustoms * PiracyPressure(s.RealmId);
            s.LastShortage = mc.Shortage.GetValueOrDefault(s.RealmId);
        }
        // Price shocks: grain soaring at a market the player trades in.
        if (_lastMarketPrices != null && _lastMarketPrices.GetLength(0) == mc.Count && mc.Share.TryGetValue(PlayerRealmId, out var mine))
            for (int i = 0; i < mc.Count; i++)
            {
                if (mine[i] < 0.05)
                    continue;
                double now = mc.LimitedGoods.Select(k => mc.Prices[i, k]).DefaultIfEmpty(1).Average();
                double before = mc.LimitedGoods.Select(k => _lastMarketPrices[i, k]).DefaultIfEmpty(1).Average();
                if (now > before * ShockRatio && now > 1.2)
                    _pendingTradeNews.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                        $"Grain prices soar at {markets[i].Name}: {now / before:0.0} times last year's{ShockCause(i, mc)}."));
            }
        _lastMarketPrices = mc.Prices;
    }

    string ShockCause(int market, MarketContext mc)
    {
        var realms = mc.Share.Where(kv => kv.Value[market] > 0.2).Select(kv => kv.Key).ToList();
        if (realms.Any(r => Game.Realm(r).BlockadeMonths > 0))
            return ", with enemy fleets off the coast";
        if (realms.Any(r => Game.Wars.Of(r).Any()))
            return ", with war in the land";
        return ", after poor harvests";
    }

    // --- Pirates ---------------------------------------------------------------------------------

    IEnumerable<PirateHaven> ActiveHavens() =>
        MarketCatalog.Instance.Havens.Where(h => h.ActiveIn(DemoYear) && !Game.SuppressedHavens.Contains(h.Id));

    /// <summary>
    /// How much of a realm's sea trade lies in pirates' reach, 0..1: the share
    /// of the places it holds on sea routes within reach of an active haven.
    /// </summary>
    public double PiracyPressure(int realmId)
    {
        var havens = ActiveHavens().ToList();
        if (havens.Count == 0)
            return 0;
        int held = 0, raided = 0;
        foreach (var (route, owners) in RouteHolders())
        {
            if (route.Kind != "sea")
                continue;
            for (int p = 0; p < owners.Length; p++)
                if (owners[p] == realmId)
                {
                    held++;
                    var pt = route.Points[p];
                    if (havens.Any(h => DisasterCatalog.Km(h.Lon, h.Lat, pt.Lon, pt.Lat) <= h.ReachKm))
                        raided++;
                }
        }
        return held == 0 ? 0 : (double)raided / held;
    }

    /// <summary>
    /// The pirates' year: they carry off some of the people of coasts in reach;
    /// a haven taken by conquest, or patrolled by warships for six months, is
    /// suppressed; at history's date the rest are swept from the sea.
    /// </summary>
    internal List<ChronicleEvent> PiratesYear()
    {
        var events = new List<ChronicleEvent>(_pendingTradeNews);
        _pendingTradeNews.Clear();
        if (Population == null)
            return events;
        var pop = Population;
        int w = pop.Width;
        foreach (var h in ActiveHavens().ToList())
        {
            // Raids on the coasts within reach.
            foreach (int i in pop.LandNodes)
            {
                if (pop.Pop[i] <= 0)
                    continue;
                int x = i % w, y = i / w;
                bool coast = (x > 0 && pop.NodeRegion[i - 1] == 0) || (x + 1 < w && pop.NodeRegion[i + 1] == 0)
                    || (y > 0 && pop.NodeRegion[i - w] == 0) || (i + w < pop.Pop.Length && pop.NodeRegion[i + w] == 0);
                if (!coast)
                    continue;
                var (lon, lat) = NodeLonLat(i);
                if (DisasterCatalog.Km(h.Lon, h.Lat, lon, lat) <= h.ReachKm)
                    pop.Pop[i] = (float)(pop.Pop[i] * (1 - RaidLoss));
            }
            // Suppressed by conquest: the haven's land changed hands this year.
            int havenNode = pop.NodeAtLonLat(h.Lon, h.Lat, LonMin, LonMax, LatMin, LatMax);
            int owner = pop.NodeOwner[havenNode];
            bool conquered = owner > 0 && Game.Lost.Values.Any(l => l.TryGetValue(ProvinceOfNode(havenNode), out int year) && year == DemoYear);
            if (conquered || Game.PatrolMonths.GetValueOrDefault(h.Id) >= PatrolMonthsToSuppress)
            {
                Game.SuppressedHavens.Add(h.Id);
                int by = conquered ? owner : Game.Realms.Values.Where(r => r.Armies.Any(a => a.Node >= 0 && Military.RawMight(a, Domain.Naval) > 0
                    && NodeKm(a.Node, h) <= PatrolKm)).Select(r => r.RealmId).FirstOrDefault();
                events.Add(new ChronicleEvent(ChronicleKind.War, by > 0 ? by : PlayerRealmId,
                    $"{(by > 0 ? RealmName(by) : "Warships")} sweep {h.Name.Replace("The ", "the ")} from the sea."));
            }
        }
        _census = null;
        return events;
    }

    internal List<ChronicleEvent> PiratesYearForTest() => PiratesYear();

    double NodeKm(int node, PirateHaven h)
    {
        var (lon, lat) = NodeLonLat(node);
        return DisasterCatalog.Km(h.Lon, h.Lat, lon, lat);
    }

    /// <summary>The month's patrols: warships standing near a haven count toward suppressing it.</summary>
    void PatrolMonth()
    {
        foreach (var h in ActiveHavens())
            if (Game.Realms.Values.Any(r => r.Armies.Any(a => a.Node >= 0 && !a.IsEmpty && Military.RawMight(a, Domain.Naval) > 0 && NodeKm(a.Node, h) <= PatrolKm)))
                Game.PatrolMonths[h.Id] = Game.PatrolMonths.GetValueOrDefault(h.Id) + 1;
    }

    // --- Embargoes and trade treaties -----------------------------------------------------------

    public bool Embargoed(int a, int b) => Game.Embargoes.Contains((a, b)) || Game.Embargoes.Contains((b, a));

    /// <summary>Stops trade with a realm (decision "Trade treaties and embargoes"), or lifts it.</summary>
    public string Embargo(int target, bool on = true)
    {
        if (on)
            Game.Embargoes.Add((PlayerRealmId, target));
        else
            Game.Embargoes.Remove((PlayerRealmId, target));
        _goodsYear = int.MinValue;   // trade runs again with the new rule
        _census = null;
        return on ? $"You forbid all trade with {RealmName(target)}." : $"Trade with {RealmName(target)} is allowed again.";
    }

    /// <summary>
    /// Proposes a trade treaty: the two realms trade directly, without
    /// middlemen. They accept unless they hate you or are at war with you.
    /// </summary>
    public string ProposeTradeTreaty(int other)
    {
        if (Game.Wars.AtWar(PlayerRealmId, other))
            return "You are at war with them.";
        if (Game.Treaties.All.Any(t => t.Kind == TreatyKind.Trade && ((t.A == PlayerRealmId && t.B == other) || (t.A == other && t.B == PlayerRealmId))))
            return "You already have a trade treaty.";
        if (Opinion(other, PlayerRealmId).Value < -20)
            return $"{RealmName(other)} will not deal with you.";
        Game.Treaties.Add(new Treaty(TreatyKind.Trade, PlayerRealmId, other, DemoYear));
        Game.Embargoes.Remove((other, PlayerRealmId));
        Game.Embargoes.Remove((PlayerRealmId, other));
        _goodsYear = int.MinValue;
        _census = null;
        return $"A trade treaty with {RealmName(other)}: your merchants deal directly.";
    }

    /// <summary>Pairs of realms with a trade treaty.</summary>
    IEnumerable<(int, int)> TradeTreatyPairs() =>
        Game.Treaties.All.Where(t => t.Kind == TreatyKind.Trade).Select(t => t.A < t.B ? (t.A, t.B) : (t.B, t.A));
}
