using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The opening: every scene, step, place and year it speaks has a recorded
/// line; the chapters run in order without gaps from 10,000 BC to the game's
/// start; the places sit on the map and light up inside the story's years;
/// every population frame is there, the last one being the game's own start;
/// and the clock and the frames agree.
/// </summary>
public partial class OpeningTest : TestRunner
{
    protected override Task Run()
    {
        var script = new OpeningScript(Godot.FileAccess.GetFileAsString(OpeningScript.Path));
        var lines = new VoiceCatalog(Godot.FileAccess.GetFileAsString(VoiceCatalog.LinesPath));
        void Spoken(string id) => Check(lines[id] != null && ResourceLoader.Exists(VoiceCatalog.FileOf(id, VoiceCatalog.Take)),
            $"the opening speaks {id}, which has no recording");

        Check(script.Chapters.Count == 7, $"the bard tells seven scenes, the script has {script.Chapters.Count}");
        Check(script.Start == -10000 && script.End == World.MapView.StartYear,
            $"the opening should run from 10,000 BC to the game's start, runs {script.Start} to {script.End}");
        for (int i = 0; i < script.Chapters.Count; i++)
        {
            var c = script.Chapters[i];
            Spoken(c.Scene);
            Spoken(OpeningScript.YearLine(c.From));
            Check(c.To > c.From, $"{c.Scene} should move the clock on");
            if (i > 0)
                Check(script.Chapters[i - 1].To == c.From, $"{c.Scene} should begin where the scene before it ends");
            Check(c.Focus.Length == 4 && c.Focus[0] < c.Focus[2] && c.Focus[1] < c.Focus[3]
                  && c.Focus[0] >= World.MapView.LonMin && c.Focus[2] <= World.MapView.LonMax
                  && c.Focus[1] >= World.MapView.LatMin && c.Focus[3] <= World.MapView.LatMax,
                $"{c.Scene} should look at a part of the map");
        }
        Check(script.Chapters.Select(c => c.Scene).SequenceEqual(lines.All.Where(l => l.Plays == "opening").Select(l => l.Id)),
            "the chapters should tell every scene of the opening, in order");

        foreach (var s in script.Stages)
            Spoken(OpeningScript.StageLine(s.Key));
        Check(script.Stages.Count == lines.All.Count(l => l.Plays == "opening_stage"), "every step of the story should be in the script");
        Check(script.Stages.Zip(script.Stages.Skip(1)).All(p => p.First.Year <= p.Second.Year), "the steps should come in order");
        Check(script.StageAt(-10000).Key == "hunters" && script.StageAt(-3200).Key == "writing" && script.StageAt(-300).Key == "polities",
            "the step shown should follow the clock");

        foreach (var site in script.Sites)
        {
            Spoken(OpeningScript.SiteLine(site.Key));
            Check(site.Lon > World.MapView.LonMin && site.Lon < World.MapView.LonMax
                  && site.Lat > World.MapView.LatMin && site.Lat < World.MapView.LatMax, $"{site.Name} should be on the map");
            Check(site.Year < -3000, $"{site.Name} should light up before the first states");
        }
        Check(script.Sites.Count == lines.All.Count(l => l.Plays == "opening_site"), "every place recorded should be on the map");
        Check(script.Sites.Select(s => s.Key).Distinct().Count() == script.Sites.Count, "each place once");

        foreach (string help in new[] { "opening_help_pause", "opening_help_speed", "opening_help_resume" })
            Spoken(help);
        foreach (var year in lines.All.Where(l => l.Plays == "opening_year"))
            Check(OpeningScript.YearLine(int.Parse(year.Key)) == year.Id, $"the year line {year.Id} should be found by its year");

        // The people: one frame per HYDE snapshot, ending on the game's start.
        Check(script.Frames[0] == script.Start && script.Frames[^1] == script.End, "the frames should cover the whole story");
        foreach (int year in script.Frames)
        {
            string path = $"res://data/opening/pop_{-year}BC.png";
            var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Check(tex != null && tex.GetWidth() == 780 && tex.GetHeight() == 456, $"{path} should be a 780 x 456 frame");
        }
        var (a, b, mix) = script.FramesAt(-9500);
        Check(a == 0 && b == 1 && Mathf.IsEqualApprox(mix, 0.5f), "9500 BC lies halfway between the first two frames");
        Check(script.FramesAt(-300).B == script.Frames.Count - 1 && script.FramesAt(-12000) == (0, 0, 0f), "the ends hold their frames");
        Check(OpeningScript.YearIn(script.Chapters[0], 0.5) == -9500, "the clock runs evenly through a chapter");
        Check(OpeningScript.YearText(-9600) == "9,600 BC", "years are written as BC");

        // Photographs of the places: each one credited, and only licences that allow any use.
        int photos = 0;
        if (Godot.FileAccess.FileExists("res://data/opening_photos.json"))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://data/opening_photos.json"));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                photos++;
                string lic = p.Value.GetProperty("licence").GetString()!;
                Check(script.Sites.Any(s => s.Key == p.Name), $"the photo {p.Name} should belong to a place");
                Check(Godot.FileAccess.FileExists($"res://assets/opening/sites/{p.Name}.jpg"), $"the photo of {p.Name} should be there");
                Check(!System.Text.RegularExpressions.Regex.IsMatch(lic, "NC|ND", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                    $"the photo of {p.Name} should be free for any use, is {lic}");
                Check(p.Value.GetProperty("author").GetString()!.Length > 0, $"the photo of {p.Name} should name its author");
            }
        }

        Finish($"Opening tests passed: {script.Chapters.Count} scenes, {script.Stages.Count} steps, {script.Sites.Count} places, {script.Frames.Count} frames of people, {photos} photographs.");
        return Task.CompletedTask;
    }
}
