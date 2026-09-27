using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The steering wheel (steeringWheel), turned with the steered wheels: 270° either way at full lock.</summary>
public partial class DrivableView : ComponentView
{
    private RunningGear? _gear;

    public Drivable Seat { get; init; } = null!;

    public override void _Ready() => _gear = Machine.Get<RunningGear>();

    public override void _Process(double delta)
    {
        if (_gear == null) return;
        Turn("steeringWheel", new Vector3(0f, _gear.SteerAngle / _gear.Def.MaxSteer * Mathf.Pi * 1.5f, 0f));
    }
}
