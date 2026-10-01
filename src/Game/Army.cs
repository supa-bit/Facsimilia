using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>
/// One unit of an army (your answers: units of any size from 100 to 5,000
/// men, each with its own experience and origin, and a roster showing
/// them): a type, its men, how hardened they are, the province they were
/// raised in, and its wounded, who return over the months.
/// </summary>
public sealed class Regiment
{
    public const int MinMen = 100, MaxMen = 5000;

    public int Type { get; set; }
    public int Men { get; set; }
    /// <summary>0 raw recruits .. 1 hardened veterans.</summary>
    public double Xp { get; set; }
    /// <summary>The province it was raised in (0 if unknown).</summary>
    public int Origin { get; set; }
    /// <summary>Wounded men out of the line, healing.</summary>
    public int Wounded { get; set; }

    public GArray ToArray(UnitCatalog cat) => new() { cat[Type].Id, Men, Xp, Origin, Wounded };

    public static Regiment? FromArray(GArray a, UnitCatalog cat) =>
        cat.Has(a[0].AsString())
            ? new Regiment { Type = cat[a[0].AsString()].Index, Men = a[1].AsInt32(), Xp = a[2].AsDouble(), Origin = a[3].AsInt32(), Wounded = a[4].AsInt32() }
            : null;
}

