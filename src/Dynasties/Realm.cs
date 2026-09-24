using Godot;

namespace Facsimilia.Dynasties;

public enum SuccessionLaw
{
    Primogeniture = 0,               // eldest living child, any gender
    MalePreferencePrimogeniture = 1, // eldest living son; else eldest living daughter
}

/// <summary>
/// A title/realm currently held by whichever character is RulerId. Its
/// territory lives on the map's ownership grid (cells holding this Id).
/// </summary>
public sealed class Realm
{
    public int Id { get; }
    public string Name { get; }
    public int RulerId { get; set; }
    public SuccessionLaw SuccessionLaw { get; }
    public Color Color { get; }  // the realm's map color - persists across succession, unlike the ruler

    public Realm(int id, string name, int rulerId, SuccessionLaw succession, Color color)
    {
        Id = id;
        Name = name;
        RulerId = rulerId;
        SuccessionLaw = succession;
        Color = color;
    }
}
