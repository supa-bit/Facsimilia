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
                    army.GeneralLoyalty = Math.Min(1, army.GeneralLoyalty + ArmyLoyalty.PerYear);
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
                if (rng.NextDouble() < ArmyLoyalty.MutinyChance(army.GeneralLoyalty, pressure))
                    events.AddRange(Mutiny(id, s, army));
        }
        return events;
    }

    /// <summary>
    /// An army follows its general against the realm (Sulla's march on Rome):
    /// it is lost to you, its general with it, and the province it stands in
    /// breaks away under them, unless it is the capital's.
    /// </summary>
    List<ChronicleEvent> Mutiny(int realmId, RealmState s, Army army)
    {
        var events = new List<ChronicleEvent>();
        var general = s.GeneralOf(army);
        string who = general?.Name ?? "Its general";
        s.Armies.Remove(army);
        if (general != null)
            s.Generals.Remove(general);
        s.RivalStrength = Math.Clamp(s.RivalStrength + 10, 0, 100);
        int province = army.Node >= 0 ? ProvinceOfNode(army.Node) : 0;
        string text = $"{who} and {army.Name} turn against {RealmName(realmId)}";
        if (province != 0 && Provinces!.Provinces.TryGetValue(province, out var p) && p.RealmId == realmId && province != CapitalProvince(realmId))
        {
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
        ArmyLoyalty.Clear(s);
        return true;
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
