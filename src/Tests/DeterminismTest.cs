using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The same game plays the same way every time (the designer's rule: no test
/// may pass or fail by chance). Two worlds are started alike and played for
/// years; everything they save must match exactly. If anything in the game
/// draws on the clock or on .NET's per-run hashing, this fails every time.
/// </summary>
public partial class DeterminismTest : TestRunner
{
    const int Years = 4;

    protected override async Task Run()
    {
        var a = await Play();
        var b = await Play();
        string ga = Json.Stringify(a.Game.ToDict()), gb = Json.Stringify(b.Game.ToDict());
        string ra = Json.Stringify(a.Registry.ToDict()), rb = Json.Stringify(b.Registry.ToDict());
        Check(ga == gb, $"two plays of the same game should leave the same world (first difference at character {FirstDifference(ga, gb)})");
        Check(ra == rb, $"two plays should leave the same people, births and deaths (first difference at character {FirstDifference(ra, rb)})");
        if (ga != gb)
            GD.Print("world A: ", Around(ga, FirstDifference(ga, gb)), "\nworld B: ", Around(gb, FirstDifference(ga, gb)));
        if (ra != rb)
            GD.Print("people A: ", Around(ra, FirstDifference(ra, rb)), "\npeople B: ", Around(rb, FirstDifference(ra, rb)));
        // Saved and loaded half-way, a game goes on exactly as if it had never stopped.
        var c = await Play(Years / 2);
        Check(await c.SaveCurrentGame("slot1"), "save failed");
        var d = new MapView();
        Check(await d.LoadSavedGame("slot1"), "load failed");
        for (int y = Years / 2; y < Years; y++)
        {
            c.AdvanceYear();
            d.AdvanceYear();
        }
        string wc = Json.Stringify(c.Game.ToDict()), wd = Json.Stringify(d.Game.ToDict());
        Check(wc == wd, $"a loaded world should go on as the unsaved one does (first difference at character {FirstDifference(wc, wd)})");
        if (wc != wd)
            GD.Print("unsaved world: ", Around(wc, FirstDifference(wc, wd)), "\nloaded world: ", Around(wd, FirstDifference(wc, wd)));
        string rc = Json.Stringify(c.Registry.ToDict()), rd = Json.Stringify(d.Registry.ToDict());
        Check(rc == rd, $"a loaded game should go on as the unsaved one does (first difference at character {FirstDifference(rc, rd)})");
        if (rc != rd)
            GD.Print("unsaved: ", Around(rc, FirstDifference(rc, rd)), "\nloaded: ", Around(rd, FirstDifference(rc, rd)));
        c.Free();
        d.Free();

        Finish($"Determinism test passed: {Years} years played twice, {ga.Length / 1024} KB of world and {ra.Length / 1024} KB of people identical.");
        a.Free();
        b.Free();
    }

    static async Task<MapView> Play(int years = Years)
    {
        var map = await WorldFixture.Seeded();
        await map.GenerateProvinces();
        map.StartPopulation();
        map.LoadLand();
        map.SetPlayerCiv("rome");
        map.StartGame();
        for (int y = 0; y < years; y++)
            map.AdvanceYear();
        return map;
    }

    static int FirstDifference(string x, string y)
    {
        int n = System.Math.Min(x.Length, y.Length);
        for (int i = 0; i < n; i++)
            if (x[i] != y[i])
                return i;
        return x.Length == y.Length ? -1 : n;
    }

    static string Around(string s, int at) => at < 0 ? "" : s.Substring(System.Math.Max(0, at - 150), System.Math.Min(300, s.Length - System.Math.Max(0, at - 150)));
}
