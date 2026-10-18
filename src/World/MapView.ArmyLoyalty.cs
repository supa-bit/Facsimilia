using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;

namespace Facsimilia.World;

/// <summary>
/// Armies loyal to their general, and veterans waiting for land (War:
/// armies, units and generals). See Game/ArmyLoyalty.cs.
/// </summary>
public partial class MapView
{
    /// <summary>
    /// Once a year: armies grow loyal to the general who leads them; loyal
    /// armies may turn on a realm that owes them or leaves its veterans
    /// landless; veterans wait, grumble, and sometimes offer to take silver.
    /// The computer's realms settle their veterans at once.
    /// </summary>
    internal List<ChronicleEvent> ArmyLoyaltyYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        foreach (var (id, s) in Game.Realms)
        {
            foreach (var army in s.Armies.Where(a => a.CompanyId == 0))
            {
                if (army.GeneralId == 0)
                {
                    army.GeneralLoyalty = Math.Max(0, army.GeneralLoyalty - ArmyLoyalty.PerYear);
                    army.LoyalTo = 0;
                }
                else if (army.LoyalTo != army.GeneralId)
                {
                    army.LoyalTo = army.GeneralId;
                    army.GeneralLoyalty = 0;   // a new general must win them over
                }
                else
                    army.GeneralLoyalty = Math.Min(1, army.GeneralLoyalty
                        + ArmyLoyalty.PerYear * Math.Max(0, 1 + ReformCatalog.Instance.Effect(s, "loyalty_rate")));   // Marius' volunteers looked to their general
            }

            if (s.VeteransWaiting >= 1 && ReformCatalog.Instance.Effect(s, "veterans_paid") > 0
                && s.Treasury >= ArmyLoyalty.PayOffCost(s.VeteransWaiting))
            {
                s.Treasury -= ArmyLoyalty.PayOffCost(s.VeteransWaiting);   // Augustus' military treasury pays them in silver
                ArmyLoyalty.Clear(s);
            }
            if (s.VeteransWaiting >= 1)
            {
                if (id != PlayerRealmId)
                    SettleVeteransForComputer(id, s);
                else
                {
                    s.RivalStrength = Math.Clamp(s.RivalStrength + ArmyLoyalty.WaitingRivalPerYear, 0, 100);
                    if (!s.VeteransOfferSilver && rng.NextDouble() < ArmyLoyalty.PayOffChance(s.VeteransWaiting, s.VeteransXp, s.VeteransLed))
                    {
                        s.VeteransOfferSilver = true;
                        events.Add(new ChronicleEvent(ChronicleKind.Economy, id,
                            $"Your {s.VeteransWaiting:N0} veterans would take {ArmyLoyalty.PayOffCost(s.VeteransWaiting):N0} talents instead of land."));
                    }
                }
            }

            double pressure = ArmyLoyalty.Pressure(s);
            foreach (var army in s.Armies.Where(a => a.CompanyId == 0 && a.GeneralId != 0).ToList())
                if (rng.NextDouble() < ArmyLoyalty.MutinyChance(army.GeneralLoyalty, pressure) * (Guards.Has(army) ? Guards.MutinyCut : 1))
                    events.AddRange(Mutiny(id, s, army));
        }
        return events;
    }

    /// <summary>
    /// An army follows its general against the realm (Sulla's march on Rome):
    /// it is lost to you, its general with it, and the province it stands in
    /// breaks away under them, unless it is the capital's.
    /// </summary>
    internal List<ChronicleEvent> Mutiny(int realmId, RealmState s, Army army, bool lordship = false)
    {
        var events = new List<ChronicleEvent>();
        var general = s.GeneralOf(army);
        string who = general?.Name ?? "Its general";
        s.Armies.Remove(army);
        if (general != null)
            s.Generals.Remove(general);
        s.RivalStrength = Math.Clamp(s.RivalStrength + 10, 0, 100);
        s.LastMutinyYear = DemoYear;
        int province = army.Node >= 0 ? ProvinceOfNode(army.Node) : 0;
        string text = $"{who} and {army.Name} turn against {RealmName(realmId)}";
        if (province != 0 && Provinces!.Provinces.TryGetValue(province, out var p) && p.RealmId == realmId && province != CapitalProvince(realmId))
        {
            if (lordship && general != null)
            {
                // A captain with land and loyal men makes himself a lord (your answer "How far can a captain rise":
                // to a lordship or a throne), as the Mamertines took Messana in 288 BC.
                var culture = CultureOfProvince(province, Registry.Realms.TryGetValue(realmId, out var old) ? old.Culture : Culture.Greek);
                int lord = FoundRealm($"Lordship of {p.Name}", general.Name, general.BornYear, culture, province, army.Node);
                var ls = Game.Realm(lord);
                ls.Armies.Add(army);
                ls.NextArmyId = Math.Max(ls.NextArmyId, army.Id + 1);
                ls.Generals.Add(general);
                ls.Treasury = 0;
                Game.AddGrudge(realmId, lord, GrudgeProvince, DemoYear);
                ProvinceLog(province, $"{who} makes himself lord of {p.Name}, taking it from {RealmName(realmId)}.");
                text += $", seize {p.Name} and make it a lordship of their own";
                events.Add(new ChronicleEvent(ChronicleKind.Revolt, realmId, text + ". The soldiers were his, not yours."));
                events.Add(new ChronicleEvent(ChronicleKind.NewHouse, lord, $"{who} founds the {RealmName(lord)}."));
                return events;
            }
            Game.RecordLoss(realmId, province, DemoYear);
            Provinces.SetRealm(province, 0, Grid);
            MarkChanged(province);
            ProvinceLog(province, $"{who} and {army.Name} seize it from {RealmName(realmId)}.");
            TerritoryChanged = true;
            _census = null;
            SyncPopulationOwnership();
            text += $" and seize {p.Name}";
        }
        events.Add(new ChronicleEvent(ChronicleKind.Revolt, realmId, text + ". The soldiers were his, not yours."));
        return events;
    }

    /// <summary>Why the player can't settle their waiting veterans in this province, or null.</summary>
    public string? CanSettleVeterans(int provinceId)
    {
        if (PlayerState.VeteransWaiting < 1)
            return "No veterans are waiting for land.";
        if (Provinces?.Provinces.TryGetValue(provinceId, out var p) != true || p!.RealmId != PlayerRealmId)
            return "Only in your own provinces.";
        if (Game.Sieges.Any(x => x.ProvinceId == provinceId))
            return "Not while it is besieged.";
        return null;
    }

    /// <summary>
    /// The veterans get their land: on conquered land they hold it for you
    /// (integration rises, as Rome's veteran colonies did); on your own
    /// people's land, farms are taken from their owners (a grudge).
    /// </summary>
    public bool SettleVeterans(int provinceId) =>
        CanSettleVeterans(provinceId) == null && SettleVeterans(PlayerRealmId, PlayerState, provinceId);

    bool SettleVeterans(int realmId, RealmState s, int provinceId)
    {
        var ps = ProvinceStateOf(provinceId);
        double men = s.VeteransWaiting;
        bool conquered = ps.Culture != "" && ps.Culture != RealmPeople(realmId).Culture;
        if (conquered || ps.Integration < 1)
            ps.Integration = Math.Min(1, ps.Integration + Math.Min(ArmyLoyalty.VeteranIntegrationMax, men / 1000 * ArmyLoyalty.IntegrationPerThousand));
        else
            ps.Grudge = Math.Max(ps.Grudge, ArmyLoyalty.HomeLandGrudge);
        ps.Log(DemoYear, $"{men:N0} veterans are given land here.");
        ps.VeteransXp = (ps.VeteransXp * ps.Veterans + s.VeteransXp * men) / Math.Max(1, ps.Veterans + men);
        ps.Veterans += men;
        ps.VeteransYear = DemoYear;
        ArmyLoyalty.Clear(s);
        return true;
    }

    // --- Veterans as reserves (your answer: "Yes, from their colony", with consequences) -------

    /// <summary>Settled veterans lose this much of their edge each year at the plough.</summary>
    public const double ReserveXpLossPerYear = 0.02;
    /// <summary>After this many years settled they are too old to march.</summary>
    public const int ReserveYears = 25;
    /// <summary>Calling them from their farms angers the colony (its grudge rises) and leaves its fields short.</summary>
    public const double ReserveGrudge = 0.15;

    /// <summary>How many of a province's settled veterans can still march, and how hardened they still are.</summary>
    public (int Men, double Xp) Reserves(int provinceId)
    {
        var ps = ProvinceStateOf(provinceId);
        int years = Math.Max(0, DemoYear - ps.VeteransYear);
        if (ps.Veterans < 1 || years >= ReserveYears)
            return (0, 0);
        double fit = 1 - years / (double)ReserveYears;   // the old and the dead drop out
        return ((int)(ps.Veterans * fit), Math.Max(0.1, ps.VeteransXp - ReserveXpLossPerYear * years));
    }

    /// <summary>Why the player can't call this province's veterans back to arms, or null.</summary>
    public string? CanCallReserves(int provinceId)
    {
        if (Provinces?.Provinces.TryGetValue(provinceId, out var p) != true || p!.RealmId != PlayerRealmId)
            return "Only in your own provinces.";
        if (Reserves(provinceId).Men < Regiment.MinMen)
            return "No veterans here are fit to march.";
        if (Game.Sieges.Any(x => x.ProvinceId == provinceId))
            return "Not while it is besieged.";
        return null;
    }

    /// <summary>
    /// Calls a colony's veterans back to arms (the Roman evocati): they march
    /// out as hardened heavy infantry of your people, a new army at the
    /// colony. The colony resents it and its fields go short; when they are
    /// discharged again they will want land again.
    /// </summary>
    public Army? CallReserves(int provinceId)
    {
        if (CanCallReserves(provinceId) != null)
            return null;
        var s = PlayerState;
        var ps = ProvinceStateOf(provinceId);
        var (men, xp) = Reserves(provinceId);
        var unit = UnitCatalog.Instance.BestFor(UnitRoles.HeavyInfantry, CulturesOf(PlayerRealmId));
        var army = s.NewArmy($"Recalled veterans of {Provinces!.Provinces[provinceId].Name}", ProvinceNode(provinceId));
        for (int left = men; left >= Regiment.MinMen; left -= Math.Min(left, unit.Men))
            army.AddRegiment(unit.Index, Math.Min(left, unit.Men), xp, provinceId);
        army.Even();
        ps.Veterans = 0;
        ps.Grudge = Math.Max(ps.Grudge, ReserveGrudge);
        ps.Log(DemoYear, $"{men:N0} veterans leave their farms for the army again.");
        return army;
    }

    /// <summary>The computer settles its veterans on its least settled province (conquered land first).</summary>
    void SettleVeteransForComputer(int realmId, RealmState s)
    {
        var target = Provinces?.Provinces.Values.Where(p => p.RealmId == realmId && !Game.Sieges.Any(x => x.ProvinceId == p.Id))
            .OrderBy(p => ProvinceStateOf(p.Id).Integration).FirstOrDefault();
        if (target != null)
            SettleVeterans(realmId, s, target.Id);
        else
            ArmyLoyalty.Clear(s);
    }

    /// <summary>The veterans offered to take silver: pay them and they go home content.</summary>
    public bool PayOffVeterans()
    {
        var s = PlayerState;
        double cost = ArmyLoyalty.PayOffCost(s.VeteransWaiting);
        if (!s.VeteransOfferSilver || s.VeteransWaiting < 1 || s.Treasury < cost)
            return false;
        s.Treasury -= cost;
        ArmyLoyalty.Clear(s);
        return true;
    }
}
