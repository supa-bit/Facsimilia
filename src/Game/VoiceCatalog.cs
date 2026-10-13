using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One spoken line (data/voice_lines.json, built by tools/voice/build_lines.py).</summary>
public sealed record VoiceLine(string Id, string Voice, string Plays, string Key, string Subtitle);

/// <summary>
/// Every spoken line and which take of each the designer chose. Pure data,
/// so the tests can check it without sound: the Voice node (src/UI/Voice.cs)
/// plays what this picks.
///
/// A cue names what should be said: "plays:key" (for example
/// "moment:battle_won", "wonder_done:colossus", "disaster:-226:earthquake");
/// alternatives are joined with '|' and the first that has a line wins.
/// Where a cue has lines in several voices, the narrator of the year speaks:
/// the bard before 600, the chronicler to 1800, the newsreader after.
/// </summary>
public sealed class VoiceCatalog
{
    public const string LinesPath = "res://data/voice_lines.json";
    public const string Take = "a", TakeB = "b";

    readonly Dictionary<string, VoiceLine> _byId = new();
    readonly Dictionary<(string Plays, string Key), List<VoiceLine>> _byCue = new();

    /// <summary>The take chosen for each line ("a" or "b"); lines not in here use take A.</summary>
    public Dictionary<string, string> Picks { get; } = new();

    public IReadOnlyList<VoiceLine> All { get; }

    public VoiceCatalog(string linesJson)
    {
        using var doc = JsonDocument.Parse(linesJson);
        var all = new List<VoiceLine>();
        foreach (var l in doc.RootElement.GetProperty("lines").EnumerateArray())
        {
            var line = new VoiceLine(l.GetProperty("id").GetString()!, l.GetProperty("voice").GetString()!,
                l.GetProperty("plays").GetString()!, l.GetProperty("key").GetString()!, l.GetProperty("subtitle").GetString()!);
            all.Add(line);
            _byId[line.Id] = line;
            var cue = (line.Plays, line.Key);
            if (!_byCue.TryGetValue(cue, out var list))
                _byCue[cue] = list = new List<VoiceLine>();
            list.Add(line);
        }
        All = all;
    }

    public VoiceLine? this[string id] => _byId.GetValueOrDefault(id);

    public static string Narrator(int year) => year < 600 ? "bard" : year < 1800 ? "chronicler" : "newsreader";

    /// <summary>Whether a cue (or one of its alternatives) has any line.</summary>
    public bool Has(string cue) => Candidates(cue, int.MinValue).Count > 0;

    /// <summary>The line to speak for a cue in a year, varied by rng; null if the cue has none.</summary>
    public VoiceLine? Choose(string cue, int year, Random rng)
    {
        var lines = Candidates(cue, year);
        return lines.Count == 0 ? null : lines[rng.Next(lines.Count)];
    }

    List<VoiceLine> Candidates(string cue, int year)
    {
        foreach (string alt in cue.Split('|'))
        {
            int colon = alt.IndexOf(':');
            if (colon <= 0 || !_byCue.TryGetValue((alt[..colon], alt[(colon + 1)..]), out var lines))
                continue;
            if (year == int.MinValue)
                return lines;
            // The narrator of the age, when this cue has lines in his voice; otherwise whoever has them.
            var own = lines.Where(l => l.Voice == Narrator(year)).ToList();
            return own.Count > 0 ? own : lines;
        }
        return new List<VoiceLine>();
    }

    static readonly string[] Importance = { "age", "war_begin", "realm_fallen", "game_end", "disaster", "wonder_done", "tech_learnt", "moment" };

    /// <summary>
    /// Of the cues raised at once (a year's news), the one worth saying: a new age
    /// before a historic war, a disaster, a wonder, a discovery, then everyday moments.
    /// </summary>
    public string? MostImportant(IEnumerable<string?> cues) => cues
        .Where(c => c != null && Has(c))
        .Select(c => c!)
        .OrderBy(c =>
        {
            string plays = c[..Math.Max(0, c.IndexOf(':'))];
            string key = c[(c.IndexOf(':') + 1)..];
            int i = Array.IndexOf(Importance, plays is "moment" && key is "realm_fallen" or "game_end" ? key : plays);
            return i < 0 ? Importance.Length : i;
        })
        .FirstOrDefault();

    /// <summary>The sound file of a line's take: take A in assets/voice, take B in assets/voice/takes.</summary>
    public static string FileOf(string id, string take) =>
        take == TakeB ? $"res://assets/voice/takes/{id}_b.mp3" : $"res://assets/voice/{id}.mp3";

    /// <summary>The take the game plays for a line.</summary>
    public string TakeOf(string id) => Picks.GetValueOrDefault(id, Take);

    /// <summary>Reads picks saved by SavePicks ({"line_id": "b", ...}); unknown lines and takes are ignored.</summary>
    public void LoadPicks(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("picks", out var picks))
            return;
        foreach (var p in picks.EnumerateObject())
            if (_byId.ContainsKey(p.Name) && p.Value.GetString() is Take or TakeB)
                Picks[p.Name] = p.Value.GetString()!;
    }

    public string SavePicks() => JsonSerializer.Serialize(new
    {
        about = "Which take of each spoken line the game plays: \"a\" (assets/voice) or \"b\" (assets/voice/takes). "
                + "Chosen in the main menu's Voices screen; lines not listed play take A.",
        picks = new SortedDictionary<string, string>(Picks),
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>The line groups, as the script names them (docs/Voice-script.md).</summary>
    public static readonly (string Plays, string Name)[] Groups =
    {
        ("opening", "The opening"), ("opening_stage", "The opening: its stages"), ("opening_year", "The opening: the years"),
        ("opening_site", "The opening: places"), ("opening_help", "The opening: how to watch it"),
        ("realm_intro", "Realm introductions"), ("tutorial_step", "First steps"), ("tip", "Tips"),
        ("lesson_adviser", "The adviser's lessons"), ("alert", "Warnings"), ("adviser_reply", "Replies"),
        ("envoy", "Envoys' answers"), ("age", "New ages"), ("lesson", "History lessons"),
        ("moment", "Moments in play"), ("tech_learnt", "Great discoveries"), ("wonder_done", "Wonders finished"),
        ("disaster", "Disasters"), ("war_begin", "History's wars: war"), ("war_peace", "History's wars: peace"),
        ("war_cry", "War cries"), ("victory_cry", "Victory cries"),
    };
}
