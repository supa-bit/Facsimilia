using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.UI;

namespace Facsimilia.World;

/// <summary>
/// Wonders (the Ledger topic "Buildings and works", from your answers):
/// the Seven Wonders and a list through the ages, each once in the world.
/// No one starts a wonder before history did; the realm that built it in
/// history starts it on history's date if it holds the place, and takes
/// history's years. You may start any of them from that date, in any of
/// your provinces, and take it first: whoever finishes first has it, and
/// the others' work stops. A wonder costs silver, workers (taken from
/// manpower while it is built) and special goods your realm must have.
/// Each has its own effect for the realm holding it. Wonders fall on
/// history's dates; only yours can be saved, for half their price.
/// </summary>
public partial class MapView
{
    /// <summary>The province at a wonder's place, or 0 (off the map or at sea).</summary>
    int WonderProvince(WonderDef w)
    {
        if (Population == null || Provinces == null)
            return 0;
        int node = Population.NodeAtLonLat(w.Lon, w.Lat, LonMin, LonMax, LatMin, LatMax);
        if (node < 0)
            return 0;
        // A place on the shore may fall in the sea: take the nearest land.
        var np = NodeProvinces();
        int width = Population.Width;
        for (int r = 0; r <= 3; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = node % width + dx, y = node / width + dy;
                    if (x >= 0 && y >= 0 && x < width && y < Population.Height && np[y * width + x] != 0)
                        return np[y * width + x];
                }
        return 0;
    }

    /// <summary>History's builder still begins a wonder this many years after history finished it (if it could not before).</summary>
    public const int LateBuilderYears = 50;

    int CivRealm(string? civ) => civ != null && CivRealmIds.TryGetValue(civ, out int id) ? id : 0;

    /// <summary>The wonders already standing in the game's first year, at their places.</summary>
    void StartWonders()
    {
        foreach (var w in WonderCatalog.Instance.All)
        {
            if (w.Fall is int f && f <= DemoYear)
                Game.FallenWonders.Add(w.Id);
            else if (w.Done <= DemoYear && WonderProvince(w) is int prov and > 0)
                Game.Wonders[w.Id] = new WonderSite { Province = prov, Year = w.Done };
        }
        RefreshWonderEffects();
    }

    /// <summary>Whether the wonder is still to be built: not standing, not fallen.</summary>
    public bool WonderOpen(WonderDef w) => !Game.Wonders.ContainsKey(w.Id) && !Game.FallenWonders.Contains(w.Id);

    /// <summary>Why a realm can't start a wonder in a province now, or null.</summary>
    public string? CanBuildWonder(Province p, WonderDef w, int? realmId = null, double hurry = 0)
    {
        int realm = realmId ?? PlayerRealmId;
        var (silver, workers, _) = WonderPrice(w, hurry);
        var rs = Game.Realm(realm);
        if (!WonderOpen(w))
            return Game.FallenWonders.Contains(w.Id) ? "It has fallen." : "It already stands.";
        if (DemoYear < w.Start)
            return $"No one builds it before history did ({ThemeAncient.YearText(w.Start)}).";
        if (p.RealmId != realm)
            return "Only in your own provinces.";
        if (Game.WonderWorks.Any(x => x.Id == w.Id && x.Realm == realm))
            return "Already under way.";
        if (w.Needs != null && !ProvinceHas(p.Id, w.Needs))
            return w.Needs == "coast" ? "Needs a coast." : "Can't be built here.";
        if (rs.Treasury < silver)
            return $"Not enough silver: {Money(silver)} needed.";
        if (rs.Manpower < workers)
            return $"Needs {workers:N0} workers from your manpower ({rs.Manpower:N0} free).";
        var missing = MissingWonderGoods(realm, w);
        if (missing.Count > 0)
            return "Your realm needs " + string.Join(", ", missing) + ".";
        return null;
    }

    /// <summary>The special goods a realm lacks for a wonder (it must make or import some of each).</summary>
    List<string> MissingWonderGoods(int realmId, WonderDef w)
    {
        var missing = new List<string>();
        var cat = GoodsCatalog();
        var goods = CensusOf(realmId).Goods;
        if (cat == null || goods == null)
            return missing;
        foreach (var id in w.Goods)
        {
            var g = cat.Goods.FirstOrDefault(x => x.Id == id);
            if (g != null && goods.Produced[g.Index] + goods.Imported[g.Index] < 0.01)
                missing.Add(g.Name.ToLowerInvariant());
        }
        return missing;
    }

    /// <summary>Hurrying (your answer "Hurrying a wonder"): at most this much faster...</summary>
    public const double HurryMaxFaster = 0.25;
    /// <summary>...at this many times the silver and workers.</summary>
    public const double HurryMaxCost = 3;

