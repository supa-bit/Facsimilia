using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>Technology on the map: what each realm knows at the start, the year's research, and the Might it adds.</summary>
public partial class MapView
{
    static TechCatalog Tech => TechCatalog.Instance;

    /// <summary>What every realm knows in 300 BC, by its people.</summary>
    void StartTechs()
    {
        foreach (var (id, s) in Game.Realms)
        {
            foreach (var t in Tech.StartFor(RealmPeople(id).Culture))
                s.Techs.Add(t);
        }
        RefreshRoleBoosts();
    }

    /// <summary>Copies each realm's military technology onto its armies.</summary>
    void RefreshRoleBoosts()
    {
        foreach (var s in Game.Realms.Values)
        {
            var boost = new double[UnitRoles.Count];
            for (int role = 0; role < UnitRoles.Count; role++)
                boost[role] = Tech.Effect(s, "might_" + UnitRoles.Keys[role]);
            foreach (var a in s.Armies)
                Array.Copy(boost, a.RoleBoost, boost.Length);
        }
    }

    // Contacts and held regions, worked out once a year (decisions "Learning from neighbours", "Inventions from beyond").
    Dictionary<int, HashSet<int>>? _contacts;
    Dictionary<int, HashSet<string>>? _heldRegions;
    int _contactsYear = int.MinValue;

    void EnsureContacts()
    {
        if (_contacts != null && _contactsYear == DemoYear)
            return;
        _contactsYear = DemoYear;
        _contacts = new Dictionary<int, HashSet<int>>();
        _heldRegions = new Dictionary<int, HashSet<string>>();
        void Link(int a, int b)
        {
            if (a <= 0 || b <= 0 || a == b || Game.Wars.AtWar(a, b))
                return;
            (_contacts.TryGetValue(a, out var sa) ? sa : _contacts[a] = new HashSet<int>()).Add(b);
            (_contacts.TryGetValue(b, out var sb) ? sb : _contacts[b] = new HashSet<int>()).Add(a);
        }
        foreach (var (x, y) in LandNeighbours())
            Link(x, y);
        foreach (var (_, owners) in RouteHolders())
        {
            var held = owners.Where(o => o > 0).Distinct().ToList();
            foreach (int x in held)
                foreach (int y in held)
                    Link(x, y);
        }
        if (Population == null)
            return;
        var names = Population.Regions.ToDictionary(r => r.Id, r => r.Name);
        foreach (int i in Population.LandNodes)
        {
            int o = Population.NodeOwner[i];
            if (o > 0 && names.TryGetValue(Population.RegionOf(i), out var name))
                (_heldRegions.TryGetValue(o, out var set) ? set : _heldRegions[o] = new HashSet<string>()).Add(name);
        }
    }

    /// <summary>What a realm's neighbours and trade partners know, for research this year.</summary>
    public TechContext TechContextFor(int realmId)
    {
        EnsureContacts();
        var contacts = _contacts!.TryGetValue(realmId, out var c) ? c : new HashSet<int>();
        var held = _heldRegions!.TryGetValue(realmId, out var h) ? h : new HashSet<string>();
        return new TechContext
        {
            Contacts = contacts.Count,
            KnownShare = t => contacts.Count == 0 ? 0 : contacts.Count(o => Game.Realm(o).Techs.Contains(t.Id)) / (double)contacts.Count,
            Arrived = t => t.Beyond.Any(held.Contains) || contacts.Any(o => Game.Realm(o).Techs.Contains(t.Id)),
        };
    }

    /// <summary>The year's research for every realm; the player hears of each discovery.</summary>
    internal List<ChronicleEvent> TechYear()
    {
        var events = new List<ChronicleEvent>();
        var census = RealmCensus();
        foreach (var (id, s) in Game.Realms)
        {
            if (!census.TryGetValue(id, out var c))
                continue;
            foreach (var learnt in Tech.Tick(s, c, DemoYear, chooseForThem: id != PlayerRealmId, TechContextFor(id)))
                if (id == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, id, $"Your scholars master {learnt.Name.ToLowerInvariant()}."));
        }
        RefreshRoleBoosts();
        return events;
    }

    /// <summary>
    /// Conquest brings knowledge (decision "Who knows a tech"): taking a
    /// province teaches the conqueror some of what its old master knew, more
    /// the larger a share of the loser's people it held.
    /// </summary>
    List<ChronicleEvent> LearnFromConquest(int conqueror, int loser, double share)
    {
        var events = new List<ChronicleEvent>();
        if (loser <= 0)
            return events;
        var winner = Game.Realm(conqueror);
        var known = Game.Realm(loser).Techs;
        var rng = new Random(StableHash.Of(DemoYear, conqueror, loser, 9173));
        double chance = Math.Clamp(share * 2, 0.05, 0.6);
        var gained = new List<TechDef>();
        foreach (var t in Tech.All.Where(t => known.Contains(t.Id) && !winner.Techs.Contains(t.Id)).OrderBy(t => t.AvailableFrom))
            if (t.Requires.All(winner.Techs.Contains) && rng.NextDouble() < chance)
            {
                winner.Techs.Add(t.Id);
                gained.Add(t);
            }
        if (gained.Count > 0)
            events.Add(new ChronicleEvent(ChronicleKind.Economy, conqueror,
                $"{RealmName(conqueror)} learns from the scholars and craftsmen of the conquered land: {string.Join(", ", gained.Select(t => t.Name.ToLowerInvariant()))}."));
        return events;
    }

    /// <summary>Provinces a realm's court can manage well (decision "Playable 25": administrative reach).</summary>
    public int AdminCapacity(int realmId) => Loyalty.AdminCapacity + (int)Tech.Effect(Game.Realm(realmId), "admin");

    /// <summary>The player chooses what to study next.</summary>
    public string? ChooseResearch(string techId)
    {
        var t = Tech[techId];
        if (t == null)
            return "No such technology.";
        string? problem = Tech.CanResearch(PlayerState, t, DemoYear, TechContextFor(PlayerRealmId));
        if (problem != null)
            return problem;
        PlayerState.BranchResearching[TechCatalog.BranchIndex(t.Branch)] = techId;
        return null;
    }
}
