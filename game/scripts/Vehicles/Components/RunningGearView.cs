using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// The wheels on each side of each axle (wheel0L, wheel0R, wheel1L…): they roll with the distance driven, and steered
/// ones turn, the inner wheel of a turn more than the outer one. Tracks (track0L…) don't turn; on a placeholder their
/// belt runs round.
/// </summary>
public partial class RunningGearView : ComponentView
{
    private readonly List<TrackRig> _tracks = [];

    public RunningGear Gear { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var axles = Gear.Def.Axles;
        for (var i = 0; i < axles.Length; i++)
        foreach (var (side, x) in Sides(axles[i]))
        {
            var set = axles[i].Wheels;
            Node3D node;
            if (set.IsTracks)
            {
                var track = new TrackRig(set, new Vector3(x, 0f, axles[i].Z), Body);
                _tracks.Add(track);
                node = track.Root;
            }
            else node = PlaceholderBuilder.Wheel(set, new Vector3(x, set.Radius, axles[i].Z), Mathf.Sign(x));
            Rig.Root.AddChild(node);
            Rig.Add(RunningGearDef.SideRole(axles[i], i, side), node);
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
                Turn(RunningGearDef.SideRole(axles[i], i, side), new Vector3(Gear.Distance / axles[i].Wheels.Radius, Gear.WheelAngle(i, x), 0f));
        }
        foreach (var track in _tracks) track.Roll(Gear.Distance);
    }

    /// <summary>An axle's two sides: role suffix and x of its (inner) wheels, +x being left.</summary>
    private static (string side, float x)[] Sides(AxleDef a) => [("L", a.Track * 0.5f), ("R", -a.Track * 0.5f)];
}
