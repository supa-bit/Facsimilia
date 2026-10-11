using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>A province's focus: the work it is turned to.</summary>
public sealed record ProvinceFocus(string Id, string Name, string[] Works, string RealmKey, double Boost, string Help);

/// <summary>
/// Provinces, part one (the Ledger topic "Provinces", your answers): a
/// focus that shifts the province's work slowly; each province's
/// historical seat (its largest town where history gives none); splitting
/// and joining your provinces at a cost of loyalty for a while; and each
/// province's own history, year by year.
/// </summary>
public partial class MapView
{
    /// <summary>The focuses (your answer: farming, herding, crafts, trade, mining, soldiers).</summary>
    public static readonly ProvinceFocus[] Focuses =
    {
        new("farming", "Farming", new[] { "field", "orchard" }, "", 0.10, "Fields and orchards yield a tenth more."),
        new("herding", "Herding", new[] { "herd" }, "", 0.15, "Herds yield more."),
        new("mining", "Mining", new[] { "mine", "earth" }, "", 0.15, "Mines and pits yield more."),
        new("crafts", "Crafts", Array.Empty<string>(), "crafts", 0.10, "Its workshops make more of the realm's made goods."),
        new("trade", "Trade", Array.Empty<string>(), "trade", 0.10, "Its merchants bring the realm more trade."),
        new("soldiers", "Soldiers", Array.Empty<string>(), "manpower", 0.15, "More of its men serve in the levies."),
    };
    /// <summary>Every other kind of land work loses this much at a full focus: hands move to the focus.</summary>
    public const double FocusCost = 0.05;
    /// <summary>A focus takes hold by this much a year (ten years to full).</summary>
    public const double FocusPerYear = 0.1;
    /// <summary>Splitting or joining provinces stirs this much unrest in each (loyalty, for a while).</summary>
    public const double RedrawUnrest = 0.2;
    /// <summary>A province must have this many cells (about 600 km²) to be split.</summary>
    public const int MinSplitCells = 1000;

    static readonly string[] LandWorks = { "field", "orchard", "herd", "gather", "forest", "mine", "earth" };

    public static ProvinceFocus? FocusById(string id) => Focuses.FirstOrDefault(f => f.Id == id);

    /// <summary>Turns one of your provinces to a focus (or none). The new focus starts from nothing and grows.</summary>
    public string? SetFocus(int provinceId, string focus)
    {
        if (Provinces == null || !Provinces.Provinces.TryGetValue(provinceId, out var p) || p.RealmId != PlayerRealmId)
            return "Only your own provinces.";
        if (focus != "" && FocusById(focus) == null)
            return "No such focus.";
        var ps = ProvinceStateOf(provinceId);
        if (ps.Focus == focus)
            return null;
        ps.Focus = focus;
        ps.FocusLevel = 0;
        if (focus != "")
            ps.Log(DemoYear, $"Turned to {FocusById(focus)!.Name.ToLowerInvariant()}.");
        _census = null;
        _goodsYear = int.MinValue;
        return null;
    }

    /// <summary>A province's multiplier on one kind of land work from its focus.</summary>
    static double FocusFactor(ProvinceState ps, string work)
    {
        if (ps.Focus == "" || FocusById(ps.Focus) is not { } f || ps.FocusLevel <= 0)
            return 1;
        return f.Works.Contains(work) ? 1 + f.Boost * ps.FocusLevel : 1 - FocusCost * ps.FocusLevel;
    }

    /// <summary>A province's share to a realm-wide effect (crafts, trade, manpower) from its focus.</summary>
    static double FocusRealmEffect(ProvinceState ps, string key) =>
        ps.Focus != "" && FocusById(ps.Focus) is { } f && f.RealmKey == key ? f.Boost * ps.FocusLevel : 0;

