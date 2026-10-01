using Headland.Core;
using Headland.Core.Pois;
using Headland.Core.Time;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's prices (FS: the prices page): each fill type a buyer takes, the best price for it now and where, what the
/// farm has of it, and how the market goes next month; for the one picked, what each buyer pays and how it takes it,
/// and its price month by month through the year (FS: prices follow the season).
/// </summary>
public partial class PricesPage : MenuPage
{
    /// <summary>Prices are shown for this many units (FS: per 1,000 liters).</summary>
    private const float Per = 1000f;

    private static string? _picked;
    private GridContainer _table = null!;
    private Label _title = null!;
    private GridContainer _buyers = null!;
    private GridContainer _year = null!;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;

    public override string Subtitle => $"What buyers pay now, per {Per:N0} units, and how prices go through the year. It's {Calendar.MonthNames[Sim.Clock.Month - 1]}.";

    protected override void Build()
    {
        _table = new GridContainer { Columns = 5, ThemeTypeVariation = "TableGrid" };
        AddChild(_table);
        AddChild(new HSeparator());
        _title = Widgets.Label(variation: "StrongLabel");
        AddChild(_title);
        _buyers = new GridContainer { Columns = 4, ThemeTypeVariation = "TableGrid" };
        AddChild(_buyers);
        AddChild(Widgets.Label("The market through the year", "StrongLabel"));
        _year = new GridContainer { Columns = 12, ThemeTypeVariation = "TableGrid" };
        AddChild(_year);
        var sellable = Sim.Pois.Sellable.ToList();
        if (_picked == null || sellable.All(f => f.Id != _picked)) _picked = sellable.FirstOrDefault()?.Id;
        Refresh();
        _table.GetChildren().OfType<Button>().FirstOrDefault()?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 1;
        Refresh();
    }

    private void Refresh()
    {
        var month = Sim.Clock.Month;
        var next = month % 12 + 1;
        var focused = GetViewport()?.GuiGetFocusOwner() is Button { Text: var focus } ? focus : null;
        foreach (var cell in _table.GetChildren()) cell.Free();
        foreach (var header in new[] { "Goods", "Best price now", "At", "In stock", $"In {Calendar.MonthNames[next - 1]}" })
            _table.AddChild(Widgets.Label(header, "DimLabel"));
        var group = new ButtonGroup();
        foreach (var ft in Sim.Pois.Sellable)
        {
            var quotes = Sim.Pois.Quotes(ft.Id);
            var best = quotes.FirstOrDefault(q => q.Closed == null) ?? quotes[0];
            var pick = Widgets.Tab(ft.Name, group, ft.Id == _picked, () =>
            {
                _picked = ft.Id;
                Refresh();
            });
            _table.AddChild(pick);
            if (ft.Name == focused) pick.CallDeferred(Control.MethodName.GrabFocus);
            _table.AddChild(Widgets.Label($"$ {best.Price * Per:N0} / {Per:N0} {ft.Unit}", best.High != null ? "IncomeLabel" : ""));
            _table.AddChild(Widgets.Label(best.High != null ? $"{best.Poi.Name}, high demand +{(best.High.Factor - 1f) * 100f:0}%" : best.Poi.Name));
            var stock = Sim.Stock(Sim.Player.FarmId, ft.Id);
            _table.AddChild(Widgets.Label(stock >= 1f ? $"{stock:N0} {ft.Unit}" : "–", stock >= 1f ? "" : "DimLabel"));
            _table.AddChild(Trend(Sim.Economy.Price(ft.Id, next) / Sim.Economy.Price(ft.Id, month) - 1f));
        }
        ShowPicked(month);
    }

    /// <summary>The market's move next month: up, down or flat, by how much.</summary>
    private static RichTextLabel Trend(float change)
    {
        var label = Widgets.Rich(0);
        label.Text = MathF.Abs(change) < 0.005f
            ? $"{Widgets.Icon("trending_flat", Palette.Dim)} {Widgets.Colored("same", Palette.Dim)}"
            : change > 0f
                ? $"{Widgets.Icon("trending_up", Palette.Good)} {Widgets.Colored($"+{change * 100f:0}%", Palette.Good)}"
                : $"{Widgets.Icon("trending_down", Palette.Warning)} {Widgets.Colored($"{change * 100f:0}%", Palette.Warning)}";
        return label;
    }

    /// <summary>The picked fill type: each buyer's price and terms, and the market price month by month.</summary>
    private void ShowPicked(int month)
    {
        foreach (var cell in _buyers.GetChildren()) cell.Free();
        foreach (var cell in _year.GetChildren()) cell.Free();
        if (_picked == null || !Sim.Content.FillTypes.TryGetValue(_picked, out var ft)) return;
        _title.Text = $"{ft.Name} buyers";
        foreach (var header in new[] { "Buyer", "Price", "Takes", "Now" }) _buyers.AddChild(Widgets.Label(header, "DimLabel"));
        foreach (var q in Sim.Pois.Quotes(ft.Id))
        {
            _buyers.AddChild(Widgets.Label(q.Poi.Name));
            var price = $"$ {q.Price * Per:N0}";
            _buyers.AddChild(Widgets.Label(q.High != null ? $"{price}, high demand +{(q.High.Factor - 1f) * 100f:0}%" : price, q.High != null ? "IncomeLabel" : ""));
            _buyers.AddChild(Widgets.Label(Takes(q)));
            _buyers.AddChild(Widgets.Label(q.Closed ?? "Buying", q.Closed != null ? "DimLabel" : ""));
        }
        var prices = Enumerable.Range(1, 12).Select(m => Sim.Economy.Price(ft.Id, m)).ToList();
        var top = prices.Max();
        for (var m = 1; m <= 12; m++)
        {
            var header = Widgets.Label(Calendar.MonthNames[m - 1][..3], m == month ? "StrongLabel" : "DimLabel");
            header.HorizontalAlignment = HorizontalAlignment.Right;
            _year.AddChild(header);
        }
        for (var m = 1; m <= 12; m++)
        {
            var cell = Widgets.Label($"{prices[m - 1] * Per:N0}", prices[m - 1] >= top - 0.0001f ? "IncomeLabel" : m == month ? "StrongLabel" : "");
            cell.HorizontalAlignment = HorizontalAlignment.Right;
            cell.CustomMinimumSize = new Vector2(52, 0);
            _year.AddChild(cell);
        }
    }

    /// <summary>How a buyer takes it: tipped or piped, and in bales or on pallets.</summary>
    private static string Takes(Quote q) => (q.Loads, q.Objects) switch
    {
        (true, true) => "loads, bales and pallets",
        (true, false) => "loads tipped or piped",
        _ => "bales and pallets left in its area",
    };
}
