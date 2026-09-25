using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// A war between two realms (decision "War and diplomacy": declared wars and
/// peace deals). Score runs from the attacker's point of view, -100 (lost)
/// to +100 (won): provinces taken and battles won raise it, and it decides
/// what peace the loser accepts (decision "Playable 16").
/// </summary>
public sealed class War
{
    public int Attacker { get; }
    public int Defender { get; }
    public int Since { get; }
    public bool Pretext { get; }              // declared with a good reason (decision "Playable 15")
    public double Score { get; set; }
    public double AttackerLosses { get; set; }   // men
    public double DefenderLosses { get; set; }

    public War(int attacker, int defender, int since, bool pretext)
    {
        Attacker = attacker;
        Defender = defender;
        Since = since;
        Pretext = pretext;
    }

    public bool Involves(int realm) => realm == Attacker || realm == Defender;
    public int Enemy(int realm) => realm == Attacker ? Defender : Attacker;

    /// <summary>The score from this realm's point of view.</summary>
    public double ScoreFor(int realm) => realm == Attacker ? Score : -Score;

    public GDictionary ToDict() => new()
    {
        ["a"] = Attacker, ["d"] = Defender, ["since"] = Since, ["pretext"] = Pretext, ["score"] = Score,
        ["al"] = AttackerLosses, ["dl"] = DefenderLosses,
    };

    public static War FromDict(GDictionary d) =>
        new(d["a"].AsInt32(), d["d"].AsInt32(), d["since"].AsInt32(), d["pretext"].AsBool())
        {
            Score = d["score"].AsDouble(),
            AttackerLosses = d.TryGetValue("al", out var al) ? al.AsDouble() : 0,
            DefenderLosses = d.TryGetValue("dl", out var dl) ? dl.AsDouble() : 0,
        };
}

/// <summary>Every war going on, and the truces after past ones.</summary>
public sealed class Wars
{
    /// <summary>Years after a peace before the same two realms may fight again.</summary>
    public const int TruceYears = 10;

    readonly List<War> _wars = new();
    readonly Dictionary<(int, int), int> _truceUntil = new();

    static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>The last year of a truce between two realms, or null.</summary>
    public int? TruceUntil(int a, int b, int year) =>
        _truceUntil.TryGetValue(Pair(a, b), out int until) && until >= year ? until : null;

    /// <summary>Ends a war with a truce.</summary>
    public void MakePeace(War war, int year)
    {
        _wars.Remove(war);
        _truceUntil[Pair(war.Attacker, war.Defender)] = year + TruceYears;
    }
    public IReadOnlyList<War> All => _wars;

    public War? Between(int a, int b) => _wars.FirstOrDefault(w => w.Involves(a) && w.Involves(b));
    public bool AtWar(int a, int b) => Between(a, b) != null;
    public IEnumerable<War> Of(int realm) => _wars.Where(w => w.Involves(realm));
    public IEnumerable<int> EnemiesOf(int realm) => Of(realm).Select(w => w.Enemy(realm));

    public War Declare(int attacker, int defender, int year, bool pretext)
    {
        var existing = Between(attacker, defender);
        if (existing != null)
            return existing;
        var war = new War(attacker, defender, year, pretext);
        _wars.Add(war);
        return war;
    }

    public void End(War war) => _wars.Remove(war);

    /// <summary>Ends every war a realm is in (it has been destroyed).</summary>
    public void EndAllOf(int realm) => _wars.RemoveAll(w => w.Involves(realm));

    public GArray ToArray()
    {
        var a = new GArray();
        foreach (var w in _wars)
            a.Add(w.ToDict());
        foreach (var ((x, y), until) in _truceUntil)
            a.Add(new GDictionary { ["truce"] = new GArray { x, y, until } });
        return a;
    }

    public void Load(GArray a)
    {
        _wars.Clear();
        _truceUntil.Clear();
        foreach (Variant v in a)
        {
            var d = v.AsGodotDictionary();
            if (d.TryGetValue("truce", out var t))
            {
                var p = t.AsGodotArray();
                _truceUntil[(p[0].AsInt32(), p[1].AsInt32())] = p[2].AsInt32();
            }
            else
                _wars.Add(War.FromDict(d));
        }
    }
}