    /// <summary>The year's focuses take hold a little more, and each province's year is recorded for its graphs.</summary>
    void FocusYear()
    {
        if (Provinces == null)
            return;
        int years = Math.Max(1, Game.YearsPerTurn);
        foreach (var p in Provinces.Provinces.Values)
        {
            var ps = ProvinceStateOf(p.Id);
            ps.Record(DemoYear, ProvincePopulation(p.Id));
            if (ps.Focus != "" && ps.FocusLevel < 1)
                ps.FocusLevel = Math.Min(1, ps.FocusLevel + FocusPerYear * years);
        }
    }

    // --- Seats ----------------------------------------------------------------------------

    Dictionary<string, (string Name, double Lon, double Lat)>? _seats;

    /// <summary>The historical seats of the provinces about 300 BC (data/province_seats.json).</summary>
    Dictionary<string, (string Name, double Lon, double Lat)> HistorySeats()
    {
        if (_seats != null)
            return _seats;
        _seats = new();
        const string path = "res://data/province_seats.json";
        if (Godot.FileAccess.FileExists(path))
        {
            using var doc = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            foreach (var s in doc.RootElement.GetProperty("seats").EnumerateObject())
                _seats[s.Name] = (s.Value.GetProperty("name").GetString()!, s.Value.GetProperty("lon").GetDouble(), s.Value.GetProperty("lat").GetDouble());
        }
        return _seats;
    }

    /// <summary>
    /// A province's seat (your answer "Each province's seat"): its historical
    /// seat if it still lies in the province, otherwise its largest town.
    /// </summary>
    public string? ProvinceSeat(int provinceId)
    {
        if (Provinces == null || !Provinces.Provinces.TryGetValue(provinceId, out var p))
            return null;
        if (HistorySeats().TryGetValue(p.Name, out var seat) && ProvinceIdAtWorld(WorldAt(seat.Lon, seat.Lat)) == provinceId)
            return seat.Name;
        return TownsNow().Where(t => ProvinceIdAtWorld(t.World) == provinceId).OrderByDescending(t => t.Rank).Select(t => t.Name).FirstOrDefault();
    }

    // --- History --------------------------------------------------------------------------

    /// <summary>Writes a line into a province's history.</summary>
    internal void ProvinceLog(int provinceId, string text)
    {
        if (provinceId != 0)
            ProvinceStateOf(provinceId).Log(DemoYear, text);
    }

    // --- Splitting and joining ---------------------------------------------------------

    /// <summary>Why one of your provinces can't be split now, or null.</summary>
    public string? CanSplit(Province p) =>
        p.RealmId != PlayerRealmId ? "Only your own provinces."
        : p.CellCount < MinSplitCells ? "Too small to split."
        : Game.Sieges.Any(s => s.ProvinceId == p.Id) ? "Not while besieged." : null;

