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
        List<(int From, int To, double Value)>? flows = null)
    {
        int n = cat.Goods.Count;
        var priceFactor = new double[n];
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
            priceFactor[k] = demand < 1 ? 1 : supply > 0 ? Math.Clamp(Math.Sqrt(demand / supply), MinPriceFactor, MaxPriceFactor)
                : MaxPriceFactor;
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
            double price = g.Price * priceFactor[k];
            foreach (var (from, to, qty) in offers)
            {
                double q = received[to] > lack[to] ? qty * lack[to] / received[to] : qty;
                realms[from].Exported[k] += q;
                realms[to].Imported[k] += q;
                realms[from].ExportIncome += q * price;
                realms[to].ImportCost += q * price;
                flows?.Add((from, to, q * price));
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
