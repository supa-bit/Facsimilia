using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

public enum TreatyKind { Alliance, Vassal, Coalition }

/// <summary>
/// A treaty between two realms (decision "Playable 17": alliances and
/// vassals in the first playable version). Alliance: each defends the
/// other when attacked. Vassal: A is overlord, B the vassal: B pays a
/// share of its income, fights in A's wars and makes none of its own.
/// Coalition: A and B band together against an aggressor (Against).
/// </summary>
public sealed record Treaty(TreatyKind Kind, int A, int B, int Since, int Against = 0)
{
    public bool Involves(int realm) => A == realm || B == realm;
    public int Other(int realm) => realm == A ? B : A;

    public GDictionary ToDict() => new() { ["kind"] = (int)Kind, ["a"] = A, ["b"] = B, ["since"] = Since, ["against"] = Against };

    public static Treaty FromDict(GDictionary d) =>
        new((TreatyKind)d["kind"].AsInt32(), d["a"].AsInt32(), d["b"].AsInt32(), d["since"].AsInt32(),
            d.TryGetValue("against", out var ag) ? ag.AsInt32() : 0);
}

public sealed class Treaties
{
    /// <summary>A vassal's yearly tribute: this share of its income.</summary>
    public const double VassalTribute = 0.1;

    readonly List<Treaty> _list = new();
    public IReadOnlyList<Treaty> All => _list;

    public bool Allied(int a, int b) => _list.Any(t => t.Kind == TreatyKind.Alliance && t.Involves(a) && t.Involves(b));
    public int OverlordOf(int realm) => _list.FirstOrDefault(t => t.Kind == TreatyKind.Vassal && t.B == realm)?.A ?? 0;
    public IEnumerable<int> VassalsOf(int realm) => _list.Where(t => t.Kind == TreatyKind.Vassal && t.A == realm).Select(t => t.B);
    public IEnumerable<int> AlliesOf(int realm) =>
        _list.Where(t => t.Kind == TreatyKind.Alliance && t.Involves(realm)).Select(t => t.Other(realm));
    /// <summary>Coalition members standing together against an aggressor.</summary>
    public IEnumerable<int> CoalitionAgainst(int aggressor) =>
        _list.Where(t => t.Kind == TreatyKind.Coalition && t.Against == aggressor).SelectMany(t => new[] { t.A, t.B }).Distinct();

    public void Add(Treaty t)
    {
        if (!_list.Any(x => x.Kind == t.Kind && x.Involves(t.A) && x.Involves(t.B) && x.Against == t.Against))
            _list.Add(t);
    }

    public void Remove(TreatyKind kind, int a, int b) =>
        _list.RemoveAll(t => t.Kind == kind && t.Involves(a) && t.Involves(b));

    /// <summary>A realm that is gone keeps no treaties.</summary>
    public void RemoveAllOf(int realm) => _list.RemoveAll(t => t.Involves(realm) || t.Against == realm);

    public void RemoveWhere(Predicate<Treaty> match) => _list.RemoveAll(match);

    public GArray ToArray()
    {
        var a = new GArray();
        foreach (var t in _list)
            a.Add(t.ToDict());
        return a;
    }

    public void Load(GArray a)
    {
        _list.Clear();
        foreach (Variant v in a)
            _list.Add(Treaty.FromDict(v.AsGodotDictionary()));
    }
}
