using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;

namespace Facsimilia.Tests;

/// <summary>
/// City names from the Pleiades gazetteer: thousands of places load,
/// each shows the name of its time (Poseidonia in 300 BC, Paestum under
/// Rome), and the nearest place to a spot is found by name.
/// </summary>
public partial class PlacesTest : TestRunner
{
    protected override Task Run()
    {
        var cat = PlaceCatalog.Instance;
        Check(cat.Places.Count > 5000, $"thousands of places should load, got {cat.Places.Count}");
        int standing = cat.StandingIn(-300).Count();
        Check(standing > 2000, $"thousands of places should stand in 300 BC, got {standing}");
        string? early = cat.Nearest(15.005, 40.42, -300, 10), late = cat.Nearest(15.005, 40.42, 100, 10);
        Check(early != null && early.StartsWith("Pos"), $"Paestum should be Poseidonia in 300 BC, got {early}");
        Check(late is "Paestum" or "Paistos", $"and Paestum under Rome, got {late}");
        Check(cat.Nearest(12.48, 41.89, -300, 10) is string rome && rome.StartsWith("Rom"), "Rome should be found near Rome");
        Finish($"Places tests passed: {cat.Places.Count} Pleiades places, {standing} standing in 300 BC; {early} became {late}.");
        return Task.CompletedTask;
    }
}
