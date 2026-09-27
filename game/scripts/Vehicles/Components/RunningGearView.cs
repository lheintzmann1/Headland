using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// The wheels on each side of each axle (wheel0L, wheel0R, wheel1L…): they roll with the distance driven, those on the
/// outside of a turn further, and steered ones turn, the inner wheel of a turn more than the outer one. Tracks (track0L…)
/// don't turn, but the links of their belt (under track0L_belt) run round. An articulated machine's front frame
/// (frontFrame) swings.
/// </summary>
public partial class RunningGearView : ComponentView
{
    private readonly List<(TrackBelt belt, List<Node3D> links, float x)> _belts = [];

    public RunningGear Gear { get; init; } = null!;

    public override void _Ready()
    {
        var axles = Gear.Def.Axles;
        for (var i = 0; i < axles.Length; i++)
        foreach (var (side, x) in Sides(axles[i]))
        {
            var set = axles[i].Wheels;
            if (set.IsTracks && Rig.Part(RunningGearDef.SideRole(axles[i], i, side))?.Node is { } t && t.GetNodeOrNull<Node3D>($"{t.Name}_belt") is { } belt)
                _belts.Add((new TrackBelt(set), belt.GetChildren().OfType<Node3D>().ToList(), x));
        }
    }

    public override void _Process(double delta)
    {
        var axles = Gear.Def.Axles;
        // Euler order is YXZ: steering (Y) turns the wheel, rolling (X) spins it about its axle.
        for (var i = 0; i < axles.Length; i++)
        {
            if (axles[i].Wheels.IsTracks) continue;
            foreach (var (side, x) in Sides(axles[i]))
                Turn(RunningGearDef.SideRole(axles[i], i, side), new Vector3(Gear.SideDistance(x) / axles[i].Wheels.Radius, Gear.WheelAngle(i, x), 0f));
        }
        foreach (var (belt, links, x) in _belts) belt.Place(links, Gear.SideDistance(x));
        if (Gear.SteeringKind == SteeringKind.Articulated) Turn("frontFrame", new Vector3(0f, Gear.SteerAngle, 0f));
    }

    /// <summary>An axle's two sides: role suffix and x of its (inner) wheels, +x being left.</summary>
    private static (string side, float x)[] Sides(AxleDef a) => [("L", a.Track * 0.5f), ("R", -a.Track * 0.5f)];
}
