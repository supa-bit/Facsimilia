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
    /// <summary>The pretext it was declared on (Pretexts.All), or "" for none.</summary>
    public string CasusBelli { get; init; } = "";
    /// <summary>For an ally's war: the main war it was called into (attacker, defender), else null.</summary>
    public (int Attacker, int Defender)? Supports { get; init; }
    public double Score { get; set; }
    /// <summary>War exhaustion of each side, 0 fresh .. 100 spent (decision "Playable 16").</summary>
    public double AttackerExhaustion { get; set; }
    public double DefenderExhaustion { get; set; }
    public double ExhaustionFor(int realm) => realm == Attacker ? AttackerExhaustion : DefenderExhaustion;
    public void AddExhaustion(int realm, double amount)
    {
        if (realm == Attacker)
            AttackerExhaustion = Math.Clamp(AttackerExhaustion + amount, 0, 100);
        else
            DefenderExhaustion = Math.Clamp(DefenderExhaustion + amount, 0, 100);
    }
    // Losses already counted into exhaustion.
    public double CountedAttackerLosses { get; set; }
    public double CountedDefenderLosses { get; set; }
    public double AttackerLosses { get; set; }   // men
    public double DefenderLosses { get; set; }
    /// <summary>Provinces each side has taken from the other in this war (realm id -> province ids).</summary>
    public Dictionary<int, HashSet<int>> Taken { get; } = new();

    public void RecordTaken(int realm, int province)
    {
        if (!Taken.TryGetValue(realm, out var set))
            Taken[realm] = set = new HashSet<int>();
        set.Add(province);
    }

    /// <summary>What this realm's enemy took from it in this war.</summary>
    public IEnumerable<int> LostBy(int realm) => Taken.TryGetValue(Enemy(realm), out var set) ? set : Enumerable.Empty<int>();

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
        ["al"] = AttackerLosses, ["dl"] = DefenderLosses, ["cb"] = CasusBelli,
        ["ae"] = AttackerExhaustion, ["de"] = DefenderExhaustion, ["cal"] = CountedAttackerLosses, ["cdl"] = CountedDefenderLosses,
        ["sup"] = Supports is { } sp ? new GArray { sp.Attacker, sp.Defender } : new GArray(),
        ["taken_a"] = new GArray(Taken.GetValueOrDefault(Attacker)?.Select(p => (Variant)p).ToArray() ?? Array.Empty<Variant>()),
        ["taken_d"] = new GArray(Taken.GetValueOrDefault(Defender)?.Select(p => (Variant)p).ToArray() ?? Array.Empty<Variant>()),
    };

    public static War FromDict(GDictionary d)
    {
        var sup = d.TryGetValue("sup", out var spv) ? spv.AsGodotArray() : new GArray();
        var w = new War(d["a"].AsInt32(), d["d"].AsInt32(), d["since"].AsInt32(), d["pretext"].AsBool())
        {
            CasusBelli = d.TryGetValue("cb", out var cb) ? cb.AsString() : "",
            Supports = sup.Count == 2 ? (sup[0].AsInt32(), sup[1].AsInt32()) : null,
            AttackerExhaustion = d.TryGetValue("ae", out var ae) ? ae.AsDouble() : 0,
            DefenderExhaustion = d.TryGetValue("de", out var de) ? de.AsDouble() : 0,
            CountedAttackerLosses = d.TryGetValue("cal", out var cal) ? cal.AsDouble() : 0,
            CountedDefenderLosses = d.TryGetValue("cdl", out var cdl) ? cdl.AsDouble() : 0,
            Score = d["score"].AsDouble(),
            AttackerLosses = d.TryGetValue("al", out var al) ? al.AsDouble() : 0,
            DefenderLosses = d.TryGetValue("dl", out var dl) ? dl.AsDouble() : 0,
        };
        foreach (var (key, realm) in new[] { ("taken_a", w.Attacker), ("taken_d", w.Defender) })
            if (d.TryGetValue(key, out var list))
                foreach (Variant p in list.AsGodotArray())
                    w.RecordTaken(realm, p.AsInt32());
        return w;
    }
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
        foreach (var ally in CalledInto(war).ToList())
        {
            _wars.Remove(ally);   // the allies' wars end with the main one
            _truceUntil[Pair(ally.Attacker, ally.Defender)] = year + TruceYears;
        }
    }
    public IReadOnlyList<War> All => _wars;

    public War? Between(int a, int b) => _wars.FirstOrDefault(w => w.Involves(a) && w.Involves(b));
    public bool AtWar(int a, int b) => Between(a, b) != null;
    public IEnumerable<War> Of(int realm) => _wars.Where(w => w.Involves(realm));
    public IEnumerable<int> EnemiesOf(int realm) => Of(realm).Select(w => w.Enemy(realm));

    public War Declare(int attacker, int defender, int year, bool pretext, string casusBelli = "",
        (int, int)? supports = null)
    {
        var existing = Between(attacker, defender);
        if (existing != null)
            return existing;
        var war = new War(attacker, defender, year, pretext) { CasusBelli = casusBelli, Supports = supports };
        _wars.Add(war);
        return war;
    }

    /// <summary>The allies' wars called into this one.</summary>
    public IEnumerable<War> CalledInto(War main) =>
        _wars.Where(w => w.Supports is { } s && s.Attacker == main.Attacker && s.Defender == main.Defender);

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
