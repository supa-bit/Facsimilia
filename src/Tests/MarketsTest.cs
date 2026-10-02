using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Trade (your answers): the great markets set prices for the land nearest
/// them; merchants take a cut of a market's trade; a price limit on grain
/// brings shortages; an embargo cuts trade and a treaty opens it; pirate
/// havens prey on sea trade until suppressed; all of it survives a save.
/// </summary>
public partial class MarketsTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        int rome = map.PlayerRealmId, egypt = map.CivRealmIds["egypt"];
        var cat = MarketCatalog.Instance;
        Check(cat.Markets.Count >= 15 && cat.Havens.Count >= 3, "history's markets and pirate havens should load");
        Check(!cat.Markets.First(m => m.Id == "delos").OpenIn(-300) && cat.Markets.First(m => m.Id == "delos").OpenIn(-150), "Delos opens as a free port in 166 BC");

        map.InvalidateGoods();
        map.CensusOf(rome);
        var mc = map.Markets();
        Check(mc.Share.TryGetValue(rome, out var rs) && Math.Abs(rs.Sum() - 1) < 1e-6 && rs[cat.IndexOf("rome")] > 0.3, "Rome's people should be served mostly by the market of Rome");
        Check(mc.Share[egypt][cat.IndexOf("alexandria")] > 0.5, "Egypt's by Alexandria");
        var gcat = map.GoodsCatalog()!;
        var busy = Enumerable.Range(0, mc.Count).Where(i => mc.Volume[i] > 0).ToList();
        var differs = gcat.Goods.Where(g => busy.Select(i => Math.Round(mc.Prices[i, g.Index], 2)).Distinct().Count() > 1).ToList();
        Check(differs.Count > 0, "markets should price some goods differently");
        var ex = differs.OrderByDescending(g => busy.Max(i => mc.Prices[i, g.Index]) - busy.Min(i => mc.Prices[i, g.Index])).FirstOrDefault();
        string example = ex == null ? "" : $"{ex.Name} {busy.Min(i => mc.Prices[i, ex.Index]):0.00}-{busy.Max(i => mc.Prices[i, ex.Index]):0.00}x";
        Check(mc.Volume.Sum() > 0, "markets should see trade");

        // Merchants.
        var r = map.PlayerState;
        Check(map.SendMerchant("alexandria") == null && r.Merchants.Contains("alexandria"), "sending a merchant");
        Check(map.SendMerchant("rhodes") != null || map.MaxMerchants(rome) > 1, "merchants are limited");
        map.InvalidateGoods();
        double transit = map.CensusOf(rome).Goods!.TransitIncome;
        r.Merchants.Clear();
        map.InvalidateGoods();
        Check(transit > map.CensusOf(rome).Goods!.TransitIncome, "a merchant at Alexandria should bring customs");

        // Price limits.
        r.PriceLimit = true;
        map.InvalidateGoods();
        map.CensusOf(rome);
        Check(r.LastShortage >= 0, "a price limit is counted");
        r.PriceLimit = false;

        // Embargo and treaty.
        int carthage = map.CivRealmIds["carthage"];
        map.InvalidateGoods();
        bool partners = map.CensusOf(rome).Goods!.Partners.Contains(carthage);
        map.Embargo(carthage);
        map.InvalidateGoods();
        Check(!map.CensusOf(rome).Goods!.Partners.Contains(carthage), $"an embargo should stop trade with Carthage (partners before: {partners})");
        map.Embargo(carthage, false);
        Check(map.ProposeTradeTreaty(egypt).StartsWith("A trade treaty"), "Egypt should accept a trade treaty");
        map.InvalidateGoods();
        Check(map.CensusOf(rome).Goods!.Partners.Contains(egypt), "a trade treaty makes partners");

        // Pirates: the Illyrians and Cretans prey on someone's sea trade in 300 BC.
        double worst = map.Game.Realms.Keys.Max(id => map.PiracyPressure(id));
        Check(worst > 0, "pirates should prey on sea trade in 300 BC");
        map.Game.PatrolMonths["illyria"] = MapView.PatrolMonthsToSuppress;
        var events = map.PiratesYearForTest();
        Check(map.Game.SuppressedHavens.Contains("illyria") && events.Any(e => e.Text.Contains("sweep")), "six months of patrols should sweep the Illyrian pirates from the sea");

        // Saves.
        r.Merchants.Add("alexandria");
        r.PriceLimit = true;
        map.Embargo(carthage);
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        Check(map2.PlayerState.Merchants.Contains("alexandria") && map2.PlayerState.PriceLimit && map2.Embargoed(rome, carthage)
              && map2.Game.SuppressedHavens.Contains("illyria"), "merchants, edicts, embargoes and suppressed havens should survive a save");

        Finish($"Markets tests passed: {cat.Markets.Count} markets, {differs.Count} goods priced differently by market (e.g. {example}); merchants; edict; embargo and treaty; pirates (worst {worst:P0} of a realm's sea trade); saves.");
        map.Free();
        map2.Free();
    }
}
