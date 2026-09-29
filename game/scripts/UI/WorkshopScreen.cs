using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Pois.Components;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The use key in a workshop's bay (driving, or walking in beside the farm's machines): repair the chain, and change the options of its machines (wheels, engine,
/// hitches, color…) for what the new ones cost more and the work.
/// </summary>
public partial class WorkshopScreen : Screen
{
    private readonly List<Row> _rows = [];
    private Label _balance = null!;
    private Button _repair = null!;
    private Label _repairWhy = null!;
    private double _refresh;

    public Simulation Sim { get; init; } = null!;
    public Machine Vehicle { get; init; } = null!;
    public Workshop Workshop { get; init; } = null!;

    /// <summary>A machine of the chain, the options picked for it, and its fit button.</summary>
    private sealed record Row(Machine Machine, Dictionary<string, string> Picked, Button Fit, Label Why);

    protected override void Build()
    {
        var content = new VBoxContainer { ThemeTypeVariation = "DialogBox" };
        var account = new HBoxContainer();
        account.AddChild(Widgets.Label("Balance"));
        _balance = Widgets.Label();
        account.AddChild(_balance);
        content.AddChild(account);

        var repair = new HBoxContainer();
        _repair = Widgets.Button("", () =>
        {
            Sim.Pois.Repair(Vehicle.Chain(), Workshop);
            Refresh();
        });
        repair.AddChild(_repair);
        _repairWhy = Widgets.Label(variation: "DimLabel");
        repair.AddChild(_repairWhy);
        content.AddChild(repair);

        foreach (var m in Vehicle.Chain().Where(m => m.Def.Configurations.Count > 0))
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
                Sim.Pois.Configure(row.Machine, row.Picked);
                Refresh();
            });
            fit.AddChild(button);
            var why = Widgets.Label(variation: "DimLabel");
            fit.AddChild(why);
            content.AddChild(fit);
            row = new Row(m, picked, button, why);
            _rows.Add(row);
        }

        AddChild(Widgets.Dialog(Workshop.Poi.Name, content,
            "New options cost what they cost more than the ones they replace, and the work. Esc closes."));
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.25;
        Refresh();
    }

    private void Refresh()
    {
        Widgets.Balance(_balance, Sim.Economy.Money);
        var cost = Vehicle.Chain().Sum(m => Sim.Pois.RepairPrice(Workshop, m));
        var closed = Sim.Pois.Closed(Workshop.Poi, Workshop.Def);
        var cannot = closed ?? (cost < 0.5f ? "Nothing to repair" : cost > Sim.Economy.Money ? "Not enough money" : null);
        _repair.Text = cost < 0.5f ? "Repair" : $"Repair (${cost:N0})";
        _repair.Disabled = cannot != null;
        _repairWhy.Text = cannot ?? "";
        foreach (var (m, picked, fit, label) in _rows)
        {
            var def = m.Def.Configure(picked);
            var why = Sim.Pois.ConfigureBlocker(m, def, Workshop);
            fit.Text = def == m.Def ? "Fit" : $"Fit (${Sim.Pois.ConfigurePrice(Workshop, m, def):N0})";
            fit.Disabled = why != null;
            label.Text = why ?? "";
        }
    }
}
