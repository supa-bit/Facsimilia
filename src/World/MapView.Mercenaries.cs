using System;
using System.Collections.Generic;
using System.Linq;
using Facsimilia.Dynasties;
using Facsimilia.Game;
using Facsimilia.UI;

namespace Facsimilia.World;

/// <summary>
/// Mercenary companies on the map (decision "Next 8" and your follow-ups):
/// history's companies appear at their hiring grounds in their eras, others
/// are raised there by chance; a realm near a hiring ground (or sending an
/// envoy) hires a company, which becomes one of its armies under its own
/// captain; it is paid each month, with a bonus after victories; unpaid it
/// deserts, changes sides or revolts; and a richer realm can outbid you.
/// </summary>
public partial class MapView
{
    /// <summary>Within this many nodes (about 9 km each) of a hiring ground, your land or an army counts as near.</summary>
    public const int HireNearNodes = 40;
    /// <summary>A company hired from afar costs this many months' pay for the envoy and the journey.</summary>
    public const double EnvoyMonths = 1;
    /// <summary>An offer must beat a company's pay by this much for it to change masters.</summary>
    public const double OutbidMargin = 1.25;
    /// <summary>Generated companies wait this many years for an employer before they disband.</summary>
    public const int GeneratedYears = 6;

    int GroundNode(string groundId)
    {
        var g = MercenaryCatalog.Instance.Ground(groundId);
        var pop = Population!;
        int node = pop.NodeAtLonLat(g.Lon, g.Lat, LonMin, LonMax, LatMin, LatMax);
        if (pop.NodeRegion[node] != 0)
            return node;
        int w = pop.Width, x = node % w, y = node / w;
        for (int r = 1; r < 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < pop.Height && pop.NodeRegion[ny * w + nx] != 0)
                        return ny * w + nx;
                }
        return node;
    }

    /// <summary>A company's army, if it is hired.</summary>
    public Army? CompanyArmy(Company c) => c.Employer == 0 ? null : Game.Realm(c.Employer).ArmyById(c.ArmyId);

    /// <summary>The units a company has now (its army's, if hired).</summary>
    public int[] CompanyUnits(Company c) => CompanyArmy(c)?.Units ?? c.Units;

    /// <summary>Realms holding land near each hiring ground, worked out once a year (land changes slowly).</summary>
    readonly Dictionary<string, HashSet<int>> _groundOwners = new();
    int _groundOwnersYear = int.MinValue;

    HashSet<int> GroundOwners(string groundId)
    {
        if (_groundOwnersYear != DemoYear)
        {
            _groundOwners.Clear();
            _groundOwnersYear = DemoYear;
        }
        if (_groundOwners.TryGetValue(groundId, out var set))
            return set;
        set = new HashSet<int>();
        int g = GroundNode(groundId), w = Population!.Width, x = g % w, y = g / w;
        for (int dy = -HireNearNodes; dy <= HireNearNodes; dy += 2)
            for (int dx = -HireNearNodes; dx <= HireNearNodes; dx += 2)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < Population.Height && Population.NodeOwner[ny * w + nx] > 0)
                    set.Add(Population.NodeOwner[ny * w + nx]);
            }
        _groundOwners[groundId] = set;
        return set;
    }

    /// <summary>Is this realm near the company's hiring ground (its land or one of its armies)?</summary>
    public bool NearGround(int realmId, Company c)
    {
        if (Population == null)
            return false;
        if (GroundOwners(c.Ground).Contains(realmId))
            return true;
        int g = GroundNode(c.Ground);
        return Game.Realm(realmId).Armies.Any(a => a.Node >= 0 && NodeDistance(a.Node, g) <= HireNearNodes);
    }

    /// <summary>What hiring would cost now (first month in advance, plus the envoy from afar), or why not.</summary>
    public (string? Problem, double Cost) CanHire(int realmId, Company c)
    {
        if (c.Employer == realmId)
            return ("Already in your pay.", 0);
        double month = c.MonthlyPay(CompanyUnits(c));
        if (c.Employer != 0)
            return ("Serving another realm: outbid them to take it.", 0);
        double cost = month * (1 + (NearGround(realmId, c) ? 0 : EnvoyMonths));
        if (Game.Realm(realmId).Treasury < cost)
            return ($"Not enough silver: {Money(cost)} needed.", cost);
        return (null, cost);
    }

    /// <summary>Hires a waiting company: it becomes an army of the realm at its hiring ground, under its captain.</summary>
    public string? Hire(int realmId, Company c)
    {
        var (problem, cost) = CanHire(realmId, c);
        if (problem != null)
            return problem;
        var s = Game.Realm(realmId);
        s.Treasury -= cost;
        s.MercPaidThisYear += cost;
        Enlist(s, c, GroundNode(c.Ground), (int[])c.Units.Clone());
        Array.Clear(c.Units);
        return null;
    }

    void Enlist(RealmState s, Company c, int node, int[] units)
    {
        var army = s.NewArmy(c.Name, node);
        for (int i = 0; i < units.Length; i++)
            army.Units[i] = units[i];
        army.Experience = 0.3;   // hired men have seen war
        army.CompanyId = c.Id;
        c.Employer = s.RealmId;
        c.ArmyId = army.Id;
        c.Arrears = 0;
        c.Months = 0;
        var cap = c.Captain;
        var g = new General { Id = s.NextGeneralId++, Name = cap.Name, Origin = General.Captain, BornYear = cap.BornYear, CharacterId = -1 };
        Array.Copy(cap.BaseSkills, g.BaseSkills, Skills.Count);
        g.Perks.AddRange(cap.Perks);
        g.Battles = cap.Battles;
        g.Victories = cap.Victories;
        s.Generals.Add(g);
        Assign(s, g, army);
    }

    /// <summary>Takes a company out of a realm's service; it goes back to its hiring ground (or disbands if too few are left).</summary>
    void Release(Company c, bool toGround = true)
    {
        if (c.Employer == 0)
            return;
        var s = Game.Realm(c.Employer);
        var army = s.ArmyById(c.ArmyId);
        var g = army != null ? s.GeneralOf(army) : null;
        if (g != null)
        {
            // The captain keeps what he learned in your service.
            Array.Copy(g.BaseSkills, c.Captain.BaseSkills, Skills.Count);
            c.Captain.Perks.Clear();
            c.Captain.Perks.AddRange(g.Perks);
            c.Captain.Battles = g.Battles;
            c.Captain.Victories = g.Victories;
            s.Generals.Remove(g);
        }
        if (army != null)
        {
            c.Units = (int[])army.Units.Clone();
            s.Armies.Remove(army);
            Game.Sieges.RemoveAll(x => x.Attacker == s.RealmId && x.ArmyId == army.Id);
        }
        c.Employer = 0;
        c.ArmyId = 0;
        c.Arrears = 0;
        c.Until = Math.Max(c.Until, DemoYear + 3);
        if (!toGround || c.Units.Sum() == 0)
            Game.Companies.Remove(c);
    }

    public void Dismiss(Company c) => Release(c);

    /// <summary>Offers a company in another realm's pay more to change masters.</summary>
    public string? Outbid(int realmId, Company c)
    {
        if (c.Employer == 0 || c.Employer == realmId)
            return "It isn't serving another realm.";
        var old = Game.Realm(c.Employer);
        var army = old.ArmyById(c.ArmyId);
        if (army == null)
            return "The company can't be found.";
        double month = c.MonthlyPay(army.Units) * OutbidMargin;
        var s = Game.Realm(realmId);
        if (s.Treasury < month * 2)
            return $"Offering more than their pay needs two months in hand: {Money(month * 2)}.";
        int node = army.Node;
        var units = (int[])army.Units.Clone();
        var xp = (double[])army.UnitXp.Clone();
        int oldEmployer = c.Employer;
        Release(c, toGround: true);
        if (!Game.Companies.Contains(c))
            Game.Companies.Add(c);
        c.PayFactor *= OutbidMargin;
        s.Treasury -= month;
        s.MercPaidThisYear += month;
        Enlist(s, c, node, units);
        Array.Clear(c.Units);
        Array.Copy(xp, s.ArmyById(c.ArmyId)!.UnitXp, xp.Length);
        _pendingMercNews.Add(new ChronicleEvent(ChronicleKind.War, oldEmployer, $"{c.Name} leave {RealmName(oldEmployer)}'s service for {RealmName(realmId)}'s better pay."));
        _pendingMercNews.Add(new ChronicleEvent(ChronicleKind.War, realmId, $"{c.Name} leave {RealmName(oldEmployer)}'s service for {RealmName(realmId)}'s better pay."));
        return null;
    }

    readonly List<ChronicleEvent> _pendingMercNews = new();

    // --- The year and the month ------------------------------------------------------

    /// <summary>
    /// The year for companies: history's companies appear in their eras,
    /// companies are raised by chance at empty hiring grounds, old ones
    /// disband, elite companies name a new price, and other realms at war hire.
    /// </summary>
    internal List<ChronicleEvent> MercenariesYear(Random rng)
    {
        var events = new List<ChronicleEvent>();
        if (Population == null)
            return events;
        var cat = UnitCatalog.Instance;
        foreach (var spec in MercenaryCatalog.Instance.Companies)
        {
            if (DemoYear < spec.From || DemoYear > spec.To || Game.CompaniesSeen.Contains(spec.Id))
                continue;
            Game.CompaniesSeen.Add(spec.Id);
            var c = new Company
            {
                Id = Game.NextCompanyId++, SpecId = spec.Id, Name = spec.Name, Ground = spec.Ground, Units = new int[cat.Count],
                PayMin = spec.PayMin, PayMax = spec.PayMax, BonusMonths = spec.Bonus, Until = spec.To,
            };
            foreach (var (u, n) in spec.Units)
                if (cat.Has(u))
                    c.Units[cat[u].Index] = n;
            c.PayFactor = spec.PayMin + rng.NextDouble() * (spec.PayMax - spec.PayMin);
            c.Captain = spec.CaptainName != null
                ? MakeCaptain(spec.CaptainName, spec.CaptainSkills!, spec.CaptainPerks!)
                : General.Make(0, CaptainName(c.Ground, rng), General.Captain, DemoYear - rng.Next(28, 45), -1, rng, DemoYear);
            Game.Companies.Add(c);
        }
        // Chance companies at hiring grounds with none waiting.
        foreach (var ground in MercenaryCatalog.Instance.Grounds)
        {
            if (Game.Companies.Any(c => c.Employer == 0 && c.Ground == ground.Id) || rng.NextDouble() > 0.3)
                continue;
            var c = GenerateCompany(ground, rng);
            if (c != null)
                Game.Companies.Add(c);
        }
        foreach (var c in Game.Companies.Where(c => c.Employer == 0 && (DemoYear > c.Until || c.Units.Sum() == 0)).ToList())
            Game.Companies.Remove(c);
        foreach (var c in Game.Companies.Where(c => c.Elite))
            c.PayFactor = c.PayMin + rng.NextDouble() * (c.PayMax - c.PayMin);   // a strong company names its price anew
        // Other realms at war hire a company near them, if they can afford a year of it.
        foreach (var s in Game.Realms.Values.Where(s => s.RealmId != PlayerRealmId && Game.Wars.Of(s.RealmId).Any()).ToList())
        {
            var c = Game.Companies.Where(c => c.Employer == 0 && NearGround(s.RealmId, c))
                .OrderByDescending(c => c.Men(c.Units)).FirstOrDefault();
            if (c == null || s.Treasury < c.MonthlyPay(c.Units) * 12)
                continue;
            if (Hire(s.RealmId, c) == null)
            {
                var army = s.ArmyById(c.ArmyId)!;
                SendArmy(s.RealmId, army, CapitalNode(s.RealmId));
                events.Add(new ChronicleEvent(ChronicleKind.War, s.RealmId, $"{RealmName(s.RealmId)} hires {c.Name}."));
            }
        }
        return events;
    }

    General MakeCaptain(string name, int[] skills, string[] perks)
    {
        var g = new General { Id = 0, Name = name, Origin = General.Captain, BornYear = DemoYear - 40, CharacterId = -1 };
        for (int i = 0; i < Math.Min(skills.Length, Skills.Count); i++)
            g.BaseSkills[i] = skills[i];
        g.Perks.AddRange(perks);
        return g;
    }

    string CaptainName(string groundId, Random rng)
    {
        int node = GroundNode(groundId);
        int owner = Population!.NodeOwner[node];
        var culture = owner > 0 ? CultureOf(owner) : Culture.Greek;
        var grng = new Godot.RandomNumberGenerator { Seed = (ulong)rng.NextInt64() };
        return Names.Pick(culture, true, grng);
    }

    /// <summary>A company raised by chance at a hiring ground, of the peoples there.</summary>
    Company? GenerateCompany(HiringGround ground, Random rng)
    {
        var cat = UnitCatalog.Instance;
        int node = GroundNode(ground.Id);
        int prov = ProvinceOfNode(node);
        string culture = prov != 0 && Game.Provinces.TryGetValue(prov, out var ps) ? ps.Culture : "";
        if (culture == "" && Population!.NodeOwner[node] > 0)
            culture = RealmPeople(Population.NodeOwner[node]).Culture;
        var cultures = new HashSet<string> { culture };
        var units = new int[cat.Count];
        int[] roles = { UnitRoles.HeavyInfantry, UnitRoles.LightInfantry, UnitRoles.Missile, UnitRoles.Cavalry };
        int total = 2 + rng.Next(3);
        for (int k = 0; k < total; k++)
        {
            var u = cat.BestFor(roles[rng.Next(roles.Length)], cultures);
            if (u.Domain == Domain.Land)
                units[u.Index]++;
        }
        if (units.Sum() == 0)
            return null;
        string captain = CaptainName(ground.Id, rng);
        string people = culture != "" ? CultureName(culture) : ground.Name;
        return new Company
        {
            Id = Game.NextCompanyId++, Name = $"The company of {captain} ({people})", Ground = ground.Id, Units = units,
            Captain = General.Make(0, captain, General.Captain, DemoYear - rng.Next(26, 45), -1, rng, DemoYear),
            PayMin = 1.3, PayMax = 1.5, PayFactor = 1.3 + 0.2 * rng.NextDouble(), BonusMonths = 1, Until = DemoYear + GeneratedYears,
        };
    }

    /// <summary>
    /// The month for companies in service: each is paid (arrears first); an
    /// unpaid company grows angry: after two months it may desert, after four
    /// it may change sides (if its employer is at war) or revolt and plunder.
    /// </summary>
    List<ChronicleEvent> MercenariesMonth(Random rng)
    {
        var events = new List<ChronicleEvent>(_pendingMercNews);
        _pendingMercNews.Clear();
        foreach (var c in Game.Companies.Where(c => c.Employer != 0).ToList())
        {
            var s = Game.Realm(c.Employer);
            var army = s.ArmyById(c.ArmyId);
            if (army == null || army.IsEmpty)
            {
                Game.Companies.Remove(c);   // destroyed in battle or sent away
                continue;
            }
            c.Months++;
            c.Arrears += army.VictoriesUnpaid * c.BonusMonths;   // a victory earns the company its bonus
            army.VictoriesUnpaid = 0;
            double month = c.MonthlyPay(army.Units);
            double owed = month * (1 + c.Arrears);
            if (s.Treasury >= owed)
            {
                s.Treasury -= owed;
                s.MercPaidThisYear += owed;
                c.Arrears = 0;
            }
            else if (s.Treasury >= month)
            {
                s.Treasury -= month;
                s.MercPaidThisYear += month;
            }
            else
                c.Arrears += 1;
            if (c.Arrears < 2)
                continue;
            string who = RealmName(s.RealmId);
            if (c.Arrears >= 4 && rng.NextDouble() < 0.35)
            {
                var enemy = Game.Wars.Of(s.RealmId).Select(w => w.Attacker == s.RealmId ? w.Defender : w.Attacker)
                    .OrderByDescending(e => Game.Realm(e).Treasury).FirstOrDefault();
                int node = army.Node;
                var units = (int[])army.Units.Clone();
                if (enemy > 0 && Game.Realm(enemy).Treasury > month * 3)
                {
                    Release(c);
                    if (!Game.Companies.Contains(c))
                        Game.Companies.Add(c);
                    Enlist(Game.Realm(enemy), c, node, units);
                    Array.Clear(c.Units);
                    string text = $"{c.Name}, unpaid for months, go over to {RealmName(enemy)}.";
                    events.Add(new ChronicleEvent(ChronicleKind.War, s.RealmId, text));
                    events.Add(new ChronicleEvent(ChronicleKind.War, enemy, text));
                }
                else
                {
                    double loot = Math.Min(s.Treasury, month * 6);
                    s.Treasury -= loot;
                    Release(c);
                    events.Add(new ChronicleEvent(ChronicleKind.Revolt, s.RealmId,
                        $"{c.Name}, unpaid for months, revolt against {who}, plunder {Money(loot)} and march away."));
                }
            }
            else if (rng.NextDouble() < 0.15)
            {
                Release(c);
                events.Add(new ChronicleEvent(ChronicleKind.Economy, s.RealmId, $"{c.Name}, unpaid, desert {who}'s service."));
            }
        }
        return events;
    }
}
