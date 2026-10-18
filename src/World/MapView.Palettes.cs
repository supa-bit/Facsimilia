using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facsimilia.World;

/// <summary>Which colours the map views use (Settings > Display, your answer "Colour-blind palettes for the map views").</summary>
public enum ColourSight
{
    Usual,
    /// <summary>For red-green colour blindness (deuteranopia, protanopia: the most common): blue against orange.</summary>
    RedGreen,
    /// <summary>For blue-yellow colour blindness (tritanopia): teal against red.</summary>
    BlueYellow,
}

/// <summary>
/// Colour-blind palettes for the map views: the scales from low to high
/// (loyalty, wealth, people, roads, the going, supply), the named colours
/// (cultures, religions, trade goods) and the diplomacy view. With a
/// colour-blind palette, named colours come from Okabe and Ito's eight
/// (chosen so that every kind of colour blindness tells them apart), each
/// in three shades. The political map keeps history's colours for the
/// realms; their borders and names tell them apart.
/// </summary>
public partial class MapView
{
    ColourSight _sight;

    /// <summary>Set by the game from Settings (tests never read the player's settings); repaints the view shown.</summary>
    public ColourSight Sight
    {
        get => _sight;
        set
        {
            if (_sight == value)
                return;
            _sight = value;
            if (IsProvinceView(CurrentView))
            {
                if (CurrentView == DiplomacyView)
                    _ownerColorOverride = null;   // so the diplomacy colours are painted again
                ShowProvinceView(CurrentView);
            }
        }
    }

    /// <summary>Okabe and Ito's eight colours, safe for every kind of colour blindness.</summary>
    static readonly Color[] OkabeIto =
    {
        new(0.90f, 0.62f, 0.00f), new(0.34f, 0.71f, 0.91f), new(0.00f, 0.62f, 0.45f), new(0.94f, 0.89f, 0.26f),
        new(0.00f, 0.45f, 0.70f), new(0.84f, 0.37f, 0.00f), new(0.80f, 0.47f, 0.65f), new(0.60f, 0.60f, 0.60f),
    };

    static readonly Dictionary<ColourSight, (Color Low, Color Mid, Color High)> Ramps = new()
    {
        [ColourSight.Usual] = (new(0.72f, 0.22f, 0.18f), new(0.85f, 0.75f, 0.3f), new(0.25f, 0.6f, 0.3f)),
        [ColourSight.RedGreen] = (new(0.85f, 0.45f, 0.05f), new(0.92f, 0.90f, 0.82f), new(0.15f, 0.40f, 0.75f)),
        [ColourSight.BlueYellow] = (new(0.80f, 0.15f, 0.25f), new(0.92f, 0.90f, 0.86f), new(0.10f, 0.55f, 0.60f)),
    };

    /// <summary>The diplomacy view's colours: you, allies, vassals, war, truce, rivals, others.</summary>
    static readonly Dictionary<ColourSight, Color[]> DiplomacyColours = new()
    {
        [ColourSight.Usual] = new Color[] { new(0.95f, 0.8f, 0.3f), new(0.3f, 0.7f, 0.35f), new(0.55f, 0.8f, 0.5f), new(0.8f, 0.2f, 0.18f),
            new(0.55f, 0.62f, 0.75f), new(0.9f, 0.5f, 0.2f), new(0.55f, 0.53f, 0.5f) },
        [ColourSight.RedGreen] = new Color[] { new(0.94f, 0.89f, 0.26f), new(0.34f, 0.71f, 0.91f), new(0.00f, 0.45f, 0.70f), new(0.84f, 0.37f, 0.00f),
            new(0.86f, 0.86f, 0.84f), new(0.80f, 0.47f, 0.65f), new(0.45f, 0.45f, 0.45f) },
        [ColourSight.BlueYellow] = new Color[] { new(0.95f, 0.60f, 0.70f), new(0.10f, 0.60f, 0.60f), new(0.05f, 0.35f, 0.40f), new(0.85f, 0.15f, 0.20f),
            new(0.86f, 0.86f, 0.84f), new(0.55f, 0.20f, 0.10f), new(0.45f, 0.45f, 0.45f) },
    };

    Color RampLow => Ramps[_sight].Low;
    Color RampHigh => Ramps[_sight].High;

    Color Ramp(double t)
    {
        var (low, mid, high) = Ramps[_sight];
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? low.Lerp(mid, (float)(t * 2)) : mid.Lerp(high, (float)((t - 0.5) * 2));
    }

    /// <summary>A steady, distinct colour for a name (a culture, religion or good), the same every game.</summary>
    Color KeyColor(string key)
    {
        uint h = 2166136261;
        foreach (char ch in key)
            h = (h ^ ch) * 16777619;
        if (_sight != ColourSight.Usual)
        {
            // One of the eight, in one of three shades (plain, lighter, darker), toned down a little
            // so they don't glare. Each name shown takes the next one in alphabetical order, so the
            // first 24 never share a colour.
            uint i = _keyIndex.TryGetValue(key, out int at) ? (uint)at : h;
            var c = OkabeIto[i % 8].Lerp(new Color(0.55f, 0.55f, 0.55f), 0.2f);
            return (i / 8 % 3) switch { 0 => c, 1 => c.Lerp(Colors.White, 0.4f), _ => c.Darkened(0.35f) };
        }
        float hue = (h % 360) / 360f;
        float sat = 0.32f + (h / 360 % 22) / 100f;
        float val = 0.66f + (h / 10800 % 20) / 100f;
        return Color.FromHsv(hue, sat, val);
    }

    readonly Dictionary<string, int> _keyIndex = new();

    /// <summary>The names the view about to be painted will show (cultures, religions or goods), in alphabetical order.</summary>
    void IndexKeys(IEnumerable<string> keys)
    {
        _keyIndex.Clear();
        foreach (var key in keys.Where(k => k != "").Distinct().OrderBy(k => k, StringComparer.Ordinal))
            _keyIndex[key] = _keyIndex.Count;
    }

    Color DipYou => DiplomacyColours[_sight][0];
    Color DipAlly => DiplomacyColours[_sight][1];
    Color DipVassal => DiplomacyColours[_sight][2];
    Color DipWar => DiplomacyColours[_sight][3];
    Color DipTruce => DiplomacyColours[_sight][4];
    Color DipRival => DiplomacyColours[_sight][5];
    Color DipOther => DiplomacyColours[_sight][6];
}
