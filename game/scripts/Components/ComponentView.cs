using Headland.Game.Vehicles.Components;
using Headland.Core;
using Headland.Core.Components;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Components;

/// <summary>
/// Draws one component of an entity (a machine, a POI), as a child of its view: moves the rig parts of its roles, and
/// adds what isn't part of the model (lights, a rope, falling grain).
/// </summary>
public partial class ComponentView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public Rig Rig { get; init; } = null!;
    public Entity Owner { get; init; } = null!;

    /// <summary>The view drawing <paramref name="c"/>, or null for a component with nothing to show (a motor).</summary>
    public static ComponentView? For(Component c, Simulation sim, Rig rig) => c switch
    {
        RunningGear g => new RunningGearView { Sim = sim, Rig = rig, Owner = c.Owner, Gear = g },
        Drivable d => new DrivableView { Sim = sim, Rig = rig, Owner = c.Owner, Seat = d },
        AttacherJoints j => new AttacherJointsView { Sim = sim, Rig = rig, Owner = c.Owner, Joints = j },
        Attachable a => new AttachableView { Sim = sim, Rig = rig, Owner = c.Owner, Hitch = a },
        FillUnits f => new FillUnitsView { Sim = sim, Rig = rig, Owner = c.Owner, Units = f },
        AnimatedParts p => new AnimatedPartsView { Sim = sim, Rig = rig, Owner = c.Owner, Parts = p },
        WorkAreas w => new WorkAreasView { Sim = sim, Rig = rig, Owner = c.Owner, Areas = w },
        Pipe p => new PipeView { Sim = sim, Rig = rig, Owner = c.Owner, Pipe = p },
        Tipper t => new TipperView { Sim = sim, Rig = rig, Owner = c.Owner, Tipper = t },
        Lights l => new LightsView { Sim = sim, Rig = rig, Owner = c.Owner, Lights = l },
        CraneArm a => new CraneArmView { Sim = sim, Rig = rig, Owner = c.Owner, Crane = a },
        Winch w => new WinchView { Sim = sim, Rig = rig, Owner = c.Owner, Winch = w },
        Saw s => new SawView { Sim = sim, Rig = rig, Owner = c.Owner, Saw = s },
        _ => null,
    };

    protected static float Ease(float t) => t * t * (3f - 2f * t);

    /// <summary>[x, y, z] from a def.</summary>
    protected static Vector3 Vec(float[] v) => v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;

    /// <summary>Turns a part from its rest pose by <paramref name="by"/> (radians, Euler YXZ).</summary>
    protected void Turn(string role, Vector3 by)
    {
        if (Rig.Part(role) is { } p) p.Node.Rotation = p.Rotation + by;
    }
}
