using System;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.World;

/// <summary>
/// Realms born in the middle of a game: a captain's lordship now, and later
/// history's new realms, pretenders and the sides of civil wars. A new realm
/// gets a ruler and house, its land, a seat where it stands, and whatever
/// army it was born with; from then on it is a realm like any other.
/// </summary>
public partial class MapView
{
    /// <summary>
    /// Founds a realm on one province. Its key ("risen:7") stands in for the
    /// civilisation key the realms of 300 BC have, so the save finds it again.
    /// </summary>
    public int FoundRealm(string name, string rulerName, int rulerBorn, Culture culture, int province, int seatNode)
    {
        var ruler = Registry.CreateCharacter(rulerName, "male", rulerBorn, culture: culture);
        Registry.CreateDynasty("House of " + rulerName, ruler);
        var realm = Registry.CreateRealm(name, ruler, SuccessionLaw.MalePreferencePrimogeniture, NewRealmColor(name), culture);
        var s = Game.Realm(realm.Id);
        s.CivKey = $"risen:{realm.Id}";
        CivRealmIds[s.CivKey] = realm.Id;
        if (seatNode >= 0)
        {
            var (lon, lat) = NodeLonLat(seatNode);
            s.CapitalLon = lon;
            s.CapitalLat = lat;
            s.CapitalName = ProvinceName(province);
        }
        if (province != 0 && Provinces!.Provinces.TryGetValue(province, out var p))
        {
            if (p.RealmId > 0)
                Game.RecordLoss(p.RealmId, province, DemoYear);
            Provinces.SetRealm(province, realm.Id, Grid);
            MarkChanged(province);
            TerritoryChanged = true;
            _census = null;
            SyncPopulationOwnership();
        }
        return realm.Id;
    }

    /// <summary>A muted colour of its own for a new realm, from its name.</summary>
    static Color NewRealmColor(string name)
    {
        uint h = (uint)name.Aggregate(17, (a, ch) => a * 31 + ch);
        return Color.FromHsv(h % 360 / 360f, 0.35f + h % 7 * 0.03f, 0.62f + h % 5 * 0.04f);
    }

    /// <summary>The naming culture a new realm takes: its province's people's where it is one of the court cultures, else the given one.</summary>
    Culture CultureOfProvince(int province, Culture otherwise)
    {
        if (Game.Provinces.TryGetValue(province, out var ps) && Enum.TryParse<Culture>(ps.Culture, true, out var c))
            return c;
        return otherwise;
    }
}
