using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// The realm economy on the real 300 BC world: every playable realm starts
/// with its historical army and some silver; incomes are of historical size
/// (Ptolemaic Egypt, the richest kingdom, took in thousands of talents a
/// year); tax rates change income and the growth drivers' burden factor;
/// recruiting spends silver and men; a realm drowning in debt sees its
/// soldiers desert; and all of it survives a save.
/// </summary>
public partial class GameEconomyTest : TestRunner
{
    protected override async Task Run()
    {
        SaveSystemTest.UseCleanTestFolder();
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.StartGame();

        foreach (string key in WorldFixture.CivKeys)
        {
            var s = map.Game.Realm(map.CivRealmIds[key]);
            Check(s.TotalUnits > 0 && s.Armies.All(a => a.Node >= 0), $"{key} starts with no army, or one standing nowhere");
            Check(s.Treasury > 0, $"{key} starts with no silver");
        }
        int egypt = map.CivRealmIds["egypt"], rome = map.CivRealmIds["rome"], scythia = map.CivRealmIds["scythia"];
        var eg = map.Game.Realm(egypt);
        var (egTax, egTribute) = Economy.Revenue(map.CensusOf(egypt), eg.Tax);
        Check(egTax + egTribute > 2000 && egTax + egTribute < 20000,
            $"Egypt's income is {egTax + egTribute:0} talents a year; the Ptolemies took in thousands");
        var (scTax, scTribute) = Economy.Revenue(map.CensusOf(scythia), TaxRate.Normal);
        Check(scTax < scTribute * 5 + 1, "the Scythians have no provinces, so they should live on tribute, not tax");
        Check(map.Game.Realm(map.CivRealmIds["seleucid"]).RoleCount(UnitRoles.Elephants) >= 10, "Seleucus had hundreds of elephants");
        var cat = UnitCatalog.Instance;
        Check(map.Game.Realm(map.CivRealmIds["rome"]).Armies[0].Units[cat["hastati_principes"].Index] > 0,
            "Rome's army should be of legionaries, its own people's units");
        Check(map.Game.Realm(map.CivRealmIds["rome"]).Armies[0].Name == "Legio I", "Rome's first army should be Legio I");

        // A year passes: accounts are kept, and the treasury moves by the
        // balance, less whatever Egypt (run by the AI) spent raising troops.
        double before = eg.Treasury;
        int unitsBefore = eg.TotalUnits;
        var events = map.AdvanceYear();
        Check(eg.LastTax > 0, "Egypt collected no tax");
        double expected = before + eg.LastNet;
        double recruited = (eg.TotalUnits - unitsBefore) * 200.0;   // at most ~200 talents a unit, mercenaries included
        Check(eg.Treasury <= expected + 1 && eg.Treasury >= expected - Math.Max(recruited, 0) - 1,
            $"Egypt's treasury {eg.Treasury:0} doesn't match {before:0} + balance {eg.LastNet:0}");

        // Heavier taxes: more silver, and a burden on growth in the realm's regions.
        var pop = map.Population!;
        int egyptRegion = pop.Regions.First(r => r.Name == "Aegyptus").Id;
        eg.Tax = TaxRate.Crushing;
        var (heavy, _) = Economy.Revenue(map.CensusOf(egypt), eg.Tax);
        Check(heavy > egTax * 2, "crushing taxes should bring in far more");
        Economy.ApplyBurden(pop, map.Game.Realms);
        Check(pop.GetFactor(egyptRegion, GrowthFactor.Burden) < -0.5, "crushing taxes should weigh on Egypt's growth");
        eg.Tax = TaxRate.Normal;

        // Recruiting costs silver and men; disbanding returns the men.
        var r = map.Game.Realm(rome);
        var rc = map.CensusOf(rome);
        r.Treasury = 1000;
        double men = r.Manpower;
        var legio = r.Armies[0];
        var hastati = cat["hastati_principes"];
        var cultures = map.CulturesOf(rome);
        double legions = legio.Units[hastati.Index];
        Check(Military.Recruit(r, rc, cultures, hastati, legio), "Rome couldn't raise a legion");
        Check(Math.Abs(legio.Units[hastati.Index] - (legions + 1)) < 1e-9 && r.Manpower == men - 1000 && r.Treasury < 1000,
            "recruiting should cost 1,000 men and silver");
        Check(Military.Disband(r, legio, hastati) && r.Manpower == men, "disbanding should return the men");
        Check(Military.CanRecruit(r, rc, cultures, cat["scythian_horse_archers"]).Problem != null,
            "Romans shouldn't raise Scythian horse archers");
        Check(Military.CanRecruit(r, rc, cultures, cat["sacred_band"]).Problem != null, "Rome can't raise Carthage's Sacred Band");

        // Armies loyal to their general: loyalty grows with service and lifts
        // morale; only a loyal army under pressure may turn on you.
        Check(map.PlayerRealmId == rome, "the tests expect Rome to be the player");
        var loyalLegion = r.Armies.First();
        Check(loyalLegion.GeneralId == 0 || loyalLegion.LoyalTo == loyalLegion.GeneralId,
            "after a year the army should owe its loyalty to its general");
        if (loyalLegion.GeneralId != 0)
        {
            double was = loyalLegion.GeneralLoyalty;
            loyalLegion.GeneralLoyalty = 0;
            double plain = Military.Might(loyalLegion, Domain.Land);
            loyalLegion.GeneralLoyalty = 1;
            Check(Military.Might(loyalLegion, Domain.Land) > plain * 1.1, "a loyal army should fight better");
            loyalLegion.GeneralLoyalty = was;
        }
        Check(ArmyLoyalty.MutinyChance(0.5, 2) == 0 && ArmyLoyalty.MutinyChance(1, 1) > 0,
            "only a loyal army under pressure should turn on its realm");

        // Veterans' land: hardened units sent home wait for land and stir unrest;
        // settled on conquered land, they hold it for you.
        var hardened = legio.AddRegiment(hastati.Index, 2000, 0.6);
        Check(Military.Disband(r, legio, hardened, map.DemoYear) && r.VeteransWaiting >= 2000,
            "hardened soldiers sent home should wait for land");
        Check(ArmyLoyalty.WaitingUnrest(r) > 0, "veterans waiting for land should stir unrest");
        var romanLand = map.Provinces!.Provinces.Values.First(p => p.RealmId == rome && !map.Game.Sieges.Any(x => x.ProvinceId == p.Id));
        map.ProvinceStateOf(romanLand.Id).Integration = 0.5;   // as if newly conquered
        Check(map.SettleVeterans(romanLand.Id) && r.VeteransWaiting == 0 && map.ProvinceStateOf(romanLand.Id).Integration > 0.5,
            "veterans settled on conquered land should hold it for you");

        // Where units are raised: in a province whose people make them; they
        // train, then walk to the army, and are paid from the day they are called up.
        var (from, travel) = map.MusterPlace(rome, hastati, legio);
        Check(from != 0 && map.ProvinceStateOf(from).Culture == "latin", "Roman legionaries should be raised in a Latin province");
        int countBefore = legio.Count;
        double hastatiBefore = legio.Units[hastati.Index];
        double upkeepBefore = Economy.Upkeep(r);
        r.Treasury = 1000;
        Check(map.RaiseUnit(r, hastati, legio) && legio.Count == countBefore && r.Musters.Count == 1 && r.Treasury < 1000,
            "a raised unit should be paid for now and train before it joins");
        Check(Economy.Upkeep(r) > upkeepBefore, "recruits should be paid while they train");
        Check(r.Musters[0].TrainLeft == Muster.TrainingMonths(hastati) && Muster.TrainingMonths(cat["levy_spearmen"]) == 1,
            "trained troops take months, levies one");
        var arrivals = new System.Collections.Generic.List<Facsimilia.Dynasties.ChronicleEvent>();
        for (int m = 0; m < 12 && r.Musters.Count > 0; m++)
            map.MustersMonth(arrivals);
        Check(r.Musters.Count == 0 && Math.Abs(legio.Units[hastati.Index] - (hastatiBefore + 1)) < 1e-6, $"the recruits should reach the army ({travel} month(s) on the road)");

        // Military reforms: offered when history's cause is present and the
        // technology known; silver and years to take effect; the rival grows.
        double menBefore = r.Manpower, multBefore = r.ManpowerMultiplier, rivalBefore = r.RivalStrength;
        r.Manpower = 0;   // short of men, as Rome was in Gracchus' day
        map.ReformsYear();
        Check(r.ReformsOffered.Contains("lex_militaris"), "Rome, short of men, should be offered Gaius Gracchus' military law");
        Check(!r.ReformsOffered.Contains("marian_reforms"), "the Marian reforms need the cohort, which Rome doesn't know in 300 BC");
        r.Treasury = 1000;
        Check(map.EnactReform("lex_militaris") && r.ReformsPending["lex_militaris"] == 2 && r.Treasury < 1000 && r.RivalStrength > rivalBefore,
            "beginning a reform should cost silver now and anger the rival");
        map.ReformsYear();
        map.ReformsYear();
        Check(r.Reforms.ContainsKey("lex_militaris") && r.ManpowerMultiplier > multBefore, "after its years the reform should take effect");
        r.Manpower = menBefore;
        // Other realms take theirs in history's year (Xanthippus drilled Carthage's army in 255 BC).
        int year = map.DemoYear;
        map.DemoYear = -255;
        map.ReformsYear();
        Check(map.Game.Realm(map.CivRealmIds["carthage"]).Reforms.ContainsKey("xanthippus"), "Carthage should take Xanthippus' drill in 255 BC");
        Check(!map.Game.Realm(map.CivRealmIds["carthage"]).Reforms.ContainsKey("barcid_army"), "the Barcid army comes later (237 BC)");
        map.DemoYear = year;

        // Army traditions: history's at the start, shaping units; they fade unused.
        int carthage = map.CivRealmIds["carthage"];
        Check(r.Traditions.ContainsKey("manipular_legion") && r.Traditions.ContainsKey("roman_camp"), "Rome should start with its traditions");
        Check(map.Game.Realm(carthage).Traditions.ContainsKey("masters_of_the_sea"), "Carthage should start as master of the sea");
        Check(map.Game.Realm(map.CivRealmIds["rhodes"]).Traditions.ContainsKey("rhodian_seamanship")
            && !map.Game.Realm(map.CivRealmIds["byzantium"]).Traditions.ContainsKey("rhodian_seamanship"),
            "Rhodian seamanship should be Rhodes' own, not every Greek city's");
        Check(legio.RoleBoost[UnitRoles.HeavyInfantry] >= 0.1 - 1e-9, "the manipular legion should strengthen Rome's heavy infantry");
        Check(map.Game.Realms.Values.All(x => Guards.Count(x) == 0),
            "no realm should start with guard units (Carthage's line is Libyan spearmen, not all Sacred Band)");
        Check(TraditionCatalog.Crushing(10_000, 5_000, 1_000) && !TraditionCatalog.Crushing(10_000, 2_000, 1_500),
            "only a crushing defeat should break a tradition");
        r.Traditions["manipular_legion"] = map.DemoYear - TraditionCatalog.FadeYears - 1;
        map.TraditionsYear();
        Check(!r.Traditions.ContainsKey("manipular_legion"), "a tradition unused for fifty years should be forgotten");
        r.Traditions["manipular_legion"] = map.DemoYear;

        // Guards: famous veterans made the guard; at most three.
        var famous = legio.AddRegiment(hastati.Index, 1000, 0.9);
        Check(map.MakeGuard(legio, famous) && cat[famous.Type].Id == "praetorians" && Guards.Count(r) == 1,
            "Rome's famous veterans should become a praetorian cohort");
        legio.AddRegiment(cat["praetorians"].Index, 1000);
        legio.AddRegiment(cat["praetorians"].Index, 1000);
        r.Treasury = 10_000;
        Check(Military.CanRecruit(r, rc, cultures, cat["praetorians"]).Problem != null, "a realm should keep at most three guards");

        // Deep debt: unpaid soldiers desert.
        r.Debt = 1e6;
        int army = r.Armies.Sum(x => x.Men);
        Economy.Tick(r, rc);
        Check(r.Armies.Sum(x => x.Men) < army, "an army unpaid for years should shrink");

        // War's toll (Security): a great battle ravages its region, which then
        // grows slower, and the ravage fades over the years; long peace helps.
        int italia = pop.Regions.First(x => x.Name == "Italia").Id;
        int field = pop.LandNodes.First(i => pop.RegionOf(i) == italia && pop.Pop[i] > 0);
        double ravagedBefore = map.RavagedOf(italia);
        map.RavageAtNode(field, Security.Battle(40_000));
        double ravaged = map.RavagedOf(italia);
        Check(ravaged - ravagedBefore > 0.4 && ravaged - ravagedBefore <= Security.BattleRavageMax + 1e-9,
            $"a battle of 40,000 dead should ravage Italia by about 0.45, not {ravaged - ravagedBefore:0.00}");
        map.AdvanceYear();
        Check(pop.GetFactor(italia, GrowthFactor.Security) <= -0.4f, "ravaged Italia should grow slower");
        Check(map.RavagedOf(italia) < ravaged + 0.5 && map.RavagedOf(italia) >= ravaged * Security.Fade - 1e-6,
            "the ravage should fade by an eighth a year");
        Check(Math.Abs(Security.Score(0, 100) - Security.PeaceScore) < 1e-9 && Security.Score(0.3, 100) < 0,
            "long peace should help growth, and ravage should outweigh it");
        double italiaRavaged = map.RavagedOf(italia);

        r.Treasury = 1000;
        r.Debt = 0;
        Check(map.RaiseUnit(r, hastati, r.Armies[0]) && r.Musters.Count == 1, "a unit should be on its way at the save");
        // Save and load.
        Check(await map.SaveCurrentGame("slot1"), "save failed");
        var map2 = new MapView();
        Check(await map2.LoadSavedGame("slot1"), "load failed");
        var eg2 = map2.Game.Realm(egypt);
        Check(Math.Abs(eg2.Treasury - eg.Treasury) < 0.01 && eg2.Armies.Count == eg.Armies.Count
            && eg2.Armies.Zip(eg.Armies).All(x => x.First.Units.SequenceEqual(x.Second.Units) && x.First.Node == x.Second.Node
                && x.First.Name == x.Second.Name) && eg2.Tax == eg.Tax,
            "Egypt's treasury, army or tax didn't survive the save");
        Check(map2.Game.Realm(rome).Reforms.ContainsKey("lex_militaris"), "reforms didn't survive the save");
        Check(map2.Game.Realm(rome).Musters.Count == 1, "recruits on the way didn't survive the save");
        Check(map2.Game.Realm(rome).Traditions.ContainsKey("manipular_legion") && Guards.Count(map2.Game.Realm(rome)) == 3,
            "traditions and guards didn't survive the save");
        Check(map2.CapitalNode(egypt) == map.CapitalNode(egypt), $"a saved game should keep history's capitals ({map.CapitalNode(egypt)} vs {map2.CapitalNode(egypt)}; {map2.Game.Realm(egypt).CapitalLon}, {map2.Game.Realm(egypt).CapitalName})");
        Check(Math.Abs(map2.RavagedOf(italia) - italiaRavaged) < 1e-6 && map2.Game.Realm(rome).LastWarYear == r.LastWarYear,
            "the ravage of war and the years of peace didn't survive the save");

        Finish($"Economy tests passed: 12 realms start with armies and silver; Egypt takes in {egTax + egTribute:N0} " +
            $"talents a year at normal taxes ({heavy:N0} crushing); recruiting, disbanding, desertion, tax burden, war's toll on growth, raising units in their provinces, military reforms, army loyalty, veterans' land, traditions, guards and " +
            $"saving all work. {events.Count} events in the first year.");
        map.Free();
        map2.Free();
    }
}