    /// <summary>
    /// Splits a province in two across its longest side (your answer
    /// "Redrawing your provinces"). The new half takes the name of its
    /// largest town; both are restless for a while. Returns the new province.
    /// </summary>
    public Province? SplitProvince(int provinceId)
    {
        if (Provinces == null || !Provinces.Provinces.TryGetValue(provinceId, out var p) || CanSplit(p) != null)
            return null;
        var cells = Provinces.Cells;
        int w = GridWidth;
        // The province's shape: its centre and longest axis.
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int n = 0;
        var mine = new List<int>(p.CellCount);
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] == provinceId)
            {
                mine.Add(i);
                double x = i % w, y = i / w;
                sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
                n++;
            }
        double mx = sx / n, my = sy / n;
        double cxx = sxx / n - mx * mx, cyy = syy / n - my * my, cxy = sxy / n - mx * my;
        double angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        double ax = Math.Cos(angle), ay = Math.Sin(angle);
        // Cut at the median along that axis, so the halves hold as many cells each.
        var along = mine.Select(i => (i % w - mx) * ax + (i / w - my) * ay).OrderBy(v => v).ToList();
        double cut = along[along.Count / 2];
        var piece = Provinces.Create(p.Name + " II", p.RealmId);
        foreach (int i in mine)
            if ((i % w - mx) * ax + (i / w - my) * ay > cut)
                Provinces.Assign(i, piece.Id);
        AfterRedraw();
        // Its people, faith and standing come from the old province; buildings stay where they were built.
        var old = ProvinceStateOf(p.Id);
        var fresh = ProvinceStateOf(piece.Id);
        fresh.Culture = old.Culture;
        fresh.Religion = old.Religion;
        fresh.Integration = old.Integration;
        fresh.Owner = old.Owner;
        fresh.Tax = old.Tax;
        fresh.Unrest = old.Unrest + RedrawUnrest;
        old.Unrest += RedrawUnrest;
        string? town = TownsNow().Where(t => ProvinceIdAtWorld(t.World) == piece.Id).OrderByDescending(t => t.Rank).Select(t => t.Name).FirstOrDefault();
        if (town != null && !Provinces.Provinces.Values.Any(x => x.Name == town))
            RenameProvince(piece.Id, town);
        old.Log(DemoYear, $"Split: {piece.Name} is made from its lands.");
        fresh.Log(DemoYear, $"Made from the lands of {p.Name}.");
        return piece;
    }

    /// <summary>Why two provinces can't be joined, or null: both yours, side by side.</summary>
    public string? CanJoin(Province a, Province b)
    {
        if (a.Id == b.Id)
            return "Pick another province.";
        if (a.RealmId != PlayerRealmId || b.RealmId != PlayerRealmId)
            return "Only your own provinces.";
        if (Game.Sieges.Any(s => s.ProvinceId == a.Id || s.ProvinceId == b.Id))
            return "Not while besieged.";
        return Neighbours(a.Id).Contains(b.Id) ? null : $"{b.Name} doesn't border {a.Name}.";
    }

    /// <summary>The provinces bordering one, worked out from the map's cells.</summary>
    public HashSet<int> Neighbours(int provinceId)
    {
        var result = new HashSet<int>();
        if (Provinces == null)
            return result;
        var cells = Provinces.Cells;
        int w = GridWidth;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != provinceId)
                continue;
            int x = i % w;
            if (x + 1 < w && cells[i + 1] != provinceId && cells[i + 1] != 0) result.Add(cells[i + 1]);
            if (x > 0 && cells[i - 1] != provinceId && cells[i - 1] != 0) result.Add(cells[i - 1]);
            if (i + w < cells.Length && cells[i + w] != provinceId && cells[i + w] != 0) result.Add(cells[i + w]);
            if (i - w >= 0 && cells[i - w] != provinceId && cells[i - w] != 0) result.Add(cells[i - w]);
        }
        return result;
    }

    /// <summary>
    /// Joins a neighbouring province of yours into this one: its land, people
    /// and buildings (the better of each) pass to it; both are restless for a while.
    /// </summary>
    public string? JoinProvince(int intoId, int otherId)
    {
        if (Provinces == null || !Provinces.Provinces.TryGetValue(intoId, out var into) || !Provinces.Provinces.TryGetValue(otherId, out var other))
            return "No such province.";
        if (CanJoin(into, other) is { } why)
            return why;
        var a = ProvinceStateOf(intoId);
        var b = ProvinceStateOf(otherId);
        foreach (var (id, level) in b.Buildings)
            a.Buildings[id] = Math.Max(a.Level(id), level);
        foreach (var work in b.Works)
            if (a.Works.Count < MaxQueue)
                a.Works.Add(work);
        a.Unrest = Math.Max(a.Unrest, b.Unrest) + RedrawUnrest;
        a.Integration = Math.Min(a.Integration, b.Integration);
        a.Log(DemoYear, $"Joined with {other.Name}.");
        var cells = Provinces.Cells;
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] == otherId)
                Provinces.Assign(i, intoId);
        Game.Provinces.Remove(otherId);
        AfterRedraw();
        SelectProvince(intoId);
        return null;
    }

    void AfterRedraw()
    {
        Provinces!.Recount();
        BuildProvinceTexture();
        BuildProvinceLabels();
        InvalidateProvincePeople();
        _nodeProvinceYear = int.MinValue;
        _provinceNeedsYear = int.MinValue;
        _census = null;
        _roadsDirty = true;
        EmitSignal(SignalName.ProvincesChanged);
    }
}
