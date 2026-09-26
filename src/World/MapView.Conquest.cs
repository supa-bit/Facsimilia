using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Conquest by painting (MECHANICS.md, "Annexation resolution"): in Conquest
/// mode the player paints within their army's reach; touching an enemy
/// province targets the whole province, painting unorganized or unclaimed
/// land targets just that land. Nothing happens until the turn ends: then
/// every target is fought out (Game.Conquest) and won land changes hands.
/// </summary>
public partial class MapView
{
    [Signal] public delegate void ConquestPlanChangedEventHandler();

    /// <summary>The conquest brush is wider than the province brush: it marks land to take, not borders.</summary>
    public const int ConquestRadius = 40;

    bool[] _reach = Array.Empty<bool>();       // per population node: can the player's army get there this year
    bool[] _reachBySea = Array.Empty<bool>();  // only across water
    int _reachYear = int.MinValue;
    public bool TerritoryChanged { get; set; }  // land changed hands: the map needs redrawing
    int _reachArmy = -1;
    /// <summary>How far past its own borders a realm counts as reaching, without an army (for pretexts and the AI).</summary>
    public const double TerritoryReachKm = 150;

    /// <summary>
    /// Where a realm's army can strike this year: within ReachKm of its land
    /// over land; with a fleet, the sea costs a third as much to cross
    /// (decision "Playable 13", suggested: reach and supply).
    /// </summary>
    public (bool[] Reach, bool[] BySea) ComputeReach(int realmId, Army? army = null)
    {
        var pop = Population!;
        int w = pop.Width, h = pop.Height, n = w * h;
        bool fleet = army != null ? Military.RawMight(army, Domain.Naval) > 0 : Military.RawMight(Game.Realm(realmId), Domain.Naval) > 0;
        double stepKm = (LatMax - LatMin) / h * 111.2;
        var xKm = new double[h];
        for (int row = 0; row < h; row++)
        {
            double lat = LatMax - (row + 0.5) * (LatMax - LatMin) / h;
            xKm[row] = (LonMax - LonMin) / w * 111.2 * Math.Cos(lat * Math.PI / 180);
        }
        // From an army: where it can march in a year. Without one: within a
        // short march of the realm's borders (what counts as neighbours).
        double landLimit = army != null ? Conquest.ReachKm : TerritoryReachKm;
        var landCost = Dijkstra(allowSea: false);
        var anyCost = fleet ? Dijkstra(allowSea: true) : landCost;
        var reach = new bool[n];
        var bySea = new bool[n];
        for (int i = 0; i < n; i++)
        {
            bool land = pop.NodeRegion[i] != 0;
            if (!land)
                continue;
            reach[i] = anyCost[i] <= landLimit;
            bySea[i] = reach[i] && landCost[i] > landLimit;
        }
        return (reach, bySea);

        double[] Dijkstra(bool allowSea)
        {
            var cost = new double[n];
            Array.Fill(cost, double.PositiveInfinity);
            var queue = new PriorityQueue<int, double>();
            int[] owners = pop.NodeOwner;
            if (army != null && army.Node >= 0)
            {
                cost[army.Node] = 0;
                queue.Enqueue(army.Node, 0);
            }
            else
                foreach (int i in pop.LandNodes)
                    if (owners[i] == realmId)
                    {
                        cost[i] = 0;
                        queue.Enqueue(i, 0);
                    }
            while (queue.TryDequeue(out int i, out double c))
            {
                if (c > cost[i] || c > landLimit)
                    continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int j = ny * w + nx;
                        bool sea = pop.NodeRegion[j] == 0;
                        if (sea && !allowSea)
                            continue;
                        // East-west degrees shrink with latitude.
                        double ex = dx != 0 ? xKm[y] : 0, ey = dy != 0 ? stepKm : 0;
                        double step = Math.Sqrt(ex * ex + ey * ey) * (sea ? Conquest.SeaCostShare : 1);
                        double next = c + step;
                        if (next < cost[j])
                        {
                            cost[j] = next;
                            queue.Enqueue(j, next);
                        }
                    }
            }
            return cost;
        }
    }

    void EnsurePlayerReach()
    {
        var army = SelectedArmy;
        int armyId = army?.Id ?? 0;
        if (Population == null || (_reachYear == DemoYear && _reach.Length > 0 && _reachArmy == armyId))
            return;
        if (army != null)
            (_reach, _reachBySea) = ComputeReach(PlayerRealmId, army);
        else
        {
            _reach = new bool[Population.Width * Population.Height];
            _reachBySea = new bool[_reach.Length];
        }
        _reachYear = DemoYear;
        _reachArmy = armyId;
        if (MapSprite?.Material is ShaderMaterial material)
        {
            var bytes = new byte[_reach.Length];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = _reach[i] ? (byte)255 : (byte)0;
            var image = Image.CreateFromData(Population.Width, Population.Height, false, Image.Format.R8, bytes);
            material.SetShaderParameter("reach_texture", ImageTexture.CreateFromImage(image));
        }
    }

    /// <summary>Dims land out of the army's reach while planning a conquest.</summary>
    internal void ShowReach(bool show)
    {
        if (show)
            EnsurePlayerReach();
        (MapSprite?.Material as ShaderMaterial)?.SetShaderParameter("reach_strength", show ? 1f : 0f);
    }

    int NodeOfCell(int idx) => Population!.NodeAtCell(idx % GridWidth, idx / GridWidth, GridWidth, GridHeight);

    /// <summary>Whether the player's conquest brush may mark this cell.</summary>
    bool CanPlanConquest(int x, int y)
    {
        int owner = Grid.GetOwner(x, y);
        if (owner == SeaOwnerId || owner == PlayerRealmId || Population == null)
            return false;
        EnsurePlayerReach();
        return _reach[Population.NodeAtCell(x, y, GridWidth, GridHeight)];
    }

    /// <summary>Marks cells as the player's plan, as painting would (tests; reach is checked).</summary>
    internal int PlanCells(IEnumerable<int> cells)
    {
        int added = 0;
        foreach (int idx in cells)
        {
            int x = idx % GridWidth, y = idx / GridWidth;
            if (_proposal[idx] == PlayerRealmId || !CanPlanConquest(x, y))
                continue;
            _proposal[idx] = PlayerRealmId;
            _dirtyProposalCells.Add(idx);
            added++;
        }
        return added;
    }

    /// <summary>What the player's painted plan would attack: one target per enemy province or per owner's other land.</summary>
    public List<ConquestTarget> PlayerConquestTargets() =>
        TargetsFromCells(PlayerRealmId, _dirtyProposalCells, _reachBySea, SelectedArmy?.Id ?? 0);

    /// <summary>Groups marked cells into targets and estimates each fight.</summary>
    internal List<ConquestTarget> TargetsFromCells(int attacker, IEnumerable<int> cells, bool[] bySea, int armyId)
    {
        var targets = new List<ConquestTarget>();
        if (Population == null)
            return targets;
        var provinceTargets = new Dictionary<int, ConquestTarget>();
        var landTargets = new Dictionary<int, ConquestTarget>();
        var nodeCells = new Dictionary<(int Owner, int Node), int>();
        foreach (int idx in cells)
        {
            int owner = Grid.Cells[idx];
            if (owner == attacker || owner == SeaOwnerId)
                continue;
            int node = NodeOfCell(idx);
            bool sea = node < bySea.Length && bySea[node];
            int prov = Provinces?.Cells[idx] ?? ProvinceMap.None;
            if (prov != ProvinceMap.None && Provinces!.Provinces.TryGetValue(prov, out var province) && province.RealmId == owner)
            {
                if (provinceTargets.TryGetValue(prov, out var known))
                {
                    if (!sea && known.BySea)
                        known.BySea = false;   // some of it can be reached by land
                }
                else
                    provinceTargets[prov] = new ConquestTarget
                    {
                        Attacker = attacker, Owner = owner, ProvinceId = prov, BySea = sea, ArmyId = armyId,
                        Node = Population.NodeAtCell((int)province.LabelCell.X, (int)province.LabelCell.Y, GridWidth, GridHeight),
                        Name = $"{province.Name} ({RealmName(owner)})", People = ProvincePopulation(prov),
                    };
                continue;
            }
            if (!landTargets.TryGetValue(owner, out var t))
                landTargets[owner] = t = new ConquestTarget
                {
                    Attacker = attacker, Owner = owner, BySea = sea, ArmyId = armyId, Node = node,
                    Name = owner == 0 ? "Unclaimed land" : $"Unorganized land of {RealmName(owner)}",
                };
            t.Cells.Add(idx);
            if (!sea && t.BySea)
            {
                t.BySea = false;
                t.Node = node;
            }
            nodeCells[(owner, node)] = nodeCells.GetValueOrDefault((owner, node)) + 1;
        }
        double cellsPerNode = (double)GridWidth / Population.Width * GridHeight / Population.Height;
        foreach (var ((owner, node), count) in nodeCells)
            landTargets[owner].People += Population.Pop[node] * Math.Min(1, count / cellsPerNode);
        targets.AddRange(provinceTargets.Values);
        targets.AddRange(landTargets.Values);
        foreach (var t in targets)
            t.Problem = Conquest.CheckTarget(t, Game, RealmName(t.Owner)) ??
                (Game.Sieges.Any(x => x.Attacker == attacker && SameTarget(x, t)) ? "Already under siege" : null);
        EstimateTargets(targets);
        return targets;
    }

    public string RealmName(int id) =>
        id == 0 ? "no one" : Registry.Realms.TryGetValue(id, out var r) ? r.Name : "a vanished realm";

    /// <summary>The player's plan becomes sieges at the start of the turn's first year, then is cleared.</summary>
    internal List<ChronicleEvent> ResolvePlayerPlan()
    {
        var events = new List<ChronicleEvent>();
        if (_dirtyProposalCells.Count == 0 || Population == null)
            return events;
        EnsurePlayerReach();
        var targets = PlayerConquestTargets();
        events.AddRange(BeginSieges(targets));
        if (MapImage != null)
            ClearProposal();   // repaints just the painted cells
        else
        {
            foreach (int idx in _dirtyProposalCells)
                _proposal[idx] = 0;
            _dirtyProposalCells.Clear();
        }
        _reachYear = int.MinValue;
        EmitSignal(SignalName.ConquestPlanChanged);
        return events;
    }

    readonly HashSet<int> _repaintProvinces = new();
    readonly List<int> _repaintCells = new();

    /// <summary>Notes land that changed hands, so the next redraw repaints just that.</summary>
    internal void MarkChanged(int provinceId, IEnumerable<int>? cells = null)
    {
        if (provinceId != 0)
            _repaintProvinces.Add(provinceId);
        if (cells != null)
            _repaintCells.AddRange(cells);
    }

    /// <summary>Repaints the cells of changed provinces and land, then updates the texture once.</summary>
    async Task RepaintChanged()
    {
        var provinces = _repaintProvinces.ToHashSet();
        var provinceCells = Provinces?.Cells;
        var cells = new List<int>(_repaintCells);
        if (provinces.Count > 0 && provinceCells != null)
            cells.AddRange(await Task.Run(() =>
            {
                var found = new List<int>();
                for (int i = 0; i < provinceCells.Length; i++)
                    if (provinces.Contains(provinceCells[i]))
                        found.Add(i);
                return found;
            }));
        foreach (int idx in cells)
            RepaintCell(idx % GridWidth, idx / GridWidth);
        _mapTexture!.Update(MapImage);
    }

    /// <summary>Redraws the map and realm names after land changed hands.</summary>
    public async Task RedrawTerritory()
    {
        TerritoryChanged = false;
        _reachYear = int.MinValue;
        if (MapSprite == null)
            return;
        if (MapImage != null && _mapTexture != null && (_repaintProvinces.Count > 0 || _repaintCells.Count > 0))
            await RepaintChanged();   // only what changed hands
        else
            await BuildFullMapImage(loadLand: false);
        _repaintProvinces.Clear();
        _repaintCells.Clear();
        var cells = Grid.Cells;
        BuildLabels(await Task.Run(() => CentroidsNow(cells)));
        RefreshArmyMarkers();
    }

    // --- War and peace for the player -----------------------------------------------

    /// <summary>A pretext: a realm whose land borders yours (disputes are never short of reasons).</summary>
    public bool HasPretext(int attacker, int defender)
    {
        if (Population == null)
            return false;
        var (reach, _) = ComputeReach(attacker);
        foreach (int i in Population.LandNodes)
            if (reach[i] && Population.NodeOwner[i] == defender)
                return true;
        return false;
    }

    /// <summary>Declares war for the player; returns the chronicle line, or null if it couldn't.</summary>
    public string? DeclareWar(int defender)
    {
        if (Diplomacy.CanDeclare(Game, PlayerRealmId, defender, DemoYear) != null)
            return null;
        bool pretext = HasPretext(PlayerRealmId, defender);
        Diplomacy.Declare(Game, PlayerRealmId, defender, DemoYear, pretext);
        EmitSignal(SignalName.ConquestPlanChanged);
        return pretext
            ? $"{RealmName(PlayerRealmId)} declares war on {RealmName(defender)} over their borders."
            : $"{RealmName(PlayerRealmId)} declares war on {RealmName(defender)} without a just cause. The world takes note.";
    }

    /// <summary>The player offers peace; returns the answer for the chronicle.</summary>
    public (bool Accepted, string Text) OfferPeace(int enemy, bool demandTribute)
    {
        var war = Game.Wars.Between(PlayerRealmId, enemy);
        if (war == null)
            return (false, "");
        if (!Diplomacy.Accepts(war, PlayerRealmId, DemoYear, demandTribute))
            return (false, $"{RealmName(enemy)} refuses peace{(demandTribute ? " on those terms" : "")}.");
        var (tax, tribute) = Economy.Revenue(CensusOf(enemy), Game.Realm(enemy).Tax);
        double paid = Diplomacy.MakePeace(Game, war, PlayerRealmId, DemoYear, demandTribute, tax + tribute);
        EmitSignal(SignalName.ConquestPlanChanged);
        return (true, demandTribute
            ? $"{RealmName(enemy)} sues for peace and pays {paid:N0} talents of tribute."
            : $"Peace between {RealmName(PlayerRealmId)} and {RealmName(enemy)}. Each keeps what it holds.");
    }
}
