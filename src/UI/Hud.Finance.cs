using System;
using System.Linq;
using Facsimilia.Game;
using Godot;

namespace Facsimilia.UI;

/// <summary>
/// The Finances panel (the Ledger topic "Treasury and money"): your spending
/// lines, who collects the taxes, the silver in your coin, your loans, and
/// the indemnities you pay or receive. Opened from the treasury panel.
/// </summary>
public partial class Hud
{
    PanelContainer _financePanel = null!;
    VBoxContainer _financeBox = null!;

    void BuildFinancePanel()
    {
        _financePanel = SidePanel("Finances", () => ToggleFinancePanel(false), 640);
        var box = _financePanel.GetNode<VBoxContainer>("Box");
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620, 760), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _financeBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _financeBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_financeBox);
    }

    void ToggleFinancePanel(bool? show = null)
    {
        _financePanel.Visible = show ?? !_financePanel.Visible;
        if (_financePanel.Visible)
        {
            ToggleRealmPanel(false);
            RefreshFinancePanel();
        }
    }

    Label Wrapped(string text, int size = 15, string variation = "")
    {
        var l = variation == "" ? ThemeAncient.Label(text, fontSize: size) : ThemeAncient.Label(text, variation, size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(590, 0);
        return l;
    }

    void RefreshFinancePanel()
    {
        if (!_financePanel.Visible)
            return;
        foreach (Node child in _financeBox.GetChildren())
            child.QueueFree();
        var s = _map.PlayerState;
        var c = _map.CensusOf(_map.PlayerRealmId);
        var (tax, _, _, civil, _, _, _) = Economy.Accounts(s, c, _map.Corruption(_map.PlayerRealmId));

        // Spending lines.
        _financeBox.AddChild(ThemeAncient.Label("Spending", "HeaderLabel", 18));
        _financeBox.AddChild(Wrapped($"What you spend beyond the army and administration: {_map.Money(civil)} a year. 100% is what a realm of your size usually spends.", 14, "SubtleLabel"));
        foreach (var line in Enum.GetValues<SpendingLine>())
        {
            int i = (int)line;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _financeBox.AddChild(row);
            var name = ThemeAncient.Label(Finance.LineNames[i], fontSize: 15);
            name.CustomMinimumSize = new Vector2(190, 0);
            name.TooltipText = Finance.LineHelp[i];
            name.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(name);
            var slider = new HSlider { MinValue = 0, MaxValue = Finance.MaxSpending, Step = 0.25, Value = s.Spending[i],
                CustomMinimumSize = new Vector2(200, 24), TooltipText = Finance.LineHelp[i] };
            row.AddChild(slider);
            var value = ThemeAncient.Label($"{s.Spending[i]:P0}  ·  {_map.Money(Finance.LineCost(s, line, tax, c.People))}", fontSize: 15);
            row.AddChild(value);
            slider.DragEnded += _ =>
            {
                s.Spending[i] = slider.Value;
                RefreshFinancePanel();
                RefreshRealmPanel();
            };
        }

        // Tax collectors.
        _financeBox.AddChild(ThemeAncient.Label("Tax collectors", "HeaderLabel", 18));
        string? lockedWhy = Finance.CanSwitch(s);
        var coll = new HBoxContainer();
        coll.AddThemeConstantOverride("separation", 8);
        _financeBox.AddChild(coll);
        foreach (var (kind, label, tip) in new[]
                 {
                     (Collectors.Officials, "Salaried officials", "Your own officials collect: they need paying, and corruption takes its share."),
                     (Collectors.Farmers, "Tax farmers", $"Contractors like Rome's publicani pay {Finance.FarmersUpFront:P0} of a year's tax up front for a {Finance.ContractYears}-year contract, " +
                         $"then keep {Finance.FarmersCut:P0} of what they collect. Administration costs less and corruption doesn't touch your taxes, but they squeeze the people: more unrest, slower growth."),
                 })
        {
            var b = new Button { Text = label, ToggleMode = true, ButtonPressed = s.Collectors == kind, FocusMode = FocusModeEnum.None,
                Disabled = s.Collectors != kind && lockedWhy != null, TooltipText = tip + (lockedWhy != null ? "\n" + lockedWhy : "") };
            b.Pressed += () =>
            {
                double paid = Finance.Switch(s, kind);
                if (paid > 0)
                    LogEvents(new System.Collections.Generic.List<Dynasties.ChronicleEvent> { new(Dynasties.ChronicleKind.Economy, _map.PlayerRealmId, $"The tax farmers pay {_map.Money(paid)} for a {Finance.ContractYears}-year contract.") });
                RefreshFinancePanel();
                RefreshRealmPanel();
            };
            coll.AddChild(b);
        }

        // The coin.
        _financeBox.AddChild(ThemeAncient.Label("The coin", "HeaderLabel", 18));
        _financeBox.AddChild(Wrapped(
            $"Silver in your coin: {s.CoinPurity:P0}. Prices: ×{s.PriceLevel:0.00} of 300 BC. " +
            (s.LastMint > 0.5 ? $"Last year the mint made {_map.Money(s.LastMint)} by striking more coins from the same silver. " : "") +
            "Less silver gives you more coins now; prices catch up over the years, so the profit fades. Merchants value your coin by its real silver, so customs fall. " +
            "Restoring a good coin means striking it anew, which costs silver.", 14));
        var coinRow = new HBoxContainer();
        coinRow.AddThemeConstantOverride("separation", 8);
        _financeBox.AddChild(coinRow);
        coinRow.AddChild(ThemeAncient.Label("Silver content:", fontSize: 15));
        var purity = new HSlider { MinValue = Finance.MinPurity, MaxValue = 1, Step = 0.05, Value = s.CoinPurity, CustomMinimumSize = new Vector2(260, 24) };
        coinRow.AddChild(purity);
        var coinNote = ThemeAncient.Label("", fontSize: 14);
        coinRow.AddChild(coinNote);
        purity.ValueChanged += v =>
        {
            double cost = Finance.RestoreCost(s, v);
            coinNote.Text = cost > 0 ? $"restoring costs {_map.Money(cost)}" : "";
        };
        purity.DragEnded += _ =>
        {
            string? why = Finance.SetPurity(s, purity.Value);
            if (why != null)
                LogEvents(new System.Collections.Generic.List<Dynasties.ChronicleEvent> { new(Dynasties.ChronicleKind.Economy, _map.PlayerRealmId, why) });
            RefreshFinancePanel();
            RefreshRealmPanel();
        };

        // Loans.
        _financeBox.AddChild(ThemeAncient.Label("Loans", "HeaderLabel", 18));
        if (s.Loans.Count == 0)
            _financeBox.AddChild(Wrapped("You owe nothing.", 15));
        foreach (var loan in s.Loans)
        {
            string who = loan.Lender switch
            {
                Loan.TemplesLender => "The temples",
                Loan.BankersLender => "The bankers",
                _ => _map.RealmName(loan.LenderRealm),
            };
            _financeBox.AddChild(Wrapped($"{who}: {_map.Money(loan.Amount)} at {loan.Rate:P0} a year.", 15));
        }
        _financeBox.AddChild(Wrapped(
            $"When the treasury runs short the temples lend first ({Finance.TempleRate:P0}, up to a year's income), then the bankers ({Finance.BankerRate:P0}, up to two years'). " +
            "Two years unable to pay or borrow and you default: the temples curse the realm (more unrest, slower growth for ten years), the bankers refuse you for thirty years, " +
            "and realms you owe gain a pretext for war." +
            (_map.DemoYear < s.BankersRefuseUntil ? $"\nThe bankers refuse you until {ThemeAncient.YearText(s.BankersRefuseUntil)}." : "") +
            (s.TempleCurseYears > 0 ? $"\nThe gods' curse lies on the realm for {s.TempleCurseYears} more year(s)." : ""), 14, "SubtleLabel"));
        var owedToYou = _map.Game.Realms.Values.SelectMany(r => r.Loans.Where(l => l.LenderRealm == _map.PlayerRealmId).Select(l => (r.RealmId, l))).ToList();
        foreach (var (realm, loan) in owedToYou)
            _financeBox.AddChild(Wrapped($"{_map.RealmName(realm)} owes you {_map.Money(loan.Amount)} at {loan.Rate:P0}.", 15));

        // Indemnities.
        var mine = _map.Game.Indemnities.Where(x => x.Payer == _map.PlayerRealmId || x.Payee == _map.PlayerRealmId).ToList();
        if (mine.Count > 0)
            _financeBox.AddChild(ThemeAncient.Label("Indemnities", "HeaderLabel", 18));
        foreach (var ind in mine)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _financeBox.AddChild(row);
            bool paying = ind.Payer == _map.PlayerRealmId;
            var l = ThemeAncient.Label(paying
                ? $"You pay {_map.RealmName(ind.Payee)} {_map.Money(ind.PerYear)} a year, {ind.YearsLeft} more year(s)."
                : $"{_map.RealmName(ind.Payer)} pays you {_map.Money(ind.PerYear)} a year, {ind.YearsLeft} more year(s).", fontSize: 15);
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(l);
            if (paying)
            {
                var stop = new Button { Text = "Stop paying", FocusMode = FocusModeEnum.None, TooltipText = $"{_map.RealmName(ind.Payee)} will have a pretext for war." };
                stop.Pressed += () =>
                {
                    LogEvents(_map.StopPayingIndemnity(ind));
                    RefreshFinancePanel();
                };
                row.AddChild(stop);
            }
        }
        _financeBox.AddChild(Wrapped(Finance.Basis(_map.DemoYear) == "a basket of goods" ? "Money is compared by what it buys of a basket of goods."
            : $"Money is compared by its weight in {Finance.Basis(_map.DemoYear)}.", 13, "SubtleLabel"));
    }
}
