using System.Threading.Tasks;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>Helper (not a test): lists every value that a save and load changes, at full precision.</summary>
public partial class ZzRoundTrip : TestRunner
{
    protected override async Task Run()
    {
        var c = await WorldFixture.Seeded();
        await c.GenerateProvinces();
        c.StartPopulation();
        c.LoadLand();
        c.SetPlayerCiv("rome");
        c.StartGame();
        c.AdvanceYear();
        c.AdvanceYear();
        await c.SaveCurrentGame("slot1");
        var d = new MapView();
        await d.LoadSavedGame("slot1");
        Diff("", c.Game.ToDict(), d.Game.ToDict());
        Finish("done");
    }

    int _shown;
    void Diff(string path, Variant a, Variant b)
    {
        if (_shown > 40)
            return;
        if (a.VariantType == Variant.Type.Dictionary && b.VariantType == Variant.Type.Dictionary)
        {
            var da = a.AsGodotDictionary(); var db = b.AsGodotDictionary();
            foreach (var k in da.Keys)
                if (db.ContainsKey(k)) Diff(path + "/" + k, da[k], db[k]);
                else { GD.Print("missing after load: ", path + "/" + k); _shown++; }
            return;
        }
        if (a.VariantType == Variant.Type.Array && b.VariantType == Variant.Type.Array)
        {
            var aa = a.AsGodotArray(); var ab = b.AsGodotArray();
            if (aa.Count != ab.Count) { GD.Print("length ", path, " ", aa.Count, " vs ", ab.Count); _shown++; return; }
            for (int i = 0; i < aa.Count; i++) Diff(path + "[" + i + "]", aa[i], ab[i]);
            return;
        }
        string sa = Json.Stringify(a, "", true, true), sb = Json.Stringify(b, "", true, true);
        if (sa != sb) { GD.Print(path, ": ", sa, " vs ", sb, " (", a.VariantType, "/", b.VariantType, ")"); _shown++; }
    }
}