    /// <summary>What a wonder costs and takes when hurried by a share from 0 (not at all) to 1 (a quarter faster at triple the cost).</summary>
    public static (double Silver, double Workers, int Years) WonderPrice(WonderDef w, double hurry = 0)
    {
        hurry = Math.Clamp(hurry, 0, 1);
        double factor = 1 + (HurryMaxCost - 1) * hurry;
        return (w.Cost * factor, w.Workers * factor, Math.Max(1, (int)Math.Ceiling(w.Years * (1 - HurryMaxFaster * hurry))));
    }

    /// <summary>Starts a wonder: silver paid, workers taken from manpower; hurrying costs more and takes less time.</summary>
    public bool StartWonder(Province p, WonderDef w, int? realmId = null, double hurry = 0)
    {
        int realm = realmId ?? PlayerRealmId;
        if (CanBuildWonder(p, w, realm, hurry) != null)
            return false;
        var rs = Game.Realm(realm);
        var (silver, workers, years) = WonderPrice(w, hurry);
        rs.Treasury -= silver;
        rs.Manpower -= workers;
        Game.WonderWorks.Add(new WonderWork { Id = w.Id, Realm = realm, Province = p.Id, Left = years, Workers = workers, Paid = silver });
        _census = null;
        return true;
    }

    /// <summary>
    /// A year of wonders: history's builders begin on their dates, work goes
    /// on (halted while besieged or lost), the first finished stands and
    /// stops the rest, and wonders fall on history's dates unless you save yours.
    /// </summary>
    internal List<ChronicleEvent> WondersYear()
    {
        var events = new List<ChronicleEvent>();
        if (Provinces == null)
            return events;
        var cat = WonderCatalog.Instance;
        // History's builders begin, if they hold the place and can bear it. A wonder whose
        // builder isn't a realm of the game is begun by whoever holds the place (your answer).
        foreach (var w in cat.All)
        {
            int builder = w.Builder != null ? CivRealm(w.Builder) : WonderProvince(w) is int at and > 0 && Provinces.Provinces.TryGetValue(at, out var held) ? held.RealmId : 0;
            if (builder <= 0 || builder == PlayerRealmId || w.Start > DemoYear || DemoYear > w.Done + LateBuilderYears
                || !WonderOpen(w) || Game.WonderWorks.Any(x => x.Id == w.Id && x.Realm == builder))
                continue;
            int prov = WonderProvince(w);
            if (prov == 0 || !Provinces.Provinces.TryGetValue(prov, out var p) || p.RealmId != builder)
                continue;
            var rs = Game.Realm(builder);
            // History's own: the builder finds the silver and men (into debt if need be), but not the goods it lacks.
            if (MissingWonderGoods(builder, w).Count > 0)
                continue;
            rs.Treasury -= w.Cost;
            double men = Math.Min(w.Workers, Math.Max(0, rs.Manpower));
            rs.Manpower -= men;
            Game.WonderWorks.Add(new WonderWork { Id = w.Id, Realm = builder, Province = prov, Left = w.Years, Workers = men, Paid = w.Cost });
            events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"{RealmName(builder)} begins the {w.Name}.",
                builder == PlayerRealmId ? "moment:wonder_begun" : null));
        }
        // Work goes on: halted while besieged, abandoned if the province is lost.
        int years = Math.Max(1, Game.YearsPerTurn);
        foreach (var work in Game.WonderWorks.ToList())
        {
            if (!Provinces.Provinces.TryGetValue(work.Province, out var p) || p.RealmId != work.Realm)
            {
                EndWonderWork(work, false);
                if (work.Realm == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"With the province lost, the work on the {cat[work.Id]?.Name} stops."));
                continue;
            }
            if (!Game.Sieges.Any(s => s.ProvinceId == work.Province))
                work.Left = Math.Max(0, work.Left - years);
        }
        // The first finished stands; ties go to the one begun first.
        foreach (var work in Game.WonderWorks.Where(x => x.Left == 0).ToList())
        {
            var w = cat[work.Id];
            if (w == null || !Game.WonderWorks.Contains(work) || !WonderOpen(w))
                continue;
            Game.Wonders[w.Id] = new WonderSite { Province = work.Province, Year = DemoYear };
            ProvinceLog(work.Province, $"The {w.Name} is finished by {RealmName(work.Realm)}.");
            EndWonderWork(work, true);
            string where = Provinces.Provinces.TryGetValue(work.Province, out var at) ? at.Name : "";
            if (work.Realm == PlayerRealmId)
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"The {w.Name} is finished in {where}, a wonder of the world.", $"wonder_done:{w.Id}|moment:building_done"));
            else
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"{RealmName(work.Realm)} finishes the {w.Name}.", $"wonder_done:{w.Id}"));
            foreach (var rival in Game.WonderWorks.Where(x => x.Id == w.Id).ToList())
            {
                EndWonderWork(rival, false);
                if (rival.Realm == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId,
                        $"{RealmName(work.Realm)} finished the {w.Name} first; our work stops, and half the silver comes back."));
                else if (work.Realm == PlayerRealmId)
                    events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"{RealmName(rival.Realm)} abandons its own {w.Name}."));
            }
        }
        // Falls on history's dates; only yours can be saved.
        foreach (var w in cat.All)
        {
            if (w.Fall is not int fall || fall > DemoYear || !Game.Wonders.TryGetValue(w.Id, out var site) || site.Saved)
                continue;
            int holder = Provinces.Provinces.TryGetValue(site.Province, out var p) ? p.RealmId : 0;
            double save = w.Cost * WonderCatalog.SaveShare;
            // Yours: you are asked each time (your answer "Saving a wonder from its fall"); it stands while you decide.
            if (holder == PlayerRealmId && !site.Asked)
            {
                site.Asked = true;
                Game.Offers.Add(new Offer { Kind = WonderSaveOffer, From = PlayerRealmId, Subject = w.Id, Year = DemoYear, Silver = save,
                    Text = $"History would have the {w.Name} {w.FallWhy}. Your engineers can save it" });
                events.Add(new ChronicleEvent(ChronicleKind.Economy, PlayerRealmId, $"The {w.Name} is in danger: decide in Diplomacy whether to save it for {Money(save)}."));
                continue;
            }
            if (Game.Offers.Any(o => o.Kind == WonderSaveOffer && o.Subject == w.Id))
                continue;
            FallWonder(w, events);
        }
        RefreshWonderEffects();
        return events;
    }

    public const string WonderSaveOffer = "wonder_save";

    void FallWonder(WonderDef w, List<ChronicleEvent> events)
    {
        Game.Wonders.Remove(w.Id);
        Game.FallenWonders.Add(w.Id);
        events.Add(new ChronicleEvent(ChronicleKind.Disaster, PlayerRealmId, $"The {w.Name} falls: {w.FallWhy}.", "moment:wonder_fell"));
    }

    /// <summary>Your answer to saving a wonder: pay and it stands for good, or let it fall as history had.</summary>
    internal string? AnswerWonderSave(Offer offer, bool accept)
    {
        if (WonderCatalog.Instance[offer.Subject] is not { } w || !Game.Wonders.TryGetValue(w.Id, out var site))
            return null;
        if (accept)
        {
            if (PlayerState.Treasury < offer.Silver)
            {
                Game.Offers.Add(offer);
                return $"Saving the {w.Name} needs {Money(offer.Silver)}.";
            }
            PlayerState.Treasury -= offer.Silver;
            site.Saved = true;
            RefreshWonderEffects();
            return $"Your engineers save the {w.Name} for {Money(offer.Silver)}.";
        }
        var events = new List<ChronicleEvent>();
        FallWonder(w, events);
        RefreshWonderEffects();
        return events[0].Text;
    }

    /// <summary>Ends a work: the workers come home (a few lost); an abandoned work gives back half its silver.</summary>
    void EndWonderWork(WonderWork work, bool finished)
    {
        Game.WonderWorks.Remove(work);
        var rs = Game.Realm(work.Realm);
        rs.Manpower += work.Workers * (1 - WonderCatalog.WorkersLost);
        if (!finished)
            rs.Treasury += work.Paid * WonderCatalog.Refund;
    }

    /// <summary>Each realm's effects from its ruler's skills and traits.</summary>
    internal void RefreshRulerEffects()
    {
        foreach (var r in Game.Realms.Values)
        {
            r.RulerEffects.Clear();
            if (Registry.Realms.TryGetValue(r.RealmId, out var realm) && Registry.Characters.TryGetValue(realm.RulerId, out var ruler) && ruler.IsAlive)
                foreach (var (k, v) in Registry.RulerEffects(ruler, DemoYear))
                    r.RulerEffects[k] = v;
        }
    }

    /// <summary>Each realm's effects from the wonders standing in its provinces.</summary>
    internal void RefreshWonderEffects()
    {
        RefreshRulerEffects();
        foreach (var r in Game.Realms.Values)
            r.WonderEffects.Clear();
        if (Provinces == null)
            return;
        foreach (var (id, site) in Game.Wonders)
        {
            if (WonderCatalog.Instance[id] is not { } w || !Provinces.Provinces.TryGetValue(site.Province, out var p)
                || p.RealmId <= 0 || !Game.Realms.TryGetValue(p.RealmId, out var rs))
                continue;
            foreach (var (key, v) in w.Effects)
                rs.WonderEffects[key] = rs.WonderEffects.GetValueOrDefault(key) + v;
        }
    }

    /// <summary>The wonders a realm holds now.</summary>
    public List<WonderDef> WondersOf(int realmId) =>
        Provinces == null ? new List<WonderDef>()
        : Game.Wonders.Where(kv => Provinces.Provinces.TryGetValue(kv.Value.Province, out var p) && p.RealmId == realmId)
            .Select(kv => WonderCatalog.Instance[kv.Key]).OfType<WonderDef>().ToList();

    /// <summary>The wonders standing in a province.</summary>
    public List<WonderDef> WondersIn(int provinceId) =>
        Game.Wonders.Where(kv => kv.Value.Province == provinceId).Select(kv => WonderCatalog.Instance[kv.Key]).OfType<WonderDef>().ToList();
}
