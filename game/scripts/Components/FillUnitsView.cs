using Headland.Game.Common;
using Headland.Core.Components;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Components;

/// <summary>
/// The load (load): modeled full with its pivot at the bottom, and scaled with the fill level of the unit a tipper
/// empties, else of the first unit that isn't fuel. Its fill materials take the fill type's color.
/// </summary>
public partial class FillUnitsView : ComponentView
{
    private FillUnit? _unit;
    private string? _color;

    public FillUnits Units { get; init; } = null!;

    public override void _Ready()
    {
        var fuel = Entity.Get<Motor>()?.FuelTank;
        _unit = Entity.Get<Tipper>()?.Load ?? Units.Units.FirstOrDefault(u => u != fuel);
    }

    public override void _Process(double delta)
    {
        if (_unit == null || Rig.Part("load") is not { } load) return;
        var empty = _unit.IsEmpty;
        load.Node.Visible = !empty;
        if (empty) return;
        load.Node.Scale = load.Scale with { Y = load.Scale.Y * Mathf.Max(0.02f, _unit.Fraction) };
        if (_unit.FillType == _color || !Sim.Content.FillTypes.TryGetValue(_unit.FillType!, out var ft)) return;
        _color = _unit.FillType;
        Rig.SetFillColor(Conv.Hex(ft.Color));
    }
}
