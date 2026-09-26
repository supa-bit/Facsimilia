using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Administrative reach, corruption and rivals (decision "Playable 25", with
/// the designer's note: corruption and rivals, historical but playable).
/// Provinces far from the capital settle slower and stir more; a realm past
/// what its court can manage grows corrupt; and ambitious men gather
/// strength against an overstretched, corrupt or new ruler, until they rise.
/// </summary>
public partial class MapView
{
    /// <summary>A realm's coin this year (decision "Playable 4").</summary>
    public Coin CoinOf(int realmId) =>
        CoinCatalog.Instance.For(CivRealmIds.FirstOrDefault(kv => kv.Value == realmId).Key, DemoYear);

    /// <summary>A goal's text, with its silver shown in the player's coin.</summary>
    public string GoalText(GoalDef g) => g.Text.Replace("{silver}", Money(g.Target));

    /// <summary>Silver by weight: "20,600 kg of silver".</summary>
    public string SilverKg(double talents) => $"{CoinCatalog.Instance.Kg(talents):N0} kg of silver";

    /// <summary>
    /// What the player's coin is worth in the coins of the realms it deals
    /// with, by the silver in each: "1 didrachm = 0.42 tetradrachms (Seleucid Kingdom)".
    /// </summary>
    public List<string> ExchangeRates(IEnumerable<int> realms)
    {
        var mine = CoinOf(PlayerRealmId);
        return realms.Where(r => r != PlayerRealmId && Game.Realms.ContainsKey(r))
            .GroupBy(r => CoinOf(r))
            .Where(g => g.Key != mine)
            .Select(g => $"{CoinCatalog.Rate(mine, g.Key):0.##} {g.Key.Name} ({string.Join(", ", g.Take(3).Select(RealmName))}{(g.Count() > 3 ? "..." : "")})")
            .ToList();
    }

    /// <summary>A sum of silver, told in the player's coin: "4.7M denarii".</summary>
    public string Money(double talents)
    {
        var coin = CoinOf(PlayerRealmId);
        return $"{CoinCatalog.Short(CoinCatalog.Instance.Coins(talents, coin))} {coin.Name}";
    }

    /// <summary>Beyond this distance from the capital a province is hard to govern (without roads).</summary>
    public const double FarKm = 700;
    /// <summary>Unrest from corruption at its worst (corruption 1).</summary>
    public const double CorruptionUnrest = 0.2;

    /// <summary>The distance from a realm's capital to a province, km.</summary>
    public double DistanceFromCapital(Province p)
    {
        if (Population == null)
            return 0;
        int cap = CapitalNode(p.RealmId);
        if (cap < 0)
            return 0;
        var a = NodeCell(cap);
        var b = p.LabelCell;
        double kmPerCellY = (LatMax - LatMin) * 111.2 / GridHeight;
        double lat = LatMax - (a.Y + b.Y) / 2 / GridHeight * (LatMax - LatMin);
        double kmPerCellX = (LonMax - LonMin) * 111.2 * Math.Cos(lat * Math.PI / 180) / GridWidth;
        double dx = (a.X - b.X) * kmPerCellX, dy = (a.Y - b.Y) * kmPerCellY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Far from the capital: slower integration and more unrest, unless roads join it to the realm.</summary>
    (double Integration, double Unrest) DistanceEffects(Province p)
    {
        if (p.RealmId <= 0)
            return (0, 0);
        double km = DistanceFromCapital(p);
        if (km <= FarKm)
            return (0, 0);
        double far = Math.Min(1, (km - FarKm) / FarKm);
        if (ProvinceStateOf(p.Id).Level("road") > 0)
            far *= 0.5;
        return (-0.5 * far, 0.1 * far);
    }

    /// <summary>
    /// A realm's corruption, 0..1: some always, more for every province past
    /// what the court can manage, less with laws and audits. It eats taxes
    /// and stirs unrest.
    /// </summary>
    public double Corruption(int realmId)
    {
        if (realmId <= 0)
            return 0;
        var s = Game.Realm(realmId);
        int provinces = CensusOf(realmId).Provinces;
        double over = Loyalty.Overreach(provinces, AdminCapacity(realmId));
        return Math.Clamp(0.05 + 0.25 * over + TechCatalog.Instance.Effect(s, "corruption"), 0, 0.8);
    }

    /// <summary>
    /// A rival's strength grows with overreach, corruption, unrest and a
    /// newly founded house, and fades in good order. Returns the chronicle
    /// lines: a warning at 60, and at 100 the rival rises and takes the
    /// most restless provinces with him.
    /// </summary>
    internal List<ChronicleEvent> RivalsYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Provinces == null)
            return events;
        var census = RealmCensus();
        foreach (var (id, s) in Game.Realms)
        {
            if (!census.TryGetValue(id, out var c) || c.Provinces < 4)
            {
                s.RivalStrength = Math.Max(0, s.RivalStrength - 5);
                continue;
            }
            var mine = Provinces.Provinces.Values.Where(p => p.RealmId == id).ToList();
            double unrest = mine.Average(p => ProvinceStateOf(p.Id).Unrest);
            double over = Loyalty.Overreach(c.Provinces, AdminCapacity(id));
            double push = 20 * over + 10 * Corruption(id) + 15 * Math.Max(0, unrest - 0.2) + (Game.Wars.Of(id).Any(w => w.ExhaustionFor(id) > 60) ? 3 : 0);
            double before = s.RivalStrength;
            s.RivalStrength = Math.Clamp(s.RivalStrength + push - 4, 0, 100);
            if (id == PlayerRealmId && before < 60 && s.RivalStrength >= 60)
                events.Add(new ChronicleEvent(ChronicleKind.Revolt, id,
                    "An ambitious rival gathers followers among the discontented. (Treasury: win them over with gifts, or govern better.)"));
            if (s.RivalStrength < 100)
                continue;
            // The rival rises: the most restless third of the realm's provinces break away with him.
            var taken = mine.OrderByDescending(p => ProvinceStateOf(p.Id).Unrest).Take(Math.Max(1, mine.Count / 3)).ToList();
            foreach (var p in taken)
            {
                Game.RecordLoss(id, p.Id, DemoYear);
                Provinces.SetRealm(p.Id, 0, Grid);
                MarkChanged(p.Id);
            }
            TerritoryChanged = true;
            _census = null;
            SyncPopulationOwnership();
            s.RivalStrength = 20;
            string text = $"A rival rises against {RealmName(id)}: {string.Join(", ", taken.Select(p => p.Name))} break away with him.";
            events.Add(new ChronicleEvent(ChronicleKind.Revolt, id, text));
        }
        return events;
    }

    /// <summary>The player wins over the rival's followers with gifts: silver for rival strength.</summary>
    public string? AppeaseRival()
    {
        var s = PlayerState;
        double cost = RivalAppeaseCost();
        if (s.RivalStrength < 1 || s.Treasury < cost)
            return null;
        s.Treasury -= cost;
        s.RivalStrength = Math.Max(0, s.RivalStrength - 30);
        return $"Gifts and offices win over the rival's followers ({Money(cost)}).";
    }

    public double RivalAppeaseCost()
    {
        var (tax, trib) = Economy.Revenue(CensusOf(PlayerRealmId), PlayerState.Tax);
        return Math.Round(0.4 * (tax + trib));
    }
}
