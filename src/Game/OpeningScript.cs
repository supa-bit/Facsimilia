using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Facsimilia.Game;

/// <summary>One of the bard's seven scenes: told while the clock runs From..To, over the Focus (west, south, east, north).</summary>
public sealed record OpeningChapter(string Scene, int From, int To, double[] Focus);

/// <summary>A step of the story, shown from its year; its words are the voice line stage_&lt;key&gt;.</summary>
public sealed record OpeningStage(string Key, int Year);

/// <summary>An ancient place, lit from its year; its words are the voice line site_&lt;key&gt;.</summary>
public sealed record OpeningSite(string Key, string Name, double Lon, double Lat, int Year);

/// <summary>
/// The opening's script (data/opening.json): the chapters, the steps of the
/// story, the places and the population frames. Pure data and arithmetic,
/// so the tests can check it; src/UI/Opening.cs plays it.
/// </summary>
public sealed class OpeningScript
{
    public const string Path = "res://data/opening.json";

    public IReadOnlyList<int> Frames { get; }
    public IReadOnlyList<OpeningChapter> Chapters { get; }
    public IReadOnlyList<OpeningStage> Stages { get; }
    public IReadOnlyList<OpeningSite> Sites { get; }

    public int Start => Chapters[0].From;
    public int End => Chapters[^1].To;

    public OpeningScript(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Frames = root.GetProperty("frames").EnumerateArray().Select(e => e.GetInt32()).ToList();
        Chapters = root.GetProperty("chapters").EnumerateArray().Select(c => new OpeningChapter(
            c.GetProperty("scene").GetString()!, c.GetProperty("from").GetInt32(), c.GetProperty("to").GetInt32(),
            c.GetProperty("focus").EnumerateArray().Select(f => f.GetDouble()).ToArray())).ToList();
        Stages = root.GetProperty("stages").EnumerateArray().Select(s => new OpeningStage(
            s.GetProperty("key").GetString()!, s.GetProperty("year").GetInt32())).ToList();
        Sites = root.GetProperty("sites").EnumerateArray().Select(s => new OpeningSite(
            s.GetProperty("key").GetString()!, s.GetProperty("name").GetString()!,
            s.GetProperty("lon").GetDouble(), s.GetProperty("lat").GetDouble(), s.GetProperty("year").GetInt32())).ToList();
    }

    /// <summary>The two population frames around a year and how far between them (0..1).</summary>
    public (int A, int B, float Mix) FramesAt(double year)
    {
        if (year <= Frames[0])
            return (0, 0, 0);
        for (int i = 1; i < Frames.Count; i++)
            if (year <= Frames[i])
                return (i - 1, i, (float)((year - Frames[i - 1]) / (Frames[i] - Frames[i - 1])));
        return (Frames.Count - 1, Frames.Count - 1, 0);
    }

    /// <summary>The step of the story the clock has reached.</summary>
    public OpeningStage StageAt(double year) => Stages.LastOrDefault(s => s.Year <= year) ?? Stages[0];

    /// <summary>The year the clock shows a fraction p (0..1) through a chapter.</summary>
    public static double YearIn(OpeningChapter c, double p) => c.From + (c.To - c.From) * Math.Clamp(p, 0, 1);

    /// <summary>The voice line that reads a year aloud, if there is one.</summary>
    public static string YearLine(int year) => year < 0 ? $"year_bc{-year}" : $"year_ad{year}";

    public static string StageLine(string key) => $"stage_{key}";
    public static string SiteLine(string key) => $"site_{key}";

    /// <summary>How a year is written on screen: "9600 BC", "AD 1".</summary>
    public static string YearText(double year)
    {
        int y = (int)Math.Round(year);
        return y < 0 ? $"{-y:N0} BC" : $"AD {Math.Max(1, y)}";
    }
}
