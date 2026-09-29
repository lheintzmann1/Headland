using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's garage (FS: the vehicles page): the farm's machines with their condition, paint, fuel, hours, age, where
/// they are and what they're worth. The one picked is sold back to the dealer (a leased one given back), repaired,
/// repainted or given other options where it stands: in a workshop's bay at its prices, elsewhere a mechanic comes out
/// for more.
/// </summary>
public partial class GaragePage : MenuPage
{
    private readonly List<(Machine machine, Button pick, Action refresh)> _rows = [];
    private Label _balance = null!;
    private GridContainer _table = null!;
    private Label _title = null!;
    private Button _sell = null!;
    private Button _repair = null!;
    private Button _repaint = null!;
    private Button _options = null!;
    private Label _why = null!;
    private Label _service = null!;
    private Machine? _selected;
    /// <summary>The machine whose sale waits for a second press.</summary>
    private Machine? _confirm;
    private string _listed = "";
    private double _refresh;

    public GameRoot Game { get; init; } = null!;

    private Simulation Sim => Game.Sim;
    private Garage Garage => Sim.Garage;

    public override string Subtitle =>
        $"The farm's machines. Away from a workshop's bay, a mechanic comes out for {(Sim.Content.Economy.RemoteService - 1f) * 100f:0}% more.";

    protected override void Build()
    {
        var account = new HBoxContainer();
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        AddChild(account);

        _table = new GridContainer { Columns = 8, ThemeTypeVariation = "TableGrid" };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1060, 440), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(_table);
        AddChild(scroll);

        _title = Widgets.Label(variation: "StrongLabel");
        AddChild(_title);
        var actions = new HBoxContainer();
        _sell = Widgets.Button("", Sell);
        actions.AddChild(_sell);
        _repair = Widgets.Button("", () => Serve(Garage.Repair));
        actions.AddChild(_repair);
        _repaint = Widgets.Button("", () => Serve(Garage.Repaint));
        actions.AddChild(_repaint);
        _options = Widgets.Button("Options…", () =>
        {
            if (_selected != null) Game.Screens.Push(new WorkshopScreen { Sim = Sim, Machines = [_selected] });
        });
        actions.AddChild(_options);
        AddChild(actions);
        _why = Widgets.Label(variation: "DimLabel");
        AddChild(_why);
        _service = Widgets.Label(variation: "DimLabel");
        AddChild(_service);

        Rebuild();
        _rows.FirstOrDefault().pick?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    /// <summary>A row per machine, built again when machines come or go.</summary>
    private void Rebuild()
    {
        var machines = Garage.Machines.ToList();
        _listed = string.Join(",", machines.Select(m => m.Id));
        _rows.Clear();
        // Out of the table at once, freed later: a row's own button may be what asked for it.
        foreach (var cell in _table.GetChildren())
        {
            _table.RemoveChild(cell);
            cell.QueueFree();
        }
        foreach (var header in new[] { "Machine", "Condition", "Paint", "Fuel", "Hours", "Age", "Location", "Value" })
            _table.AddChild(Widgets.Label(header, "DimLabel"));
        var group = new ButtonGroup();
        foreach (var m in machines)
        {
            var pick = Widgets.Tab(m.Def.Name, group, m == _selected, () => Select(m));
            pick.Alignment = HorizontalAlignment.Left;
            _table.AddChild(pick);
            var (condition, paint, fuel, hours, age, location, value) =
                (Cell(), Cell(), Cell(), Cell(), Cell(), Widgets.Label(), Cell());
            foreach (var cell in new[] { condition, paint, fuel, hours, age }) _table.AddChild(cell);
            _table.AddChild(location);
            _table.AddChild(value);
            _rows.Add((m, pick, () =>
            {
                var wear = m.Get<Wearable>();
                condition.Text = wear != null ? $"{wear.Condition * 100f:0}%" : "–";
                StringName worn = wear?.Condition < Wearable.WornBelow ? "ExpenseLabel" : "";
                if (condition.ThemeTypeVariation != worn) condition.ThemeTypeVariation = worn;
                paint.Text = wear != null ? $"{wear.Paint * 100f:0}%" : "–";
                fuel.Text = m.Get<Motor>()?.FuelTank is { } tank ? $"{tank.Fraction * 100f:0}%" : "–";
                hours.Text = $"{m.OperatingHours:0.0} h";
                age.Text = m.AgeMonths == 1 ? "1 month" : $"{m.AgeMonths} months";
                location.Text = Garage.Location(m);
                value.Text = m.LeaseContract != 0 ? "contract" : m.Lease is { } lease ? $"leased, ${lease.PerHour:N0}/h" : $"$ {Garage.Value(m):N0}";
            }));
        }
        if (machines.Count == 0) _table.AddChild(Widgets.Label("No machines: buy some at the shop.", "DimLabel"));
        Select(machines.Contains(_selected!) ? _selected : machines.FirstOrDefault());
    }

    private static Label Cell()
    {
        var label = Widgets.Label();
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private void Select(Machine? m)
    {
        _selected = m;
        _confirm = null;
        if (_rows.Find(r => r.machine == m).pick is { } pick) pick.ButtonPressed = true;
        Refresh();
    }

    /// <summary>Sells (or gives back) the machine picked on a second press.</summary>
    private void Sell()
    {
        if (_selected is not { } m) return;
        if (_confirm != m)
        {
            _confirm = m;
            Refresh();
            return;
        }
        _confirm = null;
        Garage.Sell(m);
        Refresh();
    }

    private void Serve(Func<Machine, bool> serve)
    {
        if (_selected is { } m) serve(m);
        Refresh();
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        if (string.Join(",", Garage.Machines.Select(m => m.Id)) != _listed)
        {
            Rebuild();
            return;
        }
        foreach (var (_, _, refresh) in _rows) refresh();
        var shown = _selected != null;
        foreach (var control in new Control[] { _title, _sell, _repair, _repaint, _options, _why, _service }) control.Visible = shown;
        if (_selected is not { } m) return;

        _title.Text = m.Lease != null ? $"{m.Def.Name} (leased)" : m.LeaseContract != 0 ? $"{m.Def.Name} (leased for a contract)" : m.Def.Name;
        var sell = Garage.SellBlocker(m);
        var what = m.Lease != null ? "Give back" : $"Sell (${Garage.Value(m):N0})";
        _sell.Text = _confirm == m ? $"{what}: press again" : what;
        _sell.Disabled = sell != null;
        var repair = Garage.RepairBlocker(m);
        _repair.Text = Garage.RepairPrice(m) is var r and >= 0.5f ? $"Repair (${r:N0})" : "Repair";
        _repair.Disabled = repair != null;
        var repaint = Garage.RepaintBlocker(m);
        _repaint.Text = Garage.RepaintPrice(m) is var p and >= 0.5f ? $"Repaint (${p:N0})" : "Repaint";
        _repaint.Disabled = repaint != null;
        _options.Disabled = m.Def.Configurations.Count == 0;
        _why.Text = string.Join("; ", new[] { sell, repair, repaint }.OfType<string>().Distinct());
        _service.Text = Garage.Service(m) switch
        {
            ({ } w, 1f) => $"In the bay of {w.Poi.Name}: its own prices.",
            ({ } w, _) => $"{Garage.Location(m)}: a mechanic from {w.Poi.Name} comes out.",
            null => "",
        };
    }
}
