using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// The wheels on each side of each axle (wheel0L, wheel0R, wheel1L…): they roll with the distance driven, and steered
/// ones turn, the inner wheel of a turn more than the outer one.
/// </summary>
public partial class RunningGearView : ComponentView
{
    public RunningGear Gear { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var axles = Gear.Def.Axles;
        for (var i = 0; i < axles.Length; i++)
        foreach (var (side, x) in Sides(axles[i]))
        {
            var wheel = PlaceholderBuilder.Wheel(axles[i].Wheels, new Vector3(x, axles[i].Wheels.Radius, axles[i].Z));
            Rig.Root.AddChild(wheel);
            Rig.Add($"wheel{i}{side}", wheel);
        }
    }

    public override void _Process(double delta)
    {
        var axles = Gear.Def.Axles;
        // Euler order is YXZ: steering (Y) turns the wheel, rolling (X) spins it about its axle.
        for (var i = 0; i < axles.Length; i++)
        foreach (var (side, x) in Sides(axles[i]))
            Turn($"wheel{i}{side}", new Vector3(Gear.Distance / axles[i].Wheels.Radius, Gear.WheelAngle(i, x), 0f));
    }

    /// <summary>An axle's two sides: role suffix and x of its wheels (+x is left).</summary>
    private static (string side, float x)[] Sides(AxleDef a) => [("L", a.Track * 0.5f), ("R", -a.Track * 0.5f)];
}
