using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Moving the capital (Cities and settlements): the player picks a
/// province's seat; years later the court arrives there, and the
/// administrative reach (distance from the capital) centres on it at once.
/// See Game/CapitalMove.cs.
/// </summary>
public partial class MapView
{
    /// <summary>Where a province's seat stands: history's seat if it lies in the province, else its chief town.</summary>
    public (string Name, double Lon, double Lat)? SeatPlace(int provinceId)
    {
        if (Provinces == null || !Provinces.Provinces.TryGetValue(provinceId, out var p))
            return null;
        if (HistorySeats().TryGetValue(p.Name, out var seat) && ProvinceIdAtWorld(WorldAt(seat.Lon, seat.Lat)) == provinceId)
            return (seat.Name, seat.Lon, seat.Lat);
        (string Name, double Lon, double Lat, double Rank)? best = null;
        foreach (var (place, name) in PlaceCatalog.Instance.StandingIn(DemoYear))
        {
            if (ProvinceIdAtWorld(WorldAt(place.Lon, place.Lat)) != provinceId)
                continue;
            if (best == null || place.Links > best.Value.Rank)
                best = (name, place.Lon, place.Lat, place.Links);
        }
        return best is { } b ? (b.Name, b.Lon, b.Lat) : null;
    }

    /// <summary>The province holding a realm's capital now, or 0.</summary>
    public int CapitalProvince(int realmId)
    {
        int node = CapitalNode(realmId);
        return node < 0 ? 0 : ProvinceOfNode(node);
    }

    /// <summary>What moving the player's capital costs now.</summary>
    public double CapitalMoveCost() =>
        CapitalMove.Cost(RealmCensus().TryGetValue(PlayerRealmId, out var c) ? c.People : 0);

    /// <summary>Why the player can't move the capital to this province now, or null.</summary>
    public string? CanMoveCapital(int provinceId)
    {
        if (Provinces?.Provinces.TryGetValue(provinceId, out var p) != true || p!.RealmId != PlayerRealmId)
            return "Only to one of your own provinces.";
        var s = PlayerState;
        if (s.MovingCapitalYears > 0)
            return $"The court is already moving to {s.MovingCapitalName}.";
        if (CapitalProvince(PlayerRealmId) == provinceId)
            return "Your capital is already here.";
        if (SeatPlace(provinceId) == null)
            return "No town here to be a capital.";
        if (Game.Sieges.Any(x => x.ProvinceId == provinceId))
            return "Not while it is besieged.";
        double cost = CapitalMoveCost();
        if (s.Treasury < cost)
            return $"Needs {cost:0} talents.";
        return null;
    }

    /// <summary>Begins moving the player's capital: the silver is spent now, the court arrives in CapitalMove.Years.</summary>
    public bool StartCapitalMove(int provinceId)
    {
        if (CanMoveCapital(provinceId) != null)
            return false;
        var seat = SeatPlace(provinceId)!.Value;
        var s = PlayerState;
        s.Treasury -= CapitalMoveCost();
        s.MovingCapitalTo = provinceId;
        s.MovingCapitalName = seat.Name.TrimStart('★', ' ');
        s.MovingCapitalLon = seat.Lon;
        s.MovingCapitalLat = seat.Lat;
        s.MovingCapitalYears = CapitalMove.Years;
        ProvinceLog(provinceId, $"Building begins on a new capital at {s.MovingCapitalName}.");
        return true;
    }

    /// <summary>Once a year: a move under way goes on, and when it ends the capital is the new town.</summary>
    internal List<ChronicleEvent> CapitalYear()
    {
        var events = new List<ChronicleEvent>();
        foreach (var (id, s) in Game.Realms)
        {
            if (s.MovingCapitalYears <= 0)
                continue;
            // Lost the province on the way: the move is abandoned and the silver gone.
            if (Provinces?.Provinces.TryGetValue(s.MovingCapitalTo, out var p) != true || p!.RealmId != id)
            {
                events.Add(new ChronicleEvent(ChronicleKind.Economy, id, $"The new capital at {s.MovingCapitalName} is lost before the court could move."));
                s.MovingCapitalYears = 0;
                continue;
            }
            if (--s.MovingCapitalYears > 0)
                continue;
            int oldProvince = CapitalProvince(id);
            if (oldProvince != 0)
            {
                var old = ProvinceStateOf(oldProvince);
                old.Grudge = Math.Max(old.Grudge, CapitalMove.OldCapitalGrudge);
                old.Log(DemoYear, "The court leaves for a new capital.");
            }
            s.CapitalLon = s.MovingCapitalLon;
            s.CapitalLat = s.MovingCapitalLat;
            s.CapitalName = s.MovingCapitalName;
            s.RivalStrength = Math.Clamp(s.RivalStrength + CapitalMove.RivalGain, 0, 100);
            ProvinceLog(s.MovingCapitalTo, $"{s.CapitalName} becomes the capital.");
            events.Add(new ChronicleEvent(ChronicleKind.Economy, id, $"The court moves to {s.CapitalName}, the new capital of {RealmName(id)}."));
            _toCapital.Clear();   // roads now lead to the new capital
            RefreshCapitals();
        }
        return events;
    }
}
