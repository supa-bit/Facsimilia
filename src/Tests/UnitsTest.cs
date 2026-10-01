using System;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;

namespace Facsimilia.Tests;

/// <summary>
/// Units of their own (your answers on unit size, the roster, veterans and
/// manpower): each unit holds 100 to 5,000 men with its own experience and
/// home province; it splits into halves and merges with experience averaged
/// by men; an army keeps an even number of units; battle losses are part
/// dead, part wounded; the wounded heal month by month; the dead are kept for
/// their home province; and old saves become units of the usual size.
/// </summary>
public partial class UnitsTest : TestRunner
{
    protected override Task Run()
    {
        var cat = UnitCatalog.Instance;
        int h = cat["hastati_principes"].Index, eq = cat["equites"].Index;
        var a = new Army(cat.Count) { Id = 1, Name = "Test" };

        // Sizes stay between 100 and 5,000 men.
        Check(a.AddRegiment(h, 50).Men == Regiment.MinMen && a.AddRegiment(h, 9000).Men == Regiment.MaxMen, "units should hold 100 to 5,000 men");
        a.Regiments.Clear();

        // Split and merge.
        var big = a.AddRegiment(h, 2000, xp: 0.6, origin: 7);
        Check(a.Split(big) && a.Regiments.Count == 2 && a.Regiments.All(r => r.Men == 1000 && r.Xp == 0.6 && r.Origin == 7), "splitting should make two equal halves");
        var raw = a.AddRegiment(h, 1000, xp: 0);
        Check(a.Merge(a.Regiments[0], raw) && Math.Abs(a.Regiments[0].Xp - 0.3) < 1e-9 && a.Regiments[0].Men == 2000, "merging should average experience by men");
        Check(!a.Merge(a.Regiments[0], a.AddRegiment(h, 4000)), "a merged unit can't pass 5,000 men");

        // An even number of units.
        a.Regiments.Clear();
        a.AddRegiment(h, 1000); a.AddRegiment(h, 1000); a.AddRegiment(eq, 600);
        a.Even();
        Check(a.Regiments.Count == 4 && a.Men == 2600, $"an army should keep an even number of units without losing men ({a.Regiments.Count} units, {a.Men} men)");

        // Losses: dead and wounded, the wounded heal.
        a.Regiments.Clear();
        a.AddRegiment(h, 4000, origin: 3); a.AddRegiment(h, 4000, origin: 5);
        var rng = new Random(1);
        int lost = a.TakeLosses(0.2, Military.LoserDead, rng);
        int wounded = a.Regiments.Sum(r => r.Wounded);
        double dead = a.PendingDead.Values.Sum();
        Check(lost == 1600 && Math.Abs(dead - 800) <= 2 && wounded == lost - (int)dead, $"a beaten army's losses should be half dead, half wounded ({lost} lost, {dead} dead, {wounded} wounded)");
        Check(a.PendingDead.ContainsKey(3) && a.PendingDead.ContainsKey(5), "the dead should be counted for their home provinces");
        int men = a.Men;
        a.Heal(rng);
        Check(a.Men > men && a.Regiments.Sum(r => r.Wounded) < wounded, "the wounded should return to the line over the months");
        for (int m = 0; m < 24; m++) a.Heal(rng);
        Check(a.Regiments.Sum(r => r.Wounded) == 0 && a.Men < 8000 && a.Men > 8000 - 1600, $"after two years the wounded are back or dead ({a.Men} men)");

        // Small remnants fold into their kind.
        a.Regiments.Clear();
        a.AddRegiment(h, 1000); var tiny = a.AddRegiment(h, 100); tiny.Men = 60;
        a.Tidy();
        Check(a.Regiments.Count == 2 && a.Men == 1060 && a.Regiments.All(r => r.Men >= Regiment.MinMen), "a remnant under 100 men should join another unit of its kind (then the army is evened)");

        // Saves, and old saves.
        a.Regiments.Clear();
        a.AddRegiment(h, 1500, 0.4, 9); a.AddRegiment(eq, 500, 0.1, 2);
        var back = Army.FromDict(a.ToDict(cat), cat);
        Check(back.Regiments.Count == 2 && back.Regiments[0].Men == 1500 && back.Regiments[0].Origin == 9 && Math.Abs(back.Regiments[1].Xp - 0.1) < 1e-9, "units should survive a save");
        var old = new Godot.Collections.Dictionary { ["id"] = 2, ["name"] = "Old", ["node"] = 0,
            ["units"] = new Godot.Collections.Dictionary { ["hastati_principes"] = 3 }, ["unit_xp"] = new Godot.Collections.Dictionary { ["hastati_principes"] = 0.5 } };
        var conv = Army.FromDict(old, cat);
        Check(conv.Regiments.Count == 3 && conv.Regiments.All(r => r.Men == cat[h].Men && r.Xp == 0.5), "an old save's units become units of the usual size");

        Finish($"Units tests passed: 100-5,000 men, split and merge, even armies, losses {lost} ({dead:0} dead), wounded heal, saves.");
        return Task.CompletedTask;
    }
}
