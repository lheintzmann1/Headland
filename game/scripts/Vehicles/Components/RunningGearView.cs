using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>Wheels (wheel0…N): they roll with the distance driven, and the steered ones turn.</summary>
public partial class RunningGearView : ComponentView
{
    public RunningGear Gear { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var wheels = Gear.Def.Wheels;
        for (var i = 0; i < wheels.Length; i++)
        {
            var wheel = PlaceholderBuilder.Wheel(wheels[i]);
            Rig.Root.AddChild(wheel);
            Rig.Add($"wheel{i}", wheel);
        }
    }

    public override void _Process(double delta)
    {
        // A combine's rear wheels turn the other way to steer the same way.
        var steer = Gear.Def.SteersRear ? -Gear.SteerAngle : Gear.SteerAngle;
        var wheels = Gear.Def.Wheels;
        // Euler order is YXZ: steering (Y) turns the wheel, rolling (X) spins it about its axle.
        for (var i = 0; i < wheels.Length; i++)
            Turn($"wheel{i}", new Vector3(Gear.Distance / wheels[i].Radius, wheels[i].Steer ? steer : 0f, 0f));
    }
}
