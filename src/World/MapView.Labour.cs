using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Labour on the map: every region's free, dependent and enslaved people
/// as history had them (data/labour.json), captives taken in war, and
/// their sale.
/// </summary>
public partial class MapView
{
    LabourHistory? _labour;
    int _labourYear = int.MinValue;
    double[] _dependentShare = Array.Empty<double>(), _enslavedShare = Array.Empty<double>();

    public LabourHistory? LabourHistory()
    {
        if (_labour == null && Godot.FileAccess.FileExists(Facsimilia.Game.LabourHistory.Path))
            _labour = Facsimilia.Game.LabourHistory.Parse(Godot.FileAccess.GetFileAsString(Facsimilia.Game.LabourHistory.Path));
        return _labour;
    }

    /// <summary>This year's dependent and enslaved shares by region id.</summary>
    (double[] Dependent, double[] Enslaved) LabourShares()
    {
        var h = LabourHistory();
        if (Population == null || h == null)
            return (Array.Empty<double>(), Array.Empty<double>());
        if (_labourYear != DemoYear || _dependentShare.Length != Population.RegionCount + 1)
        {
            int n = Population.RegionCount + 1;
            _dependentShare = new double[n];
            _enslavedShare = new double[n];
            foreach (var r in Population.Regions)
                if (r.Id > 0 && r.Id < n)
                    (_dependentShare[r.Id], _enslavedShare[r.Id]) = h.Shares(r.Name, DemoYear);
            _labourYear = DemoYear;
        }
        return (_dependentShare, _enslavedShare);
    }

    public double EnslavedShareOfRegion(int region)
    {
        var (_, e) = LabourShares();
        return region >= 0 && region < e.Length ? e[region] : 0;
    }

    /// <summary>Counts every realm's free, dependent and enslaved people into the census.</summary>
    void CountLabour(Dictionary<int, RealmCensus> census)
    {
        var (dep, ens) = LabourShares();
        if (Population == null || dep.Length == 0)
            return;
        foreach (int i in Population.LandNodes)
        {
            int owner = Population.NodeOwner[i];
            if (owner <= 0 || !census.TryGetValue(owner, out var c))
                continue;
            double p = Population.Pop[i];
            int r = Population.RegionOf(i);
            c.Dependent += p * dep[r];
            c.Enslaved += p * ens[r];
            c.Free += p * (1 - dep[r] - ens[r]);
        }
        foreach (var (id, c) in census)
            if (Game.Realms.TryGetValue(id, out var s))
                c.Enslaved += s.Captives;
    }

    /// <summary>The mines' output per region (index = region id): the enslaved worked them.</summary>
    double[]? MineFactors()
    {
        var (_, ens) = LabourShares();
        return ens.Length == 0 ? null : ens.Select(Labour.MineFactor).ToArray();
    }

    /// <summary>The region a province lies in (by its label point), or 0.</summary>
    int RegionOfProvince(Province p)
    {
        if (Population == null)
            return 0;
        int nx = (int)(p.LabelCell.X * Population.Width / GridWidth), ny = (int)(p.LabelCell.Y * Population.Height / GridHeight);
        return Population.RegionOf(Math.Clamp(ny * Population.Width + nx, 0, Population.Pop.Length - 1));
    }

    /// <summary>A conqueror carries off part of a taken province's people as captives (as ancient armies did).</summary>
    void TakeCaptives(int attacker, Province p)
    {
        var h = LabourHistory();
        if (h == null || Population == null)
            return;
        var region = Population.Regions.FirstOrDefault(r => r.Id == RegionOfProvince(p));
        if (region != null && h.Abolished(region.Name, DemoYear))
            return;
        Game.Realm(attacker).Captives += ProvincePopulation(p.Id) * h.CaptiveShare;
    }

    /// <summary>
    /// The yearly labour step: captives die or are freed, and those beyond
    /// what a realm's households and mines can use are sold abroad.
    /// </summary>
    internal List<ChronicleEvent> LabourYear()
    {
        var events = new List<ChronicleEvent>();
        var h = LabourHistory();
        if (h == null)
            return events;
        var census = RealmCensus();
        foreach (var (id, s) in Game.Realms)
        {
            s.LastCaptiveSales = 0;
            if (s.Captives <= 0)
                continue;
            s.Captives *= 1 - h.CaptiveLoss;
            double people = census.TryGetValue(id, out var c) ? c.People : 0;
            double sold = Math.Max(0, s.Captives - h.KeptShare * people);
            if (sold < 1)
                continue;
            s.Captives -= sold;
            s.LastCaptiveSales = sold * h.CaptivePrice / 6000;
            s.Treasury += s.LastCaptiveSales;
            if (id == PlayerRealmId && sold >= 1000)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, id,
                    $"{ThemeGroup(sold)} captives are sold in the slave markets for {s.LastCaptiveSales:N0} talents."));
        }
        return events;
    }

    static string ThemeGroup(double x) => ((long)Math.Round(x)).ToString("N0");
}
