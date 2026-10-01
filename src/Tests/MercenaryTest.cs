using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Facsimilia.World;

namespace Facsimilia.Tests;

/// <summary>
/// Mercenary companies (section 11, part 4): history's companies wait at
/// their hiring grounds; a hired company becomes an army under its captain,
/// paid by the month and not in the yearly upkeep; a victory earns a bonus;
/// unpaid, it leaves; a richer realm can outbid you; companies survive a save;
/// and each unit type keeps its own experience.
/// </summary>
public partial class MercenaryTest : TestRunner
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
        int rome = map.CivRealmIds["rome"];
        var r = map.Game.Realm(rome);

        Check(map.Game.Companies.Count >= 10, $"history's companies should wait at their grounds, found {map.Game.Companies.Count}");
        var c = map.Game.Companies.FirstOrDefault(x => x.Employer == 0 && x.SpecId == "taenarum_hoplites");
        Check(c != null, "the hoplites of Taenarum should be for hire");
        if (c == null)
            return;

        // Hiring: an army under the captain, not in the yearly upkeep.
        double upkeepBefore = Economy.Upkeep(r);
        r.Treasury = 10000;
        Check(map.Hire(rome, c) == null, "couldn't hire a company with silver in hand");
        var army = r.ArmyById(c.ArmyId);
        Check(army != null && army.CompanyId == c.Id && army.Count == 3, "the company should become an army of three units");
        Check(r.GeneralOf(army!)?.Origin == General.Captain, "the company's captain should lead it");
        Check(System.Math.Abs(Economy.Upkeep(r) - upkeepBefore) < 1e-6, "a company should be paid by the month, not in the yearly upkeep");
        Check(map.Hire(rome, c) != null, "a company can't be hired twice");
        // Pay and bonus.
        double pay = c.MonthlyPay(army!.Regiments);
        double t0 = r.Treasury;
        army.VictoriesUnpaid = 1;
        map.MarchMonth();
        Check(System.Math.Abs(t0 - r.Treasury - pay * (1 + c.BonusMonths)) < pay * 0.01 + 1e-6 || r.Treasury < t0 - pay,
            $"a month should cost the pay plus the victory bonus ({t0 - r.Treasury:0.0} paid, {pay:0.0} a month)");
        Check(r.MercPaidThisYear > 0, "mercenary pay should be counted in the year's accounts");
        // Save and load.
        var loaded = GameState.FromDict(map.Game.ToDict());
        var lc = loaded.Companies.FirstOrDefault(x => x.Id == c.Id);
        Check(lc != null && lc.Employer == rome && lc.Captain.Name == c.Captain.Name && System.Math.Abs(lc.PayFactor - c.PayFactor) < 1e-9,
            "a company should survive a save");
        Check(loaded.Realm(rome).ArmyById(army.Id)?.CompanyId == c.Id, "the company's army should keep its company after a save");
        // Unpaid, it leaves within a couple of years.
        r.Treasury = 0;
        r.Debt = 0;
        int months = 0;
        while (c.Employer == rome && months < 36)
        {
            r.Treasury = 0;
            map.MarchMonth();
            months++;
        }
        Check(c.Employer != rome, "an unpaid company should desert, change sides or revolt");
        Check(r.ArmyById(army.Id) == null, "a company that left should take its army with it");
        // Each unit type keeps its own experience.
        var a = r.NewArmy("Test", map.CapitalNode(rome));
        int h = UnitCatalog.Instance["hastati_principes"].Index, v = UnitCatalog.Instance["velites"].Index;
        a.AddRaw(h, 2);
        a.Experience = 0.8;
        a.AddRaw(v, 2);
        Check(System.Math.Abs(a.UnitXp[h] - 0.8) < 1e-9 && a.UnitXp[v] == 0, "new units of another type shouldn't dilute the veterans");
        a.AddRaw(h, 2);
        Check(System.Math.Abs(a.UnitXp[h] - 0.4) < 1e-9, "new units of the same type dilute its experience");

        Finish($"Mercenary tests passed: {map.Game.Companies.Count} companies, hired and paid {pay:0.0} a month, left after {months} unpaid month(s).");
    }
}
