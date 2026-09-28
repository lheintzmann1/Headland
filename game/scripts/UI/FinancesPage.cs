using Headland.Core;
using Headland.Core.Economics;
using Headland.Core.Time;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's finances: the balance and the bank loan, with buttons to borrow and repay, and the farm's books: money in
/// and out by category over the last days or months.
/// </summary>
public partial class FinancesPage : MenuPage
{
    private Label _balance = null!;
    private Label _loan = null!;
    private Label _terms = null!;
    private Button _borrow = null!;
    private Button _repay = null!;
    private Button _repayAll = null!;
    private GridContainer _table = null!;
    private bool _byMonth;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle
    {
        get
        {
            var level = Sim.Economy.PriceLevel;
            var prices = Math.Abs(level - 1f) < 0.001f ? "" : $", prices {(level > 1f ? "+" : "")}{(level - 1f) * 100f:0}%";
            return $"{Sim.Difficulty.Name} difficulty{prices}. Money in and out by category.";
        }
    }

    protected override void Build()
    {
        var account = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        account.AddChild(Widgets.Label("Loan"));
        _loan = Widgets.Label(variation: "MoneyLabel");
        account.AddChild(_loan);
        AddChild(account);
        _terms = Widgets.Label(variation: "DimLabel");
        AddChild(_terms);

        var bank = new HBoxContainer();
        _borrow = Widgets.Button("", () => Bank(() => Sim.Economy.Borrow()));
        _repay = Widgets.Button("", () => Bank(() => Sim.Economy.Repay()));
        _repayAll = Widgets.Button("Repay all", () => Bank(() => Sim.Economy.Repay(all: true)));
        bank.AddChild(_borrow);
        bank.AddChild(_repay);
        bank.AddChild(_repayAll);
        AddChild(bank);

        var tabs = new HBoxContainer();
        var group = new ButtonGroup();
        tabs.AddChild(Widgets.Tab("Days", group, !_byMonth, () => Show(byMonth: false)));
        tabs.AddChild(Widgets.Tab("Months", group, _byMonth, () => Show(byMonth: true)));
        AddChild(tabs);

        _table = new GridContainer { ThemeTypeVariation = "TableGrid" };
        AddChild(_table);

        tabs.GetChild<Button>(0).CallDeferred(Control.MethodName.GrabFocus);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    private void Show(bool byMonth)
    {
        _byMonth = byMonth;
        Refresh();
    }

    /// <summary>Borrows or repays, and shows the new balance right away.</summary>
    private void Bank(Action deal)
    {
        deal();
        Refresh();
    }

    private void Refresh()
    {
        var eco = Sim.Economy;
        Widgets.Balance(_balance, eco.Money);
        _loan.Text = $"$ {eco.Loan:N0}";
        var terms = $"Credit limit ${eco.Terms.CreditLimit:N0} at {eco.Terms.LoanInterest * 100f:0.##}% a year";
        _terms.Text = eco.Loan > 0f ? $"{terms}: ${eco.DailyInterest:N0} of interest a day" : terms;
        _borrow.Text = $"Borrow ${(eco.NextLoan >= 1f ? eco.NextLoan : eco.Terms.LoanStep):N0}";
        _borrow.Disabled = eco.NextLoan < 1f;
        _repay.Text = $"Repay ${(eco.NextRepayment >= 1f ? eco.NextRepayment : eco.Terms.LoanStep):N0}";
        _repay.Disabled = eco.NextRepayment < 1f || eco.NextRepayment > eco.Money;
        _repayAll.Disabled = eco.Loan < 1f || eco.Loan > eco.Money;

        // Oldest on the left; rows for the categories with money in these columns.
        var books = Sim.Economy.Ledger;
        var pages = (_byMonth ? books.Months : books.Days).Reverse().ToList();
        var years = pages.Select(p => Ledger.MonthOf(p).year).Distinct().Count() > 1;
        foreach (var cell in _table.GetChildren()) cell.Free();
        _table.Columns = pages.Count + 1;
        _table.AddChild(Widgets.Label());
        foreach (var page in pages)
        {
            var header = Widgets.Label(_byMonth ? MonthName(page, years) : DayName(books.DateOf(page)), "DimLabel");
            header.HorizontalAlignment = HorizontalAlignment.Right;
            _table.AddChild(header);
        }
        foreach (var category in Ledger.Categories.Where(c => pages.Any(p => MathF.Abs(p[c]) >= 0.5f)))
        {
            _table.AddChild(Widgets.Label(Ledger.Name(category)));
            foreach (var page in pages) _table.AddChild(Widgets.Money(page[category]));
        }
        _table.AddChild(Widgets.Label("Total", "StrongLabel"));
        foreach (var page in pages) _table.AddChild(Widgets.Money(page.Net));
    }

    private static string DayName(GameDate date) => $"{Calendar.MonthNames[date.Month - 1][..3]} {date.Day}";

    private static string MonthName(FinancePeriod page, bool withYear)
    {
        var (year, month) = Ledger.MonthOf(page);
        var name = Calendar.MonthNames[month - 1][..3];
        return withYear ? $"{name} Y{year}" : name;
    }
}
