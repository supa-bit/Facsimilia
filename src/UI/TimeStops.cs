using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;

namespace Facsimilia.UI;

/// <summary>
/// What stops time (your answer "A list in Settings where you tick what
/// stops time"): each thing that can happen to your realm, and whether the
/// running clock (or a turn of several years) stops for it. The ticks live
/// in Settings > Time; the game passes them in, so nothing here reads the
/// player's settings itself.
/// </summary>
public static class TimeStops
{
    public sealed record Reason(string Id, string Name, string Detail, bool OnAtFirst);

    /// <summary>Every reason, in the order Settings lists them. The first six are the ones time always stopped for before.</summary>
    public static readonly Reason[] All =
    {
        new("succession", "Your ruler dies", "A new ruler takes the throne, or a new house if the family has died out.", true),
        new("war", "War is declared", "Someone declares war on you, or you on them.", true),
        new("conquest", "Land changes hands", "You win or lose a province.", true),
        new("revolt", "A revolt", "One of your provinces rises up.", true),
        new("disaster", "A disaster", "Plague, earthquake, eruption or flood strikes your land.", true),
        new("trouble", "Famine or desertion", "Your people go hungry, or unpaid soldiers desert.", true),
        new("peace", "Peace is made", "A war of yours ends.", false),
        new("birth", "A child is born", "A child is born in your ruling family.", false),
        new("marriage", "A marriage", "Someone in your ruling family marries.", false),
        new("death", "A death in the family", "Someone of your ruling family, other than the ruler, dies.", false),
        new("newyear", "Each new year", "Time stops every January, so you can look over the year before.", false),
    };

    public static IEnumerable<string> Defaults => All.Where(r => r.OnAtFirst).Select(r => r.Id);

    /// <summary>The reason this event would stop time for, or null if none does (it is not about your realm).</summary>
    public static string? ReasonFor(ChronicleEvent e, int player)
    {
        if (e.RealmId != player)
            return null;
        return e.Kind switch
        {
            ChronicleKind.Succession or ChronicleKind.NewHouse => "succession",
            ChronicleKind.War => "war",
            ChronicleKind.Peace => "peace",
            ChronicleKind.Conquest => "conquest",
            ChronicleKind.Revolt => "revolt",
            ChronicleKind.Disaster => "disaster",
            ChronicleKind.Birth => "birth",
            ChronicleKind.Marriage => "marriage",
            ChronicleKind.Death => "death",
            // Of the economy, only trouble stops time: famine, desertion.
            ChronicleKind.Economy when e.Text.Contains("famine") || e.Text.Contains("desert") => "trouble",
            _ => null,
        };
    }

    /// <summary>Whether this event stops time, with these reasons ticked.</summary>
    public static bool Stops(ChronicleEvent e, int player, ICollection<string> ticked) =>
        ReasonFor(e, player) is { } reason && ticked.Contains(reason);
}
