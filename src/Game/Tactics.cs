using System;
using System.Collections.Generic;
using System.Linq;

namespace Facsimilia.Game;

/// <summary>The ground of one square of the battle map.</summary>
public enum Tile { Open, Hills, Forest, Marsh, River, Water }

/// <summary>
/// One body of troops on the battle map: all of one side's units of an arm,
/// or part of them when there are many.
/// </summary>
public sealed class Block
{
    public int Id { get; init; }
    public int Side { get; init; }          // 0 the attacker, 1 the defender
    public int Role { get; init; }
    public string Name { get; init; } = "";
    public int Men { get; init; }
    public double Start { get; init; }      // strength at the start
    public double Strength { get; set; }
    public double Morale { get; set; } = 1;
    public int X { get; set; }
    public int Y { get; set; }
    public bool Moved { get; set; }
    public bool Acted { get; set; }
    public bool Routed { get; set; }
    public bool Alive => !Routed && Strength > 0;
    public double Share => Start > 0 ? Strength / Start : 0;
}

/// <summary>
/// The tactical battle (decision "Next 1": battles on a battle map, on land
/// and at sea). A grid whose ground comes from where the armies met; each
/// side's arms in blocks; turns in which every block may move and then
/// strike. Ground, the arms matched against each other, flanks, morale and
/// the generals decide it. A side breaks when most of it has fled. The same
/// rules play the enemy's turns, and the player's when they let their
/// general decide.
/// </summary>
public sealed class TacticalBattle
{
    public const int Width = 20, Height = 12;
    public const int MaxRounds = 15;
    public static readonly int[] MoveOf = { 2, 3, 2, 4, 4, 2, 3 };    // by role
    public static readonly int[] RangeOf = { 1, 1, 3, 1, 2, 1, 1 };

    public Tile[,] Map { get; } = new Tile[Width, Height];
    public List<Block> Blocks { get; } = new();
    public bool Naval { get; }
    public int Round { get; private set; } = 1;
    /// <summary>Whose turn it is: 0 the attacker, 1 the defender.</summary>
    public int Turn { get; private set; }
    public int? Winner { get; private set; }
    public List<string> Log { get; } = new();
    public string[] SideNames { get; }
    public double[] Tactics { get; }
    readonly Random _rng;

    public TacticalBattle(BattleSide att, BattleSide def, Ground ground, bool naval, int seed)
    {
        _rng = new Random(seed);
        Naval = naval;
        SideNames = new[] { att.RealmName, def.RealmName };
        int Skill(General? g) => g == null ? 3 : naval ? g.Skill(Skills.Seamanship) : g.Skill(Skills.Tactics);
        Tactics = new[] { 1 + Battle.TacticsStep * (Skill(att.General) - 5), 1 + Battle.TacticsStep * (Skill(def.General) - 5) };
        MakeGround(ground);
        Deploy(att, 0);
        Deploy(def, 1);
        if (def.Extra > 0 && !naval)
            AddBlock(1, UnitRoles.HeavyInfantry, "Levies and garrison", (int)(def.Extra * 1000), def.Extra, Width - 3, Height / 2);
    }

    // --- The ground --------------------------------------------------------------------

