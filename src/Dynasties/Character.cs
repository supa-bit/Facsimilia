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
}
