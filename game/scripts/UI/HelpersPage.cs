using Headland.Core;
using Headland.Core.Input;
using Headland.Game.Controls;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The menu's helpers (FS: the helpers list): each helper at work, with its number, vehicle and field, its job and what
/// it's doing, how long it worked and its wages so far; one is dismissed from here, or the farmer takes a seat in its
/// vehicle (it goes on working).
/// </summary>
public partial class HelpersPage : MenuPage
{
    private GridContainer _table = null!;
    private Label _none = null!;
    private string _listed = "";
    private double _refresh;

    public GameRoot Game { get; init; } = null!;

    private Simulation Sim => Game.Sim;

    public override string Subtitle =>
        $"The helpers at work, ${Sim.HelperWage:N0} an hour each. Hire one with {InputLayer.Label(InputActions.Helper)} in a vehicle with an implement, a combine or a bale collector, on a field or beside it.";

    protected override void Build()
    {
        _table = new GridContainer { Columns = 8, ThemeTypeVariation = "TableGrid" };
        AddChild(_table);
        _none = Widgets.Label("No helper is at work.", "DimLabel");
        AddChild(_none);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.5;
        Refresh();
    }

    private void Refresh()
    {
        var helpers = Sim.Helpers.OrderBy(h => h.Number).ToList();
        _none.Visible = helpers.Count == 0;
        _table.Visible = helpers.Count > 0;
        var listed = string.Join("|", helpers.Select(h => $"{h.Number}:{h.Vehicle.Id}:{h.Describe()}:{(int)h.WorkedSeconds / 10}"));
        if (listed == _listed) return;
        _listed = listed;
        foreach (var cell in _table.GetChildren()) cell.Free();
        if (helpers.Count == 0) return;
        foreach (var header in new[] { "Helper", "Vehicle", "Field", "Job", "Worked", "Wages", "", "" })
            _table.AddChild(Widgets.Label(header, "DimLabel"));
        foreach (var h in helpers)
        {
            _table.AddChild(Widgets.Label($"Helper {h.Number}", "StrongLabel"));
            _table.AddChild(Widgets.Label(string.Join(", ", h.Vehicle.Chain().Select(m => m.Def.Name))));
            _table.AddChild(Widgets.Label($"{h.Field.Label} ({h.Field.AreaHa:0.00} ha)"));
            _table.AddChild(Widgets.Label($"{h.Type.Name}: {h.Describe()}"));
            var minutes = (int)(h.WorkedSeconds / 60.0);
            _table.AddChild(Widgets.Label(minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min"));
            _table.AddChild(Widgets.Label($"$ {h.Wages:N0}"));
            var vehicle = h.Vehicle;
            _table.AddChild(Widgets.Button("Take a seat", () =>
            {
                if (!Sim.TakeSeat(vehicle)) return;
                Game.Screens.Top?.Close();
            }));
            var helper = h;
            _table.AddChild(Widgets.Button("Dismiss", () =>
            {
                Sim.Dismiss(helper);
                Refresh();
            }));
        }
    }
}
