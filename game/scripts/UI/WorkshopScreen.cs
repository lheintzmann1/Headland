using Headland.Core;
using Headland.Core.Machines;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// A workshop's services for some of the farm's machines: repair and repaint them, and change their options (wheels,
/// engine, hitches, color…) for what the new ones cost more and the work. The use key opens it in a workshop's bay for
/// the chain parked there (driving, or walking in beside the farm's machines); the garage opens it for one machine
/// anywhere, a mechanic coming out for more (<see cref="Garage.Service"/>).
/// </summary>
public partial class WorkshopScreen : Screen
{
    private readonly List<Row> _rows = [];
    private Label _balance = null!;
    private Button _repair = null!;
    private Button _repaint = null!;
    private Label _why = null!;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;
    public IReadOnlyList<Machine> Machines { get; init; } = [];

    /// <summary>A machine, the options picked for it, and its fit button.</summary>
    private sealed record Row(Machine Machine, Dictionary<string, string> Picked, Button Fit, Label Why);

    protected override void Build()
    {
        var content = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        var account = new HBoxContainer();
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        content.AddChild(account);

        var services = new HBoxContainer();
        _repair = Widgets.Button("", () => Serve(Sim.Garage.RepairBlocker, Sim.Garage.Repair));
        services.AddChild(_repair);
        _repaint = Widgets.Button("", () => Serve(Sim.Garage.RepaintBlocker, Sim.Garage.Repaint));
        services.AddChild(_repaint);
        _why = Widgets.Label(variation: "DimLabel");
        services.AddChild(_why);
        content.AddChild(services);

        foreach (var m in Machines.Where(m => m.Def.Configurations.Count > 0))
        {
            content.AddChild(Widgets.Label(m.Def.Name, "StrongLabel"));
            var picked = new Dictionary<string, string>(m.Def.Choices);
            var table = new GridContainer { Columns = 2, ThemeTypeVariation = "TableGrid" };
            foreach (var c in m.Def.Configurations)
            {
                table.AddChild(Widgets.Label(c.Name));
                table.AddChild(Widgets.Options(c, m.Def, Sim.Economy.PriceLevel, option =>
                {
                    picked[c.Id] = option;
                    Refresh();
                }));
            }
            content.AddChild(table);
            var fit = new HBoxContainer();
            Row row = null!;
            var button = Widgets.Button("", () =>
            {
                Sim.Garage.Configure(row.Machine, row.Picked);
                Refresh();
            });
            fit.AddChild(button);
            var why = Widgets.Label(variation: "DimLabel");
            fit.AddChild(why);
            content.AddChild(fit);
            row = new Row(m, picked, button, why);
            _rows.Add(row);
        }

        var service = Machines.Count > 0 ? Sim.Garage.Service(Machines[0], Machines.Any(m => m.Def.Configurations.Count > 0)) : null;
        var note = "New options cost what they cost more than the ones they replace, and the work.";
        if (service is var (w, factor) && factor > 1f)
            note = $"A mechanic from {w.Poi.Name} comes out, for {(factor - 1f) * 100f:0}% more than in its bay. {note}";
        AddChild(Widgets.Dialog(service?.workshop.Poi.Name ?? "Workshop", content, $"{note} Esc closes."));
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    /// <summary>Repairs or repaints each machine that can be.</summary>
    private void Serve(Func<Machine, string?> blocker, Func<Machine, bool> serve)
    {
        foreach (var m in Machines.Where(m => blocker(m) == null)) serve(m);
        Refresh();
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        var repair = Service("Repair", _repair, Sim.Garage.RepairPrice, Sim.Garage.RepairBlocker);
        var repaint = Service("Repaint", _repaint, Sim.Garage.RepaintPrice, Sim.Garage.RepaintBlocker);
        _why.Text = repair != null && repaint != null ? repair == repaint ? repair : $"{repair}; {repaint.ToLowerInvariant()}" : "";
        foreach (var (m, picked, fit, label) in _rows)
        {
            var def = m.Def.Configure(picked);
            var why = Sim.Garage.ConfigureBlocker(m, def);
            fit.Text = def == m.Def ? "Fit" : $"Fit (${Sim.Garage.ConfigurePrice(m, def):N0})";
            fit.Disabled = why != null;
            label.Text = why ?? "";
        }
    }

    /// <summary>A service's button for the machines: what it costs them all, and why none can have it (when so).</summary>
    private string? Service(string name, Button button, Func<Machine, float> price, Func<Machine, string?> blocker)
    {
        var cost = Machines.Where(m => blocker(m) == null).Sum(price);
        var why = Machines.All(m => blocker(m) != null) ? Machines.Select(blocker).FirstOrDefault(b => b == "Not enough money") ?? Machines.Select(blocker).FirstOrDefault() : null;
        button.Text = cost < 0.5f ? name : $"{name} (${cost:N0})";
        button.Disabled = why != null || Machines.Count == 0;
        return why;
    }
}
