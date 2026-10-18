using System;
using System.Collections.Generic;
using System.Linq;

namespace Facsimilia.World;

/// <summary>
/// Search (your answer "Search": a search box for places, characters and
/// realms): what a name finds, as far as your realm knows of it (the fog).
/// </summary>
public partial class MapView
{
    public enum FoundKind { Province, Town, Person, Realm }

    public sealed record Found(FoundKind Kind, string Name, string Detail, int Id, Godot.Vector2 Where);

    /// <summary>Everything whose name holds the words, best matches (names starting with them) first; at most this many.</summary>
    public List<Found> Search(string words, int most = 30)
    {
        words = words.Trim();
        var found = new List<Found>();
        if (words.Length < 2 || Provinces == null)
            return found;
        bool Has(string name) => name.Contains(words, StringComparison.OrdinalIgnoreCase);
        foreach (var (id, realm) in Registry.Realms)
            if (Has(realm.Name) && Game.Realms.ContainsKey(id) && CanDealWith(id))
                found.Add(new(FoundKind.Realm, realm.Name, id == PlayerRealmId ? "your realm" : "a realm", id, default));
        foreach (var p in Provinces.Provinces.Values)
            if (Has(p.Name) && ExploredAt(p.LabelCell * CellPixels))
                found.Add(new(FoundKind.Province, p.Name, $"a province of {RealmName(p.RealmId)}", p.Id, p.LabelCell * CellPixels));
        foreach (var (name, world, _) in TownsNow())
            if (Has(name.TrimStart('★', ' ')) && ExploredAt(world))
                found.Add(new(FoundKind.Town, name.TrimStart('★', ' '), "a town", 0, world));
        // People of the houses ruling realms you know.
        var houses = Registry.Realms.Where(r => CanDealWith(r.Key))
            .Select(r => Registry.Characters.TryGetValue(r.Value.RulerId, out var ruler) ? ruler.DynastyId : -1)
            .Where(d => d != -1).ToHashSet();
        foreach (var c in Registry.Characters.Values)
            if (Has(c.Name) && houses.Contains(c.DynastyId))
                found.Add(new(FoundKind.Person, c.Name,
                    (Registry.Dynasties.TryGetValue(c.DynastyId, out var d) ? d.Name : "") + (c.IsAlive ? $", aged {DemoYear - c.BirthYear}" : ", dead"),
                    c.Id, default));
        return found
            .OrderByDescending(f => f.Name.StartsWith(words, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Kind == FoundKind.Person && f.Detail.EndsWith("dead"))
            .ThenBy(f => f.Kind).ThenBy(f => f.Name)
            .Take(most).ToList();
    }
}
