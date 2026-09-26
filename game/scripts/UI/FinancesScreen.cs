using Headland.Core;
using Headland.Core.Economics;
using Headland.Core.Time;
using Godot;

namespace Headland.Game.UI;

/// <summary>F2: the balance, and the farm's books: money in and out by category over the last days or months.</summary>
public partial class FinancesScreen : Screen
{
    private Label _balance = null!;
    private GridContainer _table = null!;
    private bool _byMonth;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    protected override void Build()
    {
        var content = new VBoxContainer { ThemeTypeVariation = "DialogBox" };

        var balance = new HBoxContainer();
        balance.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label(variation: "MoneyLabel");
        balance.AddChild(_balance);
        content.AddChild(balance);

        var tabs = new HBoxContainer();
        var group = new ButtonGroup();
        tabs.AddChild(Widgets.Tab("Days", group, !_byMonth, () => Show(byMonth: false)));
        tabs.AddChild(Widgets.Tab("Months", group, _byMonth, () => Show(byMonth: true)));
        content.AddChild(tabs);

        _table = new GridContainer { ThemeTypeVariation = "TableGrid" };
        content.AddChild(_table);

        AddChild(Widgets.Dialog("Finances", content, "Money in and out by category. Esc closes."));
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

    private void Refresh()
    {
        _balance.Text = $"$ {Sim.Economy.Money:N0}";

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