    void MakeGround(Ground g)
    {
        if (Naval)
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    Map[x, y] = Tile.Water;
            return;
        }
        void Blobs(Tile t, double share)
        {
            int cells = (int)(Math.Min(share, 1) * Width * Height * 0.28);
            while (cells > 0)
            {
                int cx = _rng.Next(2, Width - 2), cy = _rng.Next(Height), r = _rng.Next(1, 3);
                for (int x = cx - r; x <= cx + r; x++)
                    for (int y = cy - r; y <= cy + r; y++)
                        if (x >= 0 && y >= 0 && x < Width && y < Height && Map[x, y] == Tile.Open && _rng.NextDouble() < 0.75)
                        {
                            Map[x, y] = t;
                            cells--;
                        }
            }
        }
        Blobs(Tile.Hills, g.Hills);
        Blobs(Tile.Forest, g.Forest);
        Blobs(Tile.Marsh, g.Marsh);
        if (g.River > 0.3)
        {
            int x = Width / 2 + _rng.Next(-2, 2);
            for (int y = 0; y < Height; y++)
            {
                Map[x, y] = Tile.River;
                if (_rng.NextDouble() < 0.3)
                    x = Math.Clamp(x + (_rng.Next(2) == 0 ? -1 : 1), 4, Width - 5);
            }
        }
    }

    public static int MoveCost(Tile t, int role) => t switch
    {
        Tile.Open => 1,
        Tile.Water => role == UnitRoles.Warships ? 1 : 99,
        Tile.Forest when role == UnitRoles.LightInfantry => 1,
        Tile.Hills when role == UnitRoles.LightInfantry => 1,
        _ => 2,
    };

    // --- Deployment --------------------------------------------------------------------

    int _nextId = 1;

    Block AddBlock(int side, int role, string name, int men, double strength, int x, int y)
    {
        var b = new Block { Id = _nextId++, Side = side, Role = role, Name = name, Men = men, Start = strength, Strength = strength, X = x, Y = y };
        Blocks.Add(b);
        return b;
    }

    void Deploy(BattleSide side, int s)
    {
        var cat = UnitCatalog.Instance;
        var might = Battle.RoleMight(side);
        var men = new double[UnitRoles.Count];
        var names = new string[UnitRoles.Count];
        var best = new double[UnitRoles.Count];
        foreach (var (a, share) in side.Armies)
        {
            var units = a.Units;
            for (int i = 0; i < units.Length; i++)
                if (units[i] > 0)
                {
                    int r = cat[i].Role;
                    men[r] += units[i] * cat[i].Men * share;
                    if (units[i] * share > best[r])
                    {
                        best[r] = units[i] * share;
                        names[r] = cat[i].Name;
                    }
                }
        }
        if (Naval)
        {
            might[UnitRoles.Warships] = side.Armies.Sum(x => Military.RawMight(x.Army, Domain.Naval) * x.Share);
            for (int r = 0; r < UnitRoles.Count; r++)
                if (r != UnitRoles.Warships)
                    might[r] = 0;
        }
        else
            might[UnitRoles.Warships] = 0;
        // The line: heavy foot in the centre, light troops and archers before it, horse and elephants on the wings.
        int front = s == 0 ? 3 : Width - 4, back = s == 0 ? 2 : Width - 3, forward = s == 0 ? 4 : Width - 5;
        var slots = new Queue<int>(new[] { Height / 2, Height / 2 - 1, Height / 2 + 1, Height / 2 - 2, Height / 2 + 2, Height / 2 - 3, Height / 2 + 3 });
        var wings = new Queue<int>(new[] { 1, Height - 2, 2, Height - 3, 0, Height - 1 });
        for (int r = 0; r < UnitRoles.Count; r++)
        {
            if (might[r] <= 0.01)
                continue;
            int n = Math.Clamp((int)Math.Ceiling(men[r] / 6000.0), 1, r == UnitRoles.HeavyInfantry || r == UnitRoles.Warships ? 5 : 2);
            for (int k = 0; k < n; k++)
            {
                int x, y;
                if (r is UnitRoles.Cavalry or UnitRoles.HorseArchers or UnitRoles.Elephants)
                {
                    x = front;
                    y = wings.Count > 0 ? wings.Dequeue() : _rng.Next(Height);
                }
                else if (r is UnitRoles.LightInfantry or UnitRoles.Missile)
                {
                    x = r == UnitRoles.Missile ? back : forward;
                    y = slots.Count > 0 ? slots.Peek() + k : Height / 2;
                }
                else
                {
                    x = front;
                    y = slots.Count > 0 ? slots.Dequeue() : _rng.Next(Height);
                }
                int tries = 0;
                while (BlockAt(x, y) != null || (!Naval && Map[x, y] == Tile.River))
                {
                    y = (y + 1) % Height;
                    if (++tries % Height == 0)
                        x = Math.Clamp(x + (s == 0 ? -1 : 1), 0, Width - 1);
                    if (tries > Height * Width)
                        break;
                }
                string label = (names[r] ?? UnitRoles.Names[r]) + (n > 1 ? $" {Roman(k + 1)}" : "");
                AddBlock(s, r, label, (int)(men[r] / n), might[r] / n, x, y);
            }
        }
    }

    static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", _ => "V" };

    public Block? BlockAt(int x, int y) => Blocks.FirstOrDefault(b => b.Alive && b.X == x && b.Y == y);

    // --- Moving and fighting -------------------------------------------------------------

    static int Dist(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

    /// <summary>Squares a block can reach this turn (not through other blocks).</summary>
    public HashSet<(int X, int Y)> Reach(Block b)
    {
        var cost = new Dictionary<(int, int), int> { [(b.X, b.Y)] = 0 };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((b.X, b.Y));
        int move = MoveOf[b.Role];
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;
                    if (BlockAt(nx, ny) != null)
                        continue;
                    // Blocks can't slip past an enemy next to them (zones of control).
                    if (cost[(x, y)] > 0 && Blocks.Any(e => e.Alive && e.Side != b.Side && Dist(e.X, e.Y, x, y) == 1))
                        continue;
                    int c = cost[(x, y)] + MoveCost(Map[nx, ny], b.Role);
                    if (c > move || (cost.TryGetValue((nx, ny), out var old) && old <= c))
                        continue;
                    cost[(nx, ny)] = c;
                    queue.Enqueue((nx, ny));
                }
        }
        cost.Remove((b.X, b.Y));
        return cost.Keys.ToHashSet();
    }

    public bool CanMove(Block b, int x, int y) => b.Alive && !b.Moved && !b.Acted && b.Side == Turn && Winner == null && Reach(b).Contains((x, y));

    public bool Move(Block b, int x, int y)
    {
        if (!CanMove(b, x, y))
            return false;
        b.X = x;
        b.Y = y;
        b.Moved = true;
        return true;
    }

    public bool CanStrike(Block a, Block t) => a.Alive && t.Alive && !a.Acted && a.Side == Turn && t.Side != a.Side && Winner == null
        && Dist(a.X, a.Y, t.X, t.Y) <= RangeOf[a.Role];

    /// <summary>How hard one arm hits another (the old matchups: horse rides down skirmishers, elephants scatter horse, javelins turn elephants).</summary>
    public static double Edge(int a, int t) => (a, t) switch
    {
        (UnitRoles.Cavalry, UnitRoles.Missile or UnitRoles.LightInfantry) => 1.4,
        (UnitRoles.Cavalry, UnitRoles.HeavyInfantry) => 0.75,
        (UnitRoles.Cavalry or UnitRoles.HorseArchers, UnitRoles.Elephants) => 0.4,
        (UnitRoles.Elephants, UnitRoles.Cavalry or UnitRoles.HorseArchers) => 1.6,
        (UnitRoles.Elephants, UnitRoles.HeavyInfantry) => 1.3,
        (UnitRoles.Elephants, UnitRoles.LightInfantry) => 0.7,
        (UnitRoles.LightInfantry or UnitRoles.Missile, UnitRoles.Elephants) => 1.5,
        (UnitRoles.HeavyInfantry, UnitRoles.LightInfantry or UnitRoles.Missile) => 1.2,
        (UnitRoles.HeavyInfantry, UnitRoles.Cavalry) => 1.1,
        (UnitRoles.HorseArchers, UnitRoles.HeavyInfantry) => 1.1,
        _ => 1,
    };

    /// <summary>What ground does to a block fighting from it (attacking) or on it (defending).</summary>
    static double GroundHit(Tile from, int role) => from switch
    {
        Tile.River => 0.7,
        Tile.Marsh => role == UnitRoles.LightInfantry ? 0.9 : 0.65,
        Tile.Forest => role is UnitRoles.Cavalry or UnitRoles.HorseArchers or UnitRoles.Elephants ? 0.55 : role == UnitRoles.LightInfantry ? 1.2 : 0.8,
        Tile.Hills => role is UnitRoles.Cavalry or UnitRoles.Elephants ? 0.8 : 1.15,
        _ => 1,
    };

    static double GroundShield(Tile on, int role) => on switch
    {
        Tile.Hills => 0.75,
        Tile.Forest => role == UnitRoles.LightInfantry ? 0.7 : 0.85,
        _ => 1,
    };

    /// <summary>The expected blow, for the panels and the AI.</summary>
    public double Blow(Block a, Block t)
    {
        bool ranged = Dist(a.X, a.Y, t.X, t.Y) > 1;
        double hit = a.Strength * 0.2 * Edge(a.Role, t.Role) * Tactics[a.Side] * (0.5 + 0.5 * a.Morale);
        if (!Naval)
            hit *= GroundHit(Map[a.X, a.Y], a.Role) * GroundShield(Map[t.X, t.Y], t.Role);
        if (ranged)
            hit *= 0.55;
        // Flanks: every other enemy already on the target makes the blow heavier.
        int others = Blocks.Count(e => e.Alive && e.Side == a.Side && e != a && Dist(e.X, e.Y, t.X, t.Y) == 1);
        hit *= 1 + 0.3 * others;
        if (a.Role == UnitRoles.Cavalry && others > 0)
            hit *= 1.5;   // horse into a flank already held
        return hit;
    }

    public bool Strike(Block a, Block t)
    {
        if (!CanStrike(a, t))
            return false;
        bool ranged = Dist(a.X, a.Y, t.X, t.Y) > 1;
        int others = Blocks.Count(e => e.Alive && e.Side == a.Side && e != a && Dist(e.X, e.Y, t.X, t.Y) == 1);
        double dmg = Blow(a, t) * (0.85 + 0.3 * _rng.NextDouble());
        Hurt(t, dmg, others > 0);
        if (!ranged && t.Alive)
            Hurt(a, Blow(t, a) * 0.5 * (0.85 + 0.3 * _rng.NextDouble()), false);   // the enemy strikes back
        a.Acted = true;
        a.Moved = true;
        if (t.Routed)
            Note($"{SideNames[a.Side]}'s {a.Name} {(ranged ? "shoots down" : "routs")} {SideNames[t.Side]}'s {t.Name}" + (others > 0 ? ", taken in the flank" : "") + ".");
        CheckEnd();
        return true;
    }

    void Hurt(Block b, double dmg, bool flanked)
    {
        dmg = Math.Min(dmg, b.Strength);
        b.Strength -= dmg;
        b.Morale -= dmg / Math.Max(b.Start, 1e-9) * 1.6 + (flanked ? 0.08 : 0);
        if (b.Morale <= 0.2 || b.Strength < 0.3 * b.Start)
        {
            b.Routed = true;
            foreach (var f in Blocks.Where(f => f.Alive && f.Side == b.Side && Dist(f.X, f.Y, b.X, b.Y) <= 2))
                f.Morale -= 0.06;   // the flight shakes those beside it
        }
    }

    void Note(string line)
    {
        if (Log.Count < 40)
            Log.Add(line);
    }

    /// <summary>Share of a side's starting strength still standing and unbroken.</summary>
    public double Standing(int side)
    {
        double start = Blocks.Where(b => b.Side == side).Sum(b => b.Start);
        return start > 0 ? Blocks.Where(b => b.Side == side && b.Alive).Sum(b => b.Strength) / start : 0;
    }

    void CheckEnd()
    {
        for (int s = 0; s < 2; s++)
            if (Standing(s) < 0.3)
            {
                Winner = 1 - s;
                Note($"{SideNames[s]}'s line breaks; {SideNames[1 - s]} holds the field.");
                return;
            }
    }

    /// <summary>Ends the side's turn; after the defender's, a new round. After the last round the stronger side holds the field.</summary>
    public void EndTurn()
    {
        if (Winner != null)
            return;
        foreach (var b in Blocks)
        {
            b.Moved = false;
            b.Acted = false;
        }
        if (Turn == 1)
        {
            Round++;
            foreach (var b in Blocks.Where(b => b.Alive))
                b.Morale = Math.Min(1, b.Morale + 0.02);
            if (Round > MaxRounds)
            {
                Winner = Standing(0) > Standing(1) * 1.2 ? 0 : 1;   // a drawn day is the defender's
                Note($"Night falls. {SideNames[Winner.Value]} keeps the field.");
                return;
            }
        }
        Turn = 1 - Turn;
    }

    // --- The commander's mind (the enemy, or your general) ------------------------------

    /// <summary>Plays the side whose turn it is: each block strikes what it can, else closes on the best target.</summary>
    public void PlayTurn()
    {
        int side = Turn;
        bool holding = side == 1 && Round <= 2 && !Naval;   // the defender waits on its ground at first
        foreach (var b in Blocks.Where(b => b.Alive && b.Side == side).OrderByDescending(b => RangeOf[b.Role]).ToList())
        {
            if (Winner != null)
                return;
            var target = BestTarget(b);
            if (target != null)
            {
                Strike(b, target);
                continue;
            }
            if (holding && b.Role is UnitRoles.HeavyInfantry)
                continue;
            var goal = Blocks.Where(e => e.Alive && e.Side != side)
                .OrderBy(e => Dist(b.X, b.Y, e.X, e.Y) / Edge(b.Role, e.Role)).FirstOrDefault();
            if (goal == null)
                break;
            var reach = Reach(b);
            if (reach.Count > 0)
            {
                // Closest square to the goal; missiles stay at their range.
                int want = RangeOf[b.Role] > 1 ? RangeOf[b.Role] : 1;
                var best = reach.OrderBy(p => Math.Abs(Dist(p.X, p.Y, goal.X, goal.Y) - want) * 10 - (Naval ? 0 : Map[p.X, p.Y] == Tile.Hills ? 1 : 0)).First();
                if (Math.Abs(Dist(best.X, best.Y, goal.X, goal.Y) - want) < Math.Abs(Dist(b.X, b.Y, goal.X, goal.Y) - want))
                    Move(b, best.X, best.Y);
            }
            target = BestTarget(b);
            if (target != null)
                Strike(b, target);
        }
        EndTurn();
    }

    Block? BestTarget(Block b) => Blocks.Where(t => CanStrike(b, t))
        .OrderByDescending(t => Blow(b, t) / Math.Max(t.Strength, 1e-9)).FirstOrDefault();

    /// <summary>Plays both sides to the end (the general decides).</summary>
    public void PlayOut()
    {
        int guard = 0;
        while (Winner == null && guard++ < 200)
            PlayTurn();
    }

    // --- The result ----------------------------------------------------------------------

    /// <summary>Share of each side's men lost: the fallen, and among the routed, those cut down in flight.</summary>
    public (double Attacker, double Defender) Losses()
    {
        double Side(int s)
        {
            double start = Blocks.Where(b => b.Side == s).Sum(b => b.Start);
            if (start <= 0)
                return 0;
            double lost = 0;
            foreach (var b in Blocks.Where(b => b.Side == s))
                lost += (b.Start - b.Strength) * 0.45 + (b.Routed ? b.Strength * 0.25 : 0);
            if (Winner == 1 - s)
                lost += start * 0.04;   // the pursuit
            return Math.Clamp(lost / start, 0.01, 0.9);
        }
        return (Side(0), Side(1));
    }
}
