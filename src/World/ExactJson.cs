using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.World;

/// <summary>
/// Reads a save's JSON with .NET's reader, which gives back every number
/// exactly as it was written. Godot's own reader gets the last digit of some
/// numbers wrong (0.027000000000000003 comes back as 0.027), so a loaded game
/// drifted from the game that was saved. Gives the same shapes Godot's reader
/// does: objects as dictionaries, arrays, every number as a double.
/// </summary>
public static class ExactJson
{
    public static Variant Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 256 });
            return From(doc.RootElement);
        }
        catch (JsonException)
        {
            return default;   // as Godot's reader does for broken text
        }
    }

    static Variant From(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var d = new GDictionary();
                foreach (var p in e.EnumerateObject())
                    d[p.Name] = From(p.Value);
                return d;
            case JsonValueKind.Array:
                var a = new GArray();
                foreach (var item in e.EnumerateArray())
                    a.Add(From(item));
                return a;
            case JsonValueKind.String:
                return e.GetString()!;
            case JsonValueKind.Number:
                return e.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return default;
        }
    }
}
