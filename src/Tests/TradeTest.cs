using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Trade on the real 300 BC world: realms trade with their neighbours and,
/// if coastal, across the sea; nobody sells more than it can spare, or
/// buys more than it lacks; what is bought and sold balances across the
/// world; trade meets people's needs; silk and spices arrive only where
/// their routes enter the map; war cuts trade between enemies; and the
/// state's tolls and customs reach the treasury.
/// </summary>
public partial class TradeTest : TestRunner
{
    protected override async Task Run()
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        var cat = map.GoodsCatalog()!;
        int rome = map.CivRealmIds["rome"], carthage = map.CivRealmIds["carthage"], seleucid = map.CivRealmIds["seleucid"];
        var realms = map.CivRealmIds.Values.Select(id => map.CensusOf(id).Goods!).ToList();

        Check(map.CensusOf(rome).Goods!.Partners.Contains(carthage), "Rome and Carthage should trade across the sea");
        foreach (var g in cat.Goods)
        {
            double sold = realms.Sum(r => r.Exported[g.Index]), bought = realms.Sum(r => r.Imported[g.Index]);
            Check(Math.Abs(sold - bought) < 1e-6 * Math.Max(1, sold), $"{g.Id}: sold {sold:0.0} but bought {bought:0.0}");
            Check(realms.All(r => r.Exported[g.Index] <= r.Produced[g.Index] - r.Used[g.Index] + 1e-6),
                $"someone sells more {g.Id} than it made");
        }
        Check(realms.All(r => r.Satisfaction > 0.5) && realms.Count(r => r.Satisfaction > 0.75) >= realms.Count * 9 / 10,
            "trade should meet most of nearly every realm's needs (small desert kingdoms may go short): " +
            string.Join(", ", map.CivRealmIds.Select(kv => $"{kv.Key} {map.CensusOf(kv.Value).Goods!.Satisfaction:P0} ({map.CensusOf(kv.Value).Goods!.Partners.Count} partners)")) +
            "; routes: " + string.Join("; ", map.RouteHolders().Select(r => r.Route.Id + " " + string.Join(",", r.Owners))));
        Check(realms.Sum(r => r.ExportIncome) > 0, "no trade happened");
        var holders = map.RouteHolders();
        Check(holders.Any(r => r.Route.Id == "nile" && r.Owners.Contains(map.CivRealmIds["egypt"]) && r.Owners.Contains(map.CivRealmIds["kush"])),
            "Egypt and Kush should share the Nile");
        Check(holders.All(r => r.Route.From <= map.DemoYear) && !holders.Any(r => r.Route.Id == "suez"), "only routes open in 300 BC");
        Check(realms.Any(r => r.TransitIncome > 0), "middlemen should take tolls on goods passing through");
        Check(map.CensusOf(map.CivRealmIds["kush"]).Goods!.Partners.Count > 1, "Kush should reach beyond Egypt through Egyptian middlemen");
        var silk = cat["silk"].Index;
        Check(map.CensusOf(seleucid).Goods!.Produced[silk] > 0, "silk should arrive in the Seleucid east");
        Check(map.CensusOf(rome).Goods!.Produced[silk] == 0, "no silk grows in Italy");

        // Customs reach the treasury.
        var r = map.Game.Realm(rome);
        map.AdvanceYear();
        Check(r.LastCustoms > 0, "Rome collected no tolls and customs");

        // War cuts trade.
        map.DeclareWar(carthage);
        map.AdvanceYear();
        var rg = map.CensusOf(rome).Goods!;
        Check(!rg.Partners.Contains(carthage), "Rome shouldn't trade with Carthage at war");

        Finish($"Trade tests passed: {realms.Sum(x => x.ExportIncome) / 6000:N0} talents of goods trade hands a year; needs met " +
            $"{realms.Min(x => x.Satisfaction):P0}-{realms.Max(x => x.Satisfaction):P0}; silk from the east; war cuts trade; " +
            $"Rome's customs {r.LastCustoms:0} talents.");
        map.Free();
    }
}