/// <summary>
/// A named army (decision "Playable 9": named armies based in provinces):
/// where it stands (a population node, inside a province or on open land),
/// and how many of each unit it has. Fleets are armies too: warships sail
/// with the army they belong to and carry it across the sea.
/// </summary>
public sealed class Army
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    /// <summary>The population node it stands on.</summary>
    public int Node { get; set; }
    /// <summary>Its units, each with its own men, experience and origin.</summary>
    public List<Regiment> Regiments { get; } = new();
    readonly int _types;
    /// <summary>0 rested .. 1 exhausted: campaigning and sieges wear an army down.</summary>
    public double Fatigue { get; set; }

    /// <summary>
    /// Strength of each unit type, in standard units (its men divided by the
    /// type's usual size): what Might, upkeep and the battle map count.
    /// </summary>
    public double[] Units
    {
        get
        {
            var eq = new double[_types];
            var cat = UnitCatalog.Instance;
            foreach (var r in Regiments)
                eq[r.Type] += r.Men / (double)cat[r.Type].Men;
            return eq;
        }
    }

    /// <summary>Each type's experience, weighted by men (0 where it has none).</summary>
    public double[] UnitXp
    {
        get
        {
            var xp = new double[_types];
            var men = new double[_types];
            foreach (var r in Regiments)
            {
                xp[r.Type] += r.Xp * r.Men;
                men[r.Type] += r.Men;
            }
            for (int i = 0; i < _types; i++)
                xp[i] = men[i] > 0 ? xp[i] / men[i] : 0;
            return xp;
        }
    }

    /// <summary>The army's experience, weighted by men. Setting it sets every unit's.</summary>
    public double Experience
    {
        get
        {
            double men = Regiments.Sum(r => (double)r.Men);
            return men > 0 ? Regiments.Sum(r => r.Xp * r.Men) / men : 0;
        }
        set
        {
            foreach (var r in Regiments)
                r.Xp = Math.Clamp(value, 0, 1);
        }
    }

    /// <summary>Hardens every unit by an amount (after a battle).</summary>
    public void Harden(double amount)
    {
        foreach (var r in Regiments)
            r.Xp = Math.Min(1, r.Xp + amount);
    }

    /// <summary>Raises a new unit of raw recruits: the type's usual size unless told, kept within 100-5,000 men.</summary>
    public Regiment AddRegiment(int type, int men = 0, double xp = 0, int origin = 0)
    {
        if (men <= 0)
            men = UnitCatalog.Instance[type].Men;
        var r = new Regiment { Type = type, Men = Math.Clamp(men, Regiment.MinMen, Regiment.MaxMen), Xp = xp, Origin = origin };
        Regiments.Add(r);
        return r;
    }

    /// <summary>Adds standard units of a type (each the type's usual size).</summary>
    public void AddRaw(int index, int count = 1, int origin = 0)
    {
        for (int k = 0; k < count; k++)
            AddRegiment(index, origin: origin);
    }

    /// <summary>Sets a type to exactly this many standard units (for set-ups and tests).</summary>
    public void SetUnits(int index, int count)
    {
        Regiments.RemoveAll(r => r.Type == index);
        AddRaw(index, count);
    }

    /// <summary>Removes standard units of a type (smallest units first); returns the men removed.</summary>
    public int RemoveUnits(int index, int count = 1)
    {
        int men = 0;
        for (int k = 0; k < count; k++)
        {
            var r = Regiments.Where(x => x.Type == index).OrderBy(x => x.Men).FirstOrDefault();
            if (r == null)
                break;
            men += r.Men;
            Regiments.Remove(r);
        }
        return men;
    }

    /// <summary>Moves standard units of a type (whole units, with their experience) into another army.</summary>
    public void MoveUnits(Army to, int index, int count)
    {
        for (int k = 0; k < count; k++)
        {
            var r = Regiments.Where(x => x.Type == index).OrderByDescending(x => x.Men).FirstOrDefault();
            if (r == null)
                return;
            Regiments.Remove(r);
            to.Regiments.Add(r);
        }
    }

    /// <summary>
    /// Splits a unit into two equal halves (your answer: splits always make
    /// even numbers). Fails if the halves would be under 100 men.
    /// </summary>
    public bool Split(Regiment r)
    {
        if (!Regiments.Contains(r) || r.Men / 2 < Regiment.MinMen)
            return false;
        int half = r.Men / 2;
        var twin = new Regiment { Type = r.Type, Men = r.Men - half, Xp = r.Xp, Origin = r.Origin, Wounded = r.Wounded / 2 };
        r.Men = half;
        r.Wounded -= twin.Wounded;
        Regiments.Insert(Regiments.IndexOf(r) + 1, twin);
        return true;
    }

    /// <summary>
    /// Merges two units of the same type into one (your answer: experience
    /// averaged by men). Fails if they differ in type or would pass 5,000 men.
    /// </summary>
    public bool Merge(Regiment a, Regiment b)
    {
        if (a == b || a.Type != b.Type || !Regiments.Contains(a) || !Regiments.Contains(b) || a.Men + b.Men > Regiment.MaxMen)
            return false;
        a.Xp = (a.Xp * a.Men + b.Xp * b.Men) / (a.Men + b.Men);
        a.Men += b.Men;
        a.Wounded += b.Wounded;
        if (a.Origin != b.Origin && b.Men > a.Men - b.Men)
            a.Origin = b.Origin;
        Regiments.Remove(b);
        return true;
    }

    /// <summary>The dead not yet taken from their home provinces (origin province -> men), drained by the map each month.</summary>
    public Dictionary<int, double> PendingDead { get; } = new();

    /// <summary>
    /// Losses in battle (your answers: the dead are lost to the population,
    /// and the wounded are separate from the dead): each unit loses a share
    /// of its men; deadShare of them die, the rest are wounded and heal.
    /// Units under 100 men fold into another of their kind, or are lost.
    /// Returns the men lost to the line.
    /// </summary>
    public int TakeLosses(double share, double deadShare, Random rng, Domain? only = null)
    {
        var cat = UnitCatalog.Instance;
        int total = 0;
        foreach (var r in Regiments.ToList())
        {
            if (only != null && cat[r.Type].Domain != only)
                continue;
            double expected = r.Men * share;
            int lost = (int)Math.Floor(expected);
            if (rng.NextDouble() < expected - lost)
                lost++;
            lost = Math.Min(lost, r.Men);
            int dead = (int)Math.Round(lost * deadShare);
            r.Men -= lost;
            r.Wounded += lost - dead;
            total += lost;
            PendingDead[r.Origin] = PendingDead.GetValueOrDefault(r.Origin) + dead;
        }
        Tidy();
        return total;
    }

    /// <summary>A month of healing: about a quarter of the wounded return to the line, a few die of their wounds.</summary>
    public void Heal(Random rng)
    {
        foreach (var r in Regiments)
        {
            if (r.Wounded <= 0)
                continue;
            int died = (int)Math.Round(r.Wounded * WoundDeaths);
            int back = (int)Math.Ceiling((r.Wounded - died) * WoundReturn);
            r.Wounded -= died + back;
            r.Men = Math.Min(Regiment.MaxMen, r.Men + back);
            PendingDead[r.Origin] = PendingDead.GetValueOrDefault(r.Origin) + died;
        }
    }

    /// <summary>Each month a quarter of the wounded return, and 3% of them die of their wounds.</summary>
    public const double WoundReturn = 0.25, WoundDeaths = 0.03;

    /// <summary>
    /// Keeps the number of units even (your answer: armies hold an even number
    /// of units, and splits always make even numbers): splits the largest unit
    /// that can be halved, or else merges the two smallest of one kind.
    /// </summary>
    public void Even()
    {
        if (Regiments.Count % 2 == 0)
            return;
        var big = Regiments.Where(r => r.Men >= 2 * Regiment.MinMen).OrderByDescending(r => r.Men).FirstOrDefault();
        if (big != null && Split(big))
            return;
        foreach (var g in Regiments.GroupBy(r => r.Type))
        {
            var two = g.OrderBy(r => r.Men).Take(2).ToList();
            if (two.Count == 2 && Merge(two[0], two[1]))
                return;
        }
    }

    /// <summary>Folds units under 100 men into another of their kind, or disbands them.</summary>
    public void Tidy()
    {
        foreach (var r in Regiments.Where(x => x.Men < Regiment.MinMen).ToList())
        {
            var into = Regiments.FirstOrDefault(x => x != r && x.Type == r.Type && x.Men >= Regiment.MinMen && x.Men + r.Men <= Regiment.MaxMen);
            if (into != null && r.Men > 0)
            {
                into.Xp = (into.Xp * into.Men + r.Xp * r.Men) / (into.Men + r.Men);
                into.Men += r.Men;
                into.Wounded += r.Wounded;
                Regiments.Remove(r);
            }
            else if (r.Men <= 0 && r.Wounded <= 0)
                Regiments.Remove(r);
            else if (r.Men < Regiment.MinMen && into == null && r.Men + r.Wounded < Regiment.MinMen)
            {
                PendingDead[r.Origin] = PendingDead.GetValueOrDefault(r.Origin);   // the few left go home
                Regiments.Remove(r);
            }
        }
        Even();
    }

    /// <summary>The mercenary company this army is (GameState.Companies id), or 0 for the realm's own troops.</summary>
    public int CompanyId { get; set; }
    /// <summary>Victories not yet rewarded (a mercenary company's bonus is added at the month's pay).</summary>
    public int VictoriesUnpaid { get; set; }
    /// <summary>
    /// How attached the army is to its general, 0..1 (decision 'Armies loyal to
    /// their general'): it grows with the years he leads it.
    /// </summary>
    public double GeneralLoyalty { get; set; }
    /// <summary>The army's standard (an emblem id), chosen by you.</summary>
    public string Standard { get; set; } = "";
    /// <summary>Its general (RealmState.Generals id), or 0.</summary>
    public int GeneralId { get; set; }
    /// <summary>The nodes still to march through, in order (decision "Next 3": marching by months on plotted routes).</summary>
    public List<int> Route { get; } = new();
    /// <summary>Kilometres of march already made toward the next node.</summary>
    public double MarchCarry { get; set; }
    /// <summary>Months of food in the baggage train (decision "Next 4": supply lines and baggage).</summary>
    public double Supply { get; set; } = 4;
    public bool Marching => Route.Count > 0;

    /// <summary>What the realm's technology adds to each role's Might (set each year; not saved).</summary>
    public double[] RoleBoost { get; } = new double[UnitRoles.Count];

    /// <summary>Not besieging this year: rests faster.</summary>
    public bool Resting { get; set; } = true;

    public Army(int unitCount) => _types = unitCount;

    /// <summary>How many units it has.</summary>
    public int Count => Regiments.Count;
    /// <summary>Men in the line (not counting the wounded).</summary>
    public int Men => Regiments.Sum(r => r.Men);
    public bool IsEmpty => Regiments.All(r => r.Men <= 0);

    /// <summary>Strength of one role, in standard units.</summary>
    public double RoleCount(UnitCatalog cat, int role) =>
        Regiments.Where(r => cat[r.Type].Role == role).Sum(r => r.Men / (double)cat[r.Type].Men);

    public GDictionary ToDict(UnitCatalog cat)
    {
        return new GDictionary { ["id"] = Id, ["name"] = Name, ["node"] = Node, ["fatigue"] = Fatigue, ["general"] = GeneralId,
            ["regiments"] = new GArray(Regiments.Select(r => (Variant)r.ToArray(cat)).ToArray()),
            ["company"] = CompanyId, ["victories_unpaid"] = VictoriesUnpaid, ["gen_loyalty"] = GeneralLoyalty, ["standard"] = Standard,
            ["route"] = new GArray(Route.Select(n => (Variant)n).ToArray()), ["carry"] = MarchCarry, ["supply"] = Supply };
    }

    public static Army FromDict(GDictionary d, UnitCatalog cat)
    {
        var a = new Army(cat.Count)
        {
            Id = d["id"].AsInt32(), Name = d["name"].AsString(), Node = d["node"].AsInt32(),
            Fatigue = d.TryGetValue("fatigue", out var f) ? f.AsDouble() : 0,
            GeneralId = d.TryGetValue("general", out var g) ? g.AsInt32() : 0,
            MarchCarry = d.TryGetValue("carry", out var mc) ? mc.AsDouble() : 0,
            Supply = d.TryGetValue("supply", out var sp) ? sp.AsDouble() : 4,
            CompanyId = d.TryGetValue("company", out var co) ? co.AsInt32() : 0,
            VictoriesUnpaid = d.TryGetValue("victories_unpaid", out var vu) ? vu.AsInt32() : 0,
            GeneralLoyalty = d.TryGetValue("gen_loyalty", out var gl) ? gl.AsDouble() : 0,
            Standard = d.TryGetValue("standard", out var st) ? st.AsString() : "",
        };
        if (d.TryGetValue("route", out var route))
            foreach (var n in route.AsGodotArray())
                a.Route.Add(n.AsInt32());
        if (d.TryGetValue("regiments", out var regs))
        {
            foreach (var v in regs.AsGodotArray())
                if (Regiment.FromArray(v.AsGodotArray(), cat) is { } r)
                    a.Regiments.Add(r);
        }
        else
        {
            // A save from before units had their own size: whole units of the usual size.
            var ux = d.TryGetValue("unit_xp", out var u) ? u.AsGodotDictionary() : new GDictionary();
            double oldXp = d.TryGetValue("xp", out var x) ? x.AsDouble() : 0;
            foreach (var (k, v) in d["units"].AsGodotDictionary())
                if (cat.Has(k.AsString()))
                    for (int n = 0; n < v.AsInt32(); n++)
                        a.AddRegiment(cat[k.AsString()].Index, xp: ux.TryGetValue(k, out var e) ? e.AsDouble() : oldXp);
        }
        return a;
    }
}
