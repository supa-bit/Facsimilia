using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Named armies on the map (decision "Playable 9"): where each stands, the
/// peoples whose units a realm can raise, starting armies, moving, and the
/// markers drawn over the map.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void ArmiesChangedEventHandler();
    [Signal] public delegate void ArmyMoveRefusedEventHandler(string why);

    /// <summary>The player's army that Conquest mode paints for.</summary>
    public int SelectedArmyId { get; set; }
    /// <summary>When set, the next left click on the map moves this army of the player's there.</summary>
    public int MovingArmyId { get; set; }

    public Army? SelectedArmy =>
        PlayerState.ArmyById(SelectedArmyId) ?? PlayerState.Armies.OrderByDescending(a => Military.RawMight(a, Domain.Land)).FirstOrDefault();

    /// <summary>The peoples whose units a realm can raise: its own and those of the provinces it holds.</summary>
    public HashSet<string> CulturesOf(int realmId)
    {
        var set = new HashSet<string>();
        string own = RealmPeople(realmId).Culture;
        if (own != "")
            set.Add(own);
        if (Provinces != null)
            foreach (var p in Provinces.Provinces.Values)
                if (p.RealmId == realmId && ProvinceStateOf(p.Id).Culture is { Length: > 0 } c)
                    set.Add(c);
        return set;
    }

    /// <summary>A realm's seat: its historical capital if it still holds it, else its most populous place.</summary>
    public int CapitalNode(int realmId)
    {
        if (Population == null)
            return -1;
        var spec = RealCivs.FirstOrDefault(c => CivRealmIds.TryGetValue(c.Key, out int id) && id == realmId);
        if (spec?.CapitalLonLat is { } ll)
        {
            int node = Population.NodeAtLonLat(ll.X, ll.Y, LonMin, LonMax, LatMin, LatMax);
            if (node >= 0 && Population.NodeOwner[node] == realmId)
                return node;
        }
        int best = -1;
        float most = -1;
        foreach (int i in Population.LandNodes)
            if (Population.NodeOwner[i] == realmId && Population.Pop[i] > most)
            {
                most = Population.Pop[i];
                best = i;
            }
        return best;
    }

    static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII" };
    static readonly string[] Ordinals = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth" };

    /// <summary>A name for a realm's next army, in its own manner.</summary>
    public string NextArmyName(int realmId)
    {
        int n = Game.Realm(realmId).NextArmyId;
        string culture = RealmPeople(realmId).Culture;
        return culture switch
        {
            "latin" => $"Legio {Roman[(n - 1) % Roman.Length]}",
            "macedonian" or "greek" => n == 1 ? "Royal army" : $"{Ordinals[(n - 1) % Ordinals.Length]} army",
            "celtic" or "iberian" or "scythian" or "sarmatian" => n == 1 ? "Warhost" : $"{Ordinals[(n - 1) % Ordinals.Length]} warhost",
            _ => $"{Ordinals[(n - 1) % Ordinals.Length]} army",
        };
    }

    /// <summary>The armies standing in a province.</summary>
    public IEnumerable<(int Realm, Army Army)> ArmiesInProvince(int provinceId)
    {
        foreach (var s in Game.Realms.Values)
            foreach (var a in s.Armies)
                if (!a.IsEmpty && ProvinceOfNode(a.Node) == provinceId)
                    yield return (s.RealmId, a);
    }

    /// <summary>Where an army stands, in words: its province, or the region of open land.</summary>
    public string PlaceOfNode(int node)
    {
        if (Population == null || node < 0)
            return "somewhere";
        int nx = node % Population.Width, ny = node / Population.Width;
        int cx = (int)((nx + 0.5) * GridWidth / Population.Width), cy = (int)((ny + 0.5) * GridHeight / Population.Height);
        var p = Provinces?.ProvinceAt(cx, cy);
        if (p != null)
            return p.Name;
        var r = Population.Regions.FirstOrDefault(x => x.Id == Population.RegionOf(node));
        return r != null ? $"the open land of {r.Name}" : "open land";
    }

    /// <summary>The centre of a node in grid cells (the map's world coordinates).</summary>
    public Vector2 NodeCell(int node) => Population == null || node < 0 ? Vector2.Zero
        : new Vector2((node % Population.Width + 0.5f) * GridWidth / Population.Width,
            (node / Population.Width + 0.5f) * GridHeight / Population.Height);

    /// <summary>Every realm's starting army (data/start_realms.json units by role), in its people's own units.</summary>
    void StartArmy(RealmState state, int[] roleCounts)
    {
        var cat = UnitCatalog.Instance;
        var cultures = CulturesOf(state.RealmId);
        var army = state.NewArmy(NextArmyName(state.RealmId), CapitalNode(state.RealmId));
        for (int role = 0; role < UnitRoles.Count && role < roleCounts.Length; role++)
            if (roleCounts[role] > 0)
                army.Units[cat.BestFor(role, cultures).Index] += roleCounts[role];
    }

    /// <summary>Armies from a save made before named armies stand at their realm's seat.</summary>
    void PlaceUnplacedArmies()
    {
        foreach (var s in Game.Realms.Values)
            foreach (var a in s.Armies)
                if (a.Node < 0)
                    a.Node = CapitalNode(s.RealmId);
    }

    /// <summary>Why the player's army can't move to a node, or null. Armies march anywhere in their own land in a turn.</summary>
    public string? CanMoveArmy(Army army, int node)
    {
        if (Population == null || node < 0 || node >= Population.NodeOwner.Length || Population.NodeRegion[node] == 0)
            return "Armies march on land.";
        if (Population.NodeOwner[node] != PlayerRealmId)
            return "Armies move within your own land; to go further, paint a conquest.";
        if (Game.Sieges.Any(s => s.Attacker == PlayerRealmId && s.ArmyId == army.Id))
            return $"{army.Name} is besieging: lift the siege first.";
        return null;
    }

    public bool MoveArmy(Army army, int node)
    {
        if (CanMoveArmy(army, node) != null)
            return false;
        army.Node = node;
        _reachYear = int.MinValue;
        EmitSignal(SignalName.ArmiesChanged);
        RefreshArmyMarkers();
        return true;
    }

    /// <summary>Calls off an army's sieges.</summary>
    public void LiftSieges(Army army)
    {
        Game.Sieges.RemoveAll(s => s.Attacker == PlayerRealmId && s.ArmyId == army.Id);
        EmitSignal(SignalName.ArmiesChanged);
    }

    // --- Markers --------------------------------------------------------------------

    /// <summary>Keeps army markers the same size on screen at any zoom.</summary>
    void ScaleArmyMarkers()
    {
        ScaleRouteLines();
        if (_armyLayer == null || _camera == null)
            return;
        float s = 0.09f / Math.Max(_camera.Zoom.X, 0.05f);
        foreach (Node child in _armyLayer.GetChildren())
            if (child is Sprite2D sprite)
                sprite.Scale = Vector2.One * s;
    }

    Node2D? _armyLayer;

    /// <summary>Draws a marker for the player's armies and for the armies of realms at war with the player.</summary>
    public void RefreshArmyMarkers()
    {
        if (MapSprite == null || Population == null)
            return;
        if (_armyLayer == null)
        {
            _armyLayer = new Node2D { ZIndex = 5 };
            AddChild(_armyLayer);
        }
        foreach (Node child in _armyLayer.GetChildren())
            child.QueueFree();
        var icon = GD.Load<Texture2D>("res://assets/icons/army.svg");
        var shipIcon = GD.Load<Texture2D>("res://assets/icons/ship.svg");
        float zoom = _camera?.Zoom.X ?? 1;
        foreach (var s in Game.Realms.Values)
        {
            bool mine = s.RealmId == PlayerRealmId;
            if (!mine && !Game.Wars.AtWar(s.RealmId, PlayerRealmId))
                continue;
            var color = ColorForOwner(s.RealmId);
            foreach (var a in s.Armies)
            {
                if (a.IsEmpty || a.Node < 0)
                    continue;
                bool fleetOnly = Military.RawMight(a, Domain.Land) <= 0;
                var marker = new Sprite2D
                {
                    Texture = fleetOnly ? shipIcon : icon,
                    Position = NodeCell(a.Node),
                    Scale = Vector2.One * (0.09f / Math.Max(zoom, 0.05f)),
                    Modulate = mine ? new Color(1f, 0.95f, 0.8f) : color.Lightened(0.3f),
                };
                var back = new Sprite2D
                {
                    Texture = fleetOnly ? shipIcon : icon,
                    Position = Vector2.One * 18,
                    Modulate = new Color(0, 0, 0, 0.7f),
                    ZIndex = -1,
                };
                marker.AddChild(back);
                _armyLayer.AddChild(marker);
            }
        }
    }
}
