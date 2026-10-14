using System.Collections.Generic;

namespace Facsimilia.Dynasties;

/// <summary>
/// A single person: alive or dead, with the family links succession needs to
/// walk (parents, spouse, children). A plain data holder - all the logic that
/// creates, marries, and kills characters lives in CharacterRegistry, so a
/// Character never needs a back-reference to the registry that owns it.
/// </summary>
public sealed class Character
{
    public int Id { get; }
    public string Name { get; set; }
    public string Sex { get; }  // "male" or "female"
    public int BirthYear { get; }
    public int DeathYear { get; set; } = -1;
    public bool IsAlive { get; set; } = true;
    public int DynastyId { get; set; }
    public int FatherId { get; }
    public int MotherId { get; }
    public int SpouseId { get; set; } = -1;
    public Culture Culture { get; set; }
    public List<int> ChildrenIds { get; set; } = new();
    public List<string> Traits { get; set; } = new();

    /// <summary>The twelve skills before traits and age (CharSkills), each 1-100; null until first developed (old saves).</summary>
    public int[]? BaseSkills { get; set; }

    /// <summary>How they died (DeathCauses), once dead.</summary>
    public string? DeathCause { get; set; }

    /// <summary>What they remember of others, for or against (opinions).</summary>
    public List<Memory> Memories { get; set; } = new();

    /// <summary>Bonds with named people: friend, rival, lover, mentor...</summary>
    public List<Bond> Bonds { get; set; } = new();

    /// <summary>Whether their schooling is finished (an education trait held); worked out, not saved.</summary>
    internal bool Schooled { get; set; }

    /// <summary>Health and fertility from traits, kept until the traits change (NaN: to be worked out).</summary>
    internal double HealthCache = double.NaN, FertilityCache = double.NaN;

    public Character(int id, string name, string sex, int birthYear,
        int dynastyId = -1, int fatherId = -1, int motherId = -1, Culture culture = Culture.Greek)
    {
        Culture = culture;
        Id = id;
        Name = name;
        Sex = sex;
        BirthYear = birthYear;
        DynastyId = dynastyId;
        FatherId = fatherId;
        MotherId = motherId;
    }

    public bool IsMale => Sex == "male";

    public int AgeIn(int year) => year - BirthYear;

    public bool Has(string trait) => Traits.Contains(trait);
}
