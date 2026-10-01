using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Facsimilia.Game;

/// <summary>A hiring ground where companies wait for employers (data/mercenaries.json).</summary>
public sealed record HiringGround(string Id, string Name, double Lon, double Lat);

/// <summary>A historical company as the data describes it.</summary>
public sealed record CompanySpec(string Id, string Name, int From, int To, string Ground, IReadOnlyDictionary<string, int> Units,
    string? CaptainName, int[]? CaptainSkills, string[]? CaptainPerks, double PayMin, double PayMax, double Bonus);

public sealed class MercenaryCatalog
{
    public const string DataPath = "res://data/mercenaries.json";
    static MercenaryCatalog? _instance;
    public static MercenaryCatalog Instance => _instance ??= Load();

    public IReadOnlyList<HiringGround> Grounds { get; }
    public IReadOnlyList<CompanySpec> Companies { get; }

    MercenaryCatalog(List<HiringGround> grounds, List<CompanySpec> companies)
    {
        Grounds = grounds;
        Companies = companies;
    }

    public HiringGround Ground(string id) => Grounds.First(g => g.Id == id);

    static MercenaryCatalog Load()
    {
        using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(DataPath));
        var grounds = doc.RootElement.GetProperty("grounds").EnumerateArray()
            .Select(g => new HiringGround(g.GetProperty("id").GetString()!, g.GetProperty("name").GetString()!,
                g.GetProperty("lon").GetDouble(), g.GetProperty("lat").GetDouble())).ToList();
        var companies = new List<CompanySpec>();
        foreach (var c in doc.RootElement.GetProperty("companies").EnumerateArray())
        {
            var units = new Dictionary<string, int>();
            foreach (var u in c.GetProperty("units").EnumerateObject())
                units[u.Name] = u.Value.GetInt32();
            string? cn = null;
            int[]? cs = null;
            string[]? cp = null;
            if (c.TryGetProperty("captain", out var cap))
            {
                cn = cap.GetProperty("name").GetString();
                cs = cap.GetProperty("skills").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                cp = cap.GetProperty("perks").EnumerateArray().Select(x => x.GetString()!).ToArray();
            }
            var pay = c.GetProperty("pay").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            companies.Add(new CompanySpec(c.GetProperty("id").GetString()!, c.GetProperty("name").GetString()!,
                c.GetProperty("from").GetInt32(), c.GetProperty("to").GetInt32(), c.GetProperty("ground").GetString()!, units,
                cn, cs, cp, pay[0], pay[1], c.GetProperty("bonus").GetDouble()));
        }
        return new MercenaryCatalog(grounds, companies);
    }
}

/// <summary>
/// A mercenary company (decision "Next 8" and its follow-ups): named men of
/// one homeland under a captain, waiting at their hiring ground or serving
/// whoever pays. Hired, it is an army of its employer; unpaid, it grows angry
/// and deserts, changes sides or revolts.
/// </summary>
public sealed class Company
{
    public int Id { get; init; }
    public string SpecId { get; init; } = "";   // a historical company's id, or "" for a generated one
    public string Name { get; set; } = "";
    public string Ground { get; set; } = "";
    /// <summary>Its units while waiting at its ground (when hired, its army holds them).</summary>
    public List<Regiment> Regiments { get; set; } = new();
    /// <summary>The captain (serves as the army's general while hired).</summary>
    public General Captain { get; set; } = null!;
    public double PayMin { get; set; }
    public double PayMax { get; set; }
    /// <summary>What it asks now, as a multiple of the troops' own upkeep (redrawn each year inside the range).</summary>
    public double PayFactor { get; set; }
    public double BonusMonths { get; set; }
    /// <summary>The realm it serves, or 0 while waiting at its hiring ground.</summary>
    public int Employer { get; set; }
    public int ArmyId { get; set; }
    /// <summary>Months of pay owed and not paid.</summary>
    public double Arrears { get; set; }
    /// <summary>Months it has served its employer.</summary>
    public int Months { get; set; }
    /// <summary>The last year it can be found (a generated company waiting too long disbands).</summary>
    public int Until { get; set; }
    public bool Elite => PayMax > PayMin * 1.3;

    /// <summary>A month's pay in talents for the troops it has.</summary>
    public double MonthlyPay(IEnumerable<Regiment> regs) =>
        regs.Sum(r => r.Men / (double)UnitCatalog.Instance[r.Type].Men * UnitCatalog.Instance[r.Type].Upkeep) / 12.0 * PayFactor;

    public static int Men(IEnumerable<Regiment> regs) => regs.Sum(r => r.Men);

    public GDictionary ToDict()
    {
        var cat = UnitCatalog.Instance;
        return new GDictionary
        {
            ["id"] = Id, ["spec"] = SpecId, ["name"] = Name, ["ground"] = Ground, ["captain"] = Captain.ToDict(),
            ["regiments"] = new GArray(Regiments.Select(r => (Variant)r.ToArray(cat)).ToArray()),
            ["pay"] = new GArray { PayMin, PayMax, PayFactor, BonusMonths }, ["employer"] = Employer, ["army"] = ArmyId,
            ["arrears"] = Arrears, ["months"] = Months, ["until"] = Until,
        };
    }

    public static Company FromDict(GDictionary d)
    {
        var cat = UnitCatalog.Instance;
        var pay = d["pay"].AsGodotArray();
        var c = new Company
        {
            Id = d["id"].AsInt32(), SpecId = d["spec"].AsString(), Name = d["name"].AsString(), Ground = d["ground"].AsString(),
            Captain = General.FromDict(d["captain"].AsGodotDictionary()),
            PayMin = pay[0].AsDouble(), PayMax = pay[1].AsDouble(), PayFactor = pay[2].AsDouble(), BonusMonths = pay[3].AsDouble(),
            Employer = d["employer"].AsInt32(), ArmyId = d["army"].AsInt32(), Arrears = d["arrears"].AsDouble(),
            Months = d["months"].AsInt32(), Until = d["until"].AsInt32(),
        };
        if (d.TryGetValue("regiments", out var regs))
        {
            foreach (var v in regs.AsGodotArray())
                if (Regiment.FromArray(v.AsGodotArray(), cat) is { } r)
                    c.Regiments.Add(r);
        }
        else if (d.TryGetValue("units", out var units))
            foreach (var (k, v) in units.AsGodotDictionary())
                if (cat.Has(k.AsString()))
                    for (int n = 0; n < v.AsInt32(); n++)
                        c.Regiments.Add(new Regiment { Type = cat[k.AsString()].Index, Men = cat[k.AsString()].Men, Xp = 0.3 });
        return c;
    }
}
