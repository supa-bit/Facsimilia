using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>The Armies panel's list of mercenary companies: hire, dismiss, outbid.</summary>
public partial class Hud
{
    bool _mercMode;

    void RefreshMercenaries()
    {
        var s = _map.PlayerState;
        var cat = UnitCatalog.Instance;
        _armiesSummary.Text =
            "Mercenary companies wait at their hiring grounds. Hire one near your land or armies, or send an envoy from afar (one month's pay more). " +
            "A hired company is an army under its own captain, paid every month, with a bonus after each victory, for the months you sign it on. " +
            "Unpaid, it deserts, goes over to your enemy or revolts, and an unpaid or slighted captain may seize the land he stands in. A realm that offers a quarter more can take it from its employer." +
            (s.LastMercPay > 0 ? $"\nLast year you paid mercenaries {_map.Money(s.LastMercPay)}." : "");
        var companies = _map.Game.Companies
            .OrderBy(c => c.Employer == _map.PlayerRealmId ? 0 : c.Employer == 0 ? 1 : 2)
            .ThenBy(c => c.Employer == 0 && _map.NearGround(_map.PlayerRealmId, c) ? 0 : 1)
            .ThenBy(c => c.Name).ToList();
        if (companies.Count == 0)
            _armyRows.AddChild(ThemeAncient.Label("No companies can be found this year.", fontSize: 15));
        foreach (var c in companies)
        {
            var units = _map.CompanyUnits(c);
            var frame = new PanelContainer { ThemeTypeVariation = "GlassPanel" };
            _armyRows.AddChild(frame);
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 3);
            frame.AddChild(box);
            string where = c.Employer == 0
                ? $"waiting at {MercenaryCatalog.Instance.Ground(c.Ground).Name}{(_map.NearGround(_map.PlayerRealmId, c) ? " (near you)" : "")}"
                : c.Employer == _map.PlayerRealmId ? "in your pay" : $"in the pay of {_map.RealmName(c.Employer)}";
            box.AddChild(ThemeAncient.Label($"{c.Name}: {where}", "HeaderLabel", 18));
            string troops = string.Join(", ", units.GroupBy(r => r.Type).Select(g => $"{ThemeAncient.GroupThousands(g.Sum(r => r.Men))} {cat[g.Key].Name}"));
            box.AddChild(ThemeAncient.Label($"{ThemeAncient.GroupThousands(Company.Men(units))} men in {units.Count} units: {troops}.", fontSize: 15));
            box.AddChild(ThemeAncient.Label(
                $"Captain {c.Captain.Name}: {c.Captain.Summary()}", fontSize: 15));
            box.AddChild(ThemeAncient.Label(
                $"Pay: {_map.Money(c.MonthlyPay(units))} a month ({c.PayFactor:0.00}× your own troops' cost" +
                (c.Elite ? $"; a strong company, it names a new price each year between {c.PayMin:0.0}× and {c.PayMax:0.0}×" : "") +
                (c.Elite ? $"); after each victory a bonus it names, {c.BonusMonths:0}-{c.BonusMonths * c.PayMax / c.PayMin:0.#} months' pay." : $"); bonus {c.BonusMonths:0} month(s) after each victory.") +
                (c.Employer == _map.PlayerRealmId && c.Arrears > 0 ? $"  Owed: {c.Arrears:0.#} month(s)." : ""), fontSize: 15));
            if (c.Employer == _map.PlayerRealmId)
            {
                var bond = ThemeAncient.Label(
                    $"Contract: {c.ContractLeft} month(s) left.  Loyalty to you: {c.Loyalty:P0} (grows with every month paid; a loyal company bears late pay longer and is hard to lure away)." +
                    (c.Slighted ? "  The captain feels slighted: a victory's bonus went unpaid." : ""), fontSize: 14);
                bond.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                bond.CustomMinimumSize = new Vector2(520, 0);
                box.AddChild(bond);
            }
            var buttons = new HBoxContainer();
            buttons.AddThemeConstantOverride("separation", 6);
            box.AddChild(buttons);
            if (c.Employer == 0)
            {
                var (problem, cost) = _map.CanHire(_map.PlayerRealmId, c);
                var length = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "How long they sign on for. When it ends they go home unless you sign them on again." };
                foreach (int m in Company.Contracts)
                    length.AddItem($"{m} months");
                length.Select(1);
                buttons.AddChild(length);
                var hire = new Button { Text = $"Hire ({_map.Money(cost)})", Disabled = problem != null, TooltipText = problem ?? "", FocusMode = FocusModeEnum.None };
                hire.Pressed += () => { _map.Hire(_map.PlayerRealmId, c, Company.Contracts[length.Selected]); _map.RefreshArmyMarkers(); RefreshArmiesPanel(); Refresh(); };
                buttons.AddChild(hire);
            }
            else if (c.Employer == _map.PlayerRealmId)
            {
                var dismiss = new Button { Text = "Dismiss", FocusMode = FocusModeEnum.None, TooltipText = "Send them back to their hiring ground." };
                dismiss.Pressed += () => { _map.Dismiss(c); _map.RefreshArmyMarkers(); RefreshArmiesPanel(); };
                buttons.AddChild(dismiss);
                foreach (int m in Company.Contracts)
                {
                    int months = m;
                    var renew = new Button { Text = $"Sign on +{m} mo", FocusMode = FocusModeEnum.None,
                        TooltipText = $"Extend their contract by {m} months; signing again costs a month's pay ({_map.Money(c.MonthlyPay(units))})." };
                    renew.Pressed += () =>
                    {
                        string? why = _map.Renew(c, months);
                        if (why != null)
                            renew.TooltipText = why;
                        RefreshArmiesPanel();
                        Refresh();
                    };
                    buttons.AddChild(renew);
                }
            }
            else
            {
                var outbid = new Button { Text = $"Outbid ({_map.Money(c.MonthlyPay(units) * Facsimilia.World.MapView.OutbidMargin)} a month)", FocusMode = FocusModeEnum.None,
                    TooltipText = "Offer a quarter more than their pay; you need two months' pay in hand." };
                outbid.Pressed += () =>
                {
                    string? why = _map.Outbid(_map.PlayerRealmId, c);
                    if (why != null)
                        outbid.TooltipText = why;
                    _map.RefreshArmyMarkers();
                    RefreshArmiesPanel();
                    Refresh();
                };
                buttons.AddChild(outbid);
            }
        }
    }
}
