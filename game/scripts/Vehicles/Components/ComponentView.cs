using Headland.Game.Common;
using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// Draws one component of a machine, as a child of its <see cref="MachineView"/>: moves the rig parts of its roles
/// and, on a placeholder, builds the parts it needs (wheels, a pipe, a crane's booms).
/// </summary>
public partial class ComponentView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public MachineRig Rig { get; init; } = null!;
    public Machine Machine { get; init; } = null!;

    /// <summary>The view drawing <paramref name="c"/>, or null for a component with nothing to show (a motor).</summary>
    public static ComponentView? For(MachineComponent c, Simulation sim, MachineRig rig) => c switch
    {
        RunningGear g => new RunningGearView { Sim = sim, Rig = rig, Machine = c.Machine, Gear = g },
        Drivable d => new DrivableView { Sim = sim, Rig = rig, Machine = c.Machine, Seat = d },
        AttacherJoints j => new AttacherJointsView { Sim = sim, Rig = rig, Machine = c.Machine, Joints = j },
        FrontLoaderBracket b => new FrontLoaderBracketView { Sim = sim, Rig = rig, Machine = c.Machine, Bracket = b },
        Attachable a => new AttachableView { Sim = sim, Rig = rig, Machine = c.Machine, Hitch = a },
        FillUnits f => new FillUnitsView { Sim = sim, Rig = rig, Machine = c.Machine, Units = f },
        AnimatedParts p => new AnimatedPartsView { Sim = sim, Rig = rig, Machine = c.Machine, Parts = p },
        WorkAreas w => new WorkAreasView { Sim = sim, Rig = rig, Machine = c.Machine, Areas = w },
        Pipe p => new PipeView { Sim = sim, Rig = rig, Machine = c.Machine, Pipe = p },
        Tipper t => new TipperView { Sim = sim, Rig = rig, Machine = c.Machine, Tipper = t },
        Lights l => new LightsView { Sim = sim, Rig = rig, Machine = c.Machine, Lights = l },
        CraneArm a => new CraneArmView { Sim = sim, Rig = rig, Machine = c.Machine, Crane = a },
        Winch w => new WinchView { Sim = sim, Rig = rig, Machine = c.Machine, Winch = w },
        Saw s => new SawView { Sim = sim, Rig = rig, Machine = c.Machine, Saw = s },
        _ => null,
    };

    /// <summary>The machine's paint.</summary>
    protected Color Body => Conv.Hex(Machine.Def.Visual.Color);

    protected static float Ease(float t) => t * t * (3f - 2f * t);

    /// <summary>[x, y, z] from a def.</summary>
    protected static Vector3 Vec(float[] v) => v.Length == 3 ? new Vector3(v[0], v[1], v[2]) : Vector3.Zero;

    /// <summary>Turns a part from its rest pose by <paramref name="by"/> (radians, Euler YXZ).</summary>
    protected void Turn(string role, Vector3 by)
    {
        if (Rig.Part(role) is { } p) p.Node.Rotation = p.Rotation + by;
    }
}
