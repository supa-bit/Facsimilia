using System;
using System.Collections.Generic;
using System.Linq;

namespace Facsimilia.Game;

/// <summary>
/// Trade between realms (MECHANICS.md, "Trade"): once a year, each realm
/// offers what it has spare and asks for what its people lack, and buys it
/// from its trading partners: realms it borders by land, and realms on the
/// same trade route (decision "Playable 8"). Realms at war don't trade.
/// Each good has one price, higher where it is scarce. Goods from beyond
/// the map arrive in the realms holding the regions where their routes
/// enter, and travel on only by trade.
/// </summary>
public static class Trade
{
    /// <summary>Share of trade's value the state takes in tolls, harbour dues and customs.</summary>
    public const double CustomsRate = 0.05;
    /// <summary>Merchants' margin on what they carry, added to the realm's output.</summary>
    public const double MerchantMargin = 0.1;
    public const double MinPriceFactor = 0.5, MaxPriceFactor = 3.0;
    const double Tiny = 1e-6;

    /// <summary>
    /// Runs one year's trade. partners(realm) are the realms it can trade
    /// with; beyondShare(good, realm) is the realm's share of a good from
    /// beyond the map. Returns each good's price factor (1 = its usual price).
    /// </summary>
    public static double[] Run(GoodsCatalog cat, Dictionary<int, RealmGoods> realms,
        Func<int, IEnumerable<int>> partners, Func<GoodDef, int, double>? beyondShare = null,
        List<(int From, int To, double Value)>? flows = null, MarketContext? markets = null)
    {
        int n = cat.Goods.Count;
        var priceFactor = new double[n];
        int mCount = markets?.Count ?? 0;
        if (markets != null)
        {
            markets.Prices = new double[mCount, n];
            markets.Volume = new double[mCount];
            markets.Shortage.Clear();
        }
        double[] ShareOf(int realm) => markets != null && markets.Share.TryGetValue(realm, out var s) ? s : Array.Empty<double>();
        foreach (var (id, rg) in realms)
        {
            Array.Clear(rg.Imported);
            Array.Clear(rg.Exported);
            rg.ImportCost = rg.ExportIncome = rg.TransitIncome = 0;
            rg.Partners.Clear();
            foreach (int p in partners(id))
                if (p != id && realms.ContainsKey(p))
                    rg.Partners.Add(p);
            foreach (var g in cat.Goods)
                if (g.Source == GoodSource.Beyond && beyondShare != null)
                    rg.Produced[g.Index] = g.Supply * beyondShare(g, id);
        }
        // Partnership is two-way.
        foreach (var (id, rg) in realms)
            foreach (int p in rg.Partners.ToList())
                realms[p].Partners.Add(id);

        var ids = realms.Keys.ToArray();
        foreach (var g in cat.Goods)
        {
            int k = g.Index;
            // What each realm can spare (after its own people and crafts) and what it lacks.
            var spare = ids.ToDictionary(id => id, id => Math.Max(0, realms[id].Surplus(k) - Tiny));
            var lack = ids.ToDictionary(id => id, id => Math.Max(0, -realms[id].Surplus(k) - Tiny));
            double supply = spare.Values.Sum(), demand = lack.Values.Sum();
            // Scarce goods cost more, plentiful ones (that someone wants) less.
            double Factor(double d, double s) => d < 1 ? 1 : s > 0 ? Math.Clamp(Math.Sqrt(d / s), MinPriceFactor, MaxPriceFactor) : MaxPriceFactor;
            priceFactor[k] = Factor(demand, supply);
            // Each market prices the good for the land it serves (your answer "A market in each great city").
            var realmPrice = new Dictionary<int, double>();
            if (mCount > 0)
            {
                for (int m = 0; m < mCount; m++)
                {
                    double ms = 0, md = 0;
                    foreach (int id in ids)
                    {
                        var sh = ShareOf(id);
                        if (sh.Length > m && sh[m] > 0)
                        {
                            ms += spare[id] * sh[m];
                            md += lack[id] * sh[m];
                        }
                    }
                    markets!.Prices[m, k] = Factor(md, ms);
                }
                foreach (int id in ids)
                {
                    var sh = ShareOf(id);
                    double pf = 0, w = 0;
                    for (int m = 0; m < sh.Length; m++)
                    {
                        pf += sh[m] * markets!.Prices[m, k];
                        w += sh[m];
                    }
                    realmPrice[id] = w > 0 ? pf / w : priceFactor[k];
                }
            }
            double PriceAt(int id) => realmPrice.TryGetValue(id, out var p) ? p : priceFactor[k];
            if (supply <= 0 || demand <= 0)
                continue;
            // Each seller shares what it can spare among its partners in proportion to their lack.
            var offers = new List<(int From, int To, double Qty)>();
            var received = new Dictionary<int, double>();
            foreach (int e in ids)
            {
                if (spare[e] <= 0)
                    continue;
                double asked = realms[e].Partners.Sum(i => lack[i]);
                if (asked <= 0)
                    continue;
                double scale = spare[e] / Math.Max(asked, spare[e]);
                foreach (int i in realms[e].Partners)
                    if (lack[i] > 0)
                    {
                        double q = lack[i] * scale;
                        offers.Add((e, i, q));
                        received[i] = received.GetValueOrDefault(i) + q;
                    }
            }
            bool limited = markets != null && markets.LimitedGoods.Contains(k);
            foreach (var (from, to, qty) in offers)
            {
                double q = received[to] > lack[to] ? qty * lack[to] / received[to] : qty;
                double pf = (PriceAt(from) + PriceAt(to)) / 2;
                if (limited && markets!.PriceLimits.Contains(to) && pf > 1)
                {
                    // A price limit by edict: sellers won't sell below their price, so the realm goes short
                    // (decision "Setting prices": price limits with shortages and black markets).
                    double sold = q / pf;
                    markets.Shortage[to] = markets.Shortage.GetValueOrDefault(to) + (q - sold) * g.Price;
                    q = sold;
                    pf = 1;
                }
                double price = g.Price * pf;
                realms[from].Exported[k] += q;
                realms[to].Imported[k] += q;
                realms[from].ExportIncome += q * price;
                realms[to].ImportCost += q * price;
                flows?.Add((from, to, q * price));
                if (mCount > 0)
                {
                    var sf = ShareOf(from);
                    for (int m = 0; m < sf.Length; m++)
                        markets!.Volume[m] += q * price * sf[m];
                }
            }
        }
        foreach (var rg in realms.Values)
        {
            double needed = 0, met = 0;
            foreach (var g in cat.Goods)
            {
                double need = rg.Needed[g.Index];
                if (need <= 0 || g.Source == GoodSource.Beyond)   // luxuries: wanted, not needed
                    continue;
                needed += need * g.Price;
                met += Math.Min(need, Math.Max(0, rg.Available(g.Index))) * g.Price;
            }
            rg.Satisfaction = needed > 0 ? met / needed : 1;
            rg.Value += (rg.ExportIncome + rg.ImportCost) * MerchantMargin * (1 + rg.TradeBonus);
        }
        return priceFactor;
    }

    /// <summary>What the state takes from a realm's trade this year, talents.</summary>
    public static double Customs(RealmGoods g) =>
        ((g.ExportIncome + g.ImportCost) * CustomsRate + g.TransitIncome) * (1 + g.TradeBonus) / 6000.0;
}
