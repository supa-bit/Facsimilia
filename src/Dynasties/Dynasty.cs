using System.Collections.Generic;

namespace Facsimilia.Dynasties;

/// <summary>
/// A house/bloodline - just an id, a name, and the set of characters who
/// belong to it. Membership is patrilineal by default (a child joins its
/// father's dynasty - see CharacterRegistry.HaveChild); that's a deliberate
/// early default, not a hard rule, and can grow options later.
/// </summary>
public sealed class Dynasty
{
    public int Id { get; }
    public string Name { get; }
    public int FounderId { get; }
    public List<int> MemberIds { get; set; }

    public Dynasty(int id, string name, int founderId)
    {
        Id = id;
        Name = name;
        FounderId = founderId;
        MemberIds = new List<int> { founderId };
    }
}
