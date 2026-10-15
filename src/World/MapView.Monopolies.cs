using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// State monopolies (Treasury and money, src/Game/Monopolies.cs): realms
/// begin with the monopolies history gave them at the start date (the
/// Ptolemies' oil, papyrus, linen, beer, salt and natron), and you can
/// declare more on any good your land makes.
/// </summary>
public partial class MapView
{
    /// <summary>Each realm starts with the monopolies history gave it in the start year, run as history ran them.</summary>
    void StartMonopolies()
    {
        foreach (var r in Game.Realms.Values)
        {
            if (r.Monopolies.Count > 0 || r.CivKey == "")
                continue;
            foreach (var def in MonopolyCatalog.Instance.HistoryFor(r.CivKey, DemoYear))
                if (r.Monopolies.All(m => m.Good != def.Good))
                    r.Monopolies.Add(new Monopoly { Good = def.Good, Farmed = def.Farmed, Historical = true, Since = DemoYear });
        }
    }

    /// <summary>What each realm's monopolies bring in this year (set before the accounts are run).</summary>
    void MonopoliesYear(IReadOnlyDictionary<int, RealmCensus> census)
    {
        var cat = GoodsCatalog();
        foreach (var (id, c) in census)
        {
            var r = Game.Realm(id);
            r.LastMonopoly = cat == null || c.Goods == null ? 0 : r.Monopolies.Sum(m => MonopolyIncome(r, m, c.Goods, cat, Corruption(id)));
        }
    }

    static double MonopolyIncome(RealmState r, Monopoly m, RealmGoods goods, GoodsCatalog cat, double corruption)
    {
        var def = cat.Goods.FirstOrDefault(g => g.Id == m.Good);
        return def == null ? 0 : MonopolyCatalog.Income(m, goods.Produced[def.Index], def.Price, corruption) * r.PriceLevel;
    }

    /// <summary>What a monopoly on this good would bring the player a year, run by officials or farmed out.</summary>
    public double MonopolyIncomeFor(string good, bool farmed)
    {
        var cat = GoodsCatalog();
        var goods = CensusOf(PlayerRealmId)?.Goods;
        return cat == null || goods == null ? 0
            : MonopolyIncome(PlayerState, new Monopoly { Good = good, Farmed = farmed }, goods, cat, Corruption(PlayerRealmId));
    }

    /// <summary>The goods the player's land makes that it could monopolise, history's first, then by value.</summary>
    public List<(GoodDef Good, MonopolyDef? History, double Loads)> MonopolyChoices()
    {
        var cat = GoodsCatalog();
        var goods = CensusOf(PlayerRealmId)?.Goods;
        if (cat == null || goods == null)
            return new();
        var s = PlayerState;
        return cat.Goods
            .Where(g => goods.Produced[g.Index] >= MonopolyCatalog.MinLoads && s.Monopolies.All(m => m.Good != g.Id))
            .Select(g => (g, MonopolyCatalog.Instance.Historical(s.CivKey, g.Id, DemoYear), goods.Produced[g.Index]))
            .OrderBy(x => x.Item2 == null).ThenByDescending(x => x.Item3 * x.g.Price)
            .ToList();
    }

    /// <summary>Why the player can't monopolise this good, or null.</summary>
    public string? CanDeclareMonopoly(string good)
    {
        var s = PlayerState;
        if (s.Monopolies.Any(m => m.Good == good))
            return "You already hold it.";
        var cat = GoodsCatalog();
        var def = cat?.Goods.FirstOrDefault(g => g.Id == good);
        var goods = CensusOf(PlayerRealmId)?.Goods;
        if (def == null || goods == null || goods.Produced[def.Index] < MonopolyCatalog.MinLoads)
            return "Your land doesn't make it.";
        return null;
    }

    /// <summary>Declares a monopoly for the player's realm. Returns the chronicle line, or why not.</summary>
    public string DeclareMonopoly(string good, bool farmed)
    {
        if (CanDeclareMonopoly(good) is { } why)
            return why;
        var s = PlayerState;
        var hist = MonopolyCatalog.Instance.Historical(s.CivKey, good, DemoYear);
        var def = GoodsCatalog()?.Goods.FirstOrDefault(g => g.Id == good);
        s.Monopolies.Add(new Monopoly { Good = good, Farmed = farmed, Historical = hist != null, Since = DemoYear, Staple = def?.Need > 0 });
        string name = def?.Name.ToLowerInvariant() ?? good;
        return $"The crown alone may now sell {name}" + (farmed ? ", and contractors bid to run the sale." : "; royal officials run the sale.") +
               (hist == null ? (def?.Need > 0 ? " The people, who must buy it every day, are furious." : " The people and the merchants resent a monopoly their fathers never knew.") : "");
    }

    /// <summary>Switches a monopoly between officials and contractors.</summary>
    public void SetMonopolyFarmed(string good, bool farmed)
    {
        var m = PlayerState.Monopolies.FirstOrDefault(x => x.Good == good);
        if (m != null)
            m.Farmed = farmed;
    }
}
