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

    /// <summary>The year's research for every realm; the player hears of each discovery.</summary>
    internal List<ChronicleEvent> TechYear()
    {
        var events = new List<ChronicleEvent>();
        var census = RealmCensus();
        foreach (var (id, s) in Game.Realms)
        {
            if (!census.TryGetValue(id, out var c))
                continue;
            var learnt = Tech.Tick(s, c, DemoYear, chooseForThem: id != PlayerRealmId);
            if (learnt != null && id == PlayerRealmId)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, id, $"Your scholars master {learnt.Name.ToLowerInvariant()}."));
        }
        RefreshRoleBoosts();
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
        string? problem = Tech.CanResearch(PlayerState, t, DemoYear);
        if (problem != null)
            return problem;
        PlayerState.Researching = techId;
        return null;
    }
}
