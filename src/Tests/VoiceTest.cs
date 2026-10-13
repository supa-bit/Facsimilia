using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.UI;
using Facsimilia.World;
using Godot;

namespace Facsimilia.Tests;

/// <summary>
/// The voices: every line in the script has its take A recording, and every
/// cue the game speaks has a line (the ages, history's wars and disasters,
/// the wonders, the realms, the tips, the first steps, the moments of play).
/// The narrator fits the year; the player's own news is spoken, other realms'
/// only when it is history's; the most important news wins; the designer's
/// choice of takes survives saving.
/// </summary>
public partial class VoiceTest : TestRunner
{
    protected override Task Run()
    {
        var cat = new VoiceCatalog(Godot.FileAccess.GetFileAsString(VoiceCatalog.LinesPath));
        Check(cat.All.Count >= 1000, $"the script should have its thousand lines, has {cat.All.Count}");
        var noA = cat.All.Where(l => !ResourceLoader.Exists(VoiceCatalog.FileOf(l.Id, VoiceCatalog.Take))).Select(l => l.Id).ToList();
        Check(noA.Count == 0, $"every line should have take A: missing {string.Join(", ", noA.Take(5))}");
        int withB = cat.All.Count(l => Voice.Exists(l.Id, VoiceCatalog.TakeB));
        Check(AudioStreamMP3.LoadFromFile(VoiceCatalog.FileOf(cat.All[0].Id, VoiceCatalog.TakeB)).GetLength() > 0.5,
            "a take B should load straight from its file");
        Check(withB == cat.All.Count, $"every line should have take B, {withB} of {cat.All.Count} do");
        Check(cat.All.Select(l => l.Plays).Distinct().All(p => VoiceCatalog.Groups.Any(g => g.Plays == p)),
            "every group of lines should be listed on the Voices screen");

        // Every cue the game raises has something to say.
        void Has(string cue) => Check(cat.Has(cue), $"no line for the cue {cue}");
        foreach (var era in Enum.GetValues<ThemeAncient.Era>())
        {
            ThemeAncient.SetEra(era);
            Has($"age:{ThemeAncient.EraName}");
        }
        ThemeAncient.SetEra(ThemeAncient.Era.Antiquity);
        foreach (var w in WonderCatalog.Instance.All.Where(w => w.IsWonder))   // great works (canals, dams) fall back on a finished building
            Has($"wonder_done:{w.Id}");
        foreach (var d in DisasterCatalog.Instance.Events)
            Has($"disaster:{d.Year}:{d.Kind}");
        using (var goals = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(MapView.HistoryGoalsPath)))
            foreach (var g in goals.RootElement.GetProperty("goals").EnumerateArray())
            {
                var goal = new HistoryGoal(g.GetProperty("realm").GetString()!, g.GetProperty("target").GetString()!,
                    Array.Empty<string>(), g.GetProperty("from").GetInt32(), g.GetProperty("to").GetInt32(), "");
                Has($"war_begin:{MapView.GoalKey(goal)}");
                Has($"war_peace:{MapView.GoalKey(goal)}");
            }
        foreach (var civ in MapView.RealCivs)
            Has($"realm_intro:{civ.Key}");
        foreach (string tip in new[] { "realm", "goods", "diplomacy", "armies", "research", "goals", "realms", "province" })
            Has($"tip:{tip}");
        for (int step = 0; step <= 10; step++)
            Has($"tutorial_step:{step}");
        foreach (string m in new[]
                 {
                     "battle_won", "battle_lost", "naval_won", "naval_lost", "war_declared", "war_on_us", "peace", "siege_begun",
                     "siege_lifted", "city_taken", "city_lost", "ruler_died", "new_house", "heir_born", "marriage", "revolt",
                     "wonder_begun", "wonder_fell", "tech_learnt", "goal_reached", "building_done", "new_century", "realm_fallen",
                     "game_end", "alliance", "vassal_gained", "tribute", "plague", "earthquake",
                 })
            Has($"moment:{m}");

        // The narrator of the age speaks.
        var rng = new Random(1);
        Check(cat.Choose("moment:battle_won", -200, rng)?.Voice == "bard", "the bard should tell of battles in antiquity");
        Check(cat.Choose("moment:battle_won", 1200, rng)?.Voice == "chronicler", "the chronicler should in the middle ages");
        Check(cat.Choose("moment:battle_won", 1900, rng)?.Voice == "newsreader", "the newsreader should in modern times");
        Check(cat.Choose("nothing:here|moment:peace", 0, rng)?.Key == "peace", "a cue's second choice should be used when the first has no line");

        // Whose news is spoken.
        const int me = 7, them = 9;
        Check(new ChronicleEvent(ChronicleKind.War, me, "", "!moment:battle_won").CueFor(me) == "moment:battle_won", "our battles are spoken");
        Check(new ChronicleEvent(ChronicleKind.War, them, "", "!moment:battle_won").CueFor(me) == null, "others' battles are not");
        Check(new ChronicleEvent(ChronicleKind.War, them, "", "war_begin:x").CueFor(me) == "war_begin:x", "history's wars are spoken for all");
        Check(new ChronicleEvent(ChronicleKind.Birth, me, "").CueFor(me) == "moment:heir_born", "a birth at court is spoken");
        Check(new ChronicleEvent(ChronicleKind.Birth, them, "").CueFor(me) == null, "a birth abroad is not");
        Check(new ChronicleEvent(ChronicleKind.Economy, me, "").CueFor(me) == null, "everyday news is silent");
        Check(cat.MostImportant(new[] { "moment:battle_won", null, "age:The Middle Ages", "disaster:-226:earthquake" }) == "age:The Middle Ages",
            "a new age should be told before anything else");
        Check(cat.MostImportant(new[] { "moment:battle_won", "wonder_done:colossus" }) == "wonder_done:colossus",
            "a finished wonder should be told before a battle");
        Check(cat.MostImportant(new string?[] { null, "nothing:here" }) == null, "cues without lines are skipped");

        // The designer's choices survive saving; nonsense is ignored.
        string first = cat.All[0].Id, second = cat.All[1].Id;
        Check(cat.TakeOf(first) == "a", "an unchosen line plays take A");
        cat.Picks[first] = "b";
        cat.Picks[second] = "a";
        var again = new VoiceCatalog(Godot.FileAccess.GetFileAsString(VoiceCatalog.LinesPath));
        again.LoadPicks(cat.SavePicks());
        Check(again.TakeOf(first) == "b" && again.TakeOf(second) == "a" && again.Picks.Count == 2, "the choices should load back");
        again.LoadPicks("{\"picks\": {\"no_such_line\": \"b\", \"" + second + "\": \"c\"}}");
        Check(again.Picks.Count == 2 && again.TakeOf(second) == "a", "unknown lines and takes should be ignored");
        if (Godot.FileAccess.FileExists(Voice.PicksPath))   // the designer's own choices, once made
            again.LoadPicks(Godot.FileAccess.GetFileAsString(Voice.PicksPath));

        Finish($"Voice tests passed: {cat.All.Count} lines, {withB} with a second take, every cue spoken.");
        return Task.CompletedTask;
    }
}
