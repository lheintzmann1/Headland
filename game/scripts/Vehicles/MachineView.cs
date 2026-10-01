using Headland.Game.Common;
using Headland.Game.Components;
using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// Follows a Core machine on the terrain (with pitch and roll), or a tool where the crane joint carrying it holds it (a
/// fork on a loader arm, lifted and tilted with it), and draws its glTF model (e.g. from Blockbench). A view per
/// component moves its parts: wheels, pipe, tipper, lights…
/// </summary>
public partial class MachineView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public Machine Machine { get; init; } = null!;
    /// <summary>The view of a machine, by its id (the one carrying this one on a crane joint).</summary>
    public Func<int, Node3D?> MachineViewOf { get; init; } = _ => null;

    public override void _Ready()
    {
        Name = $"{Machine.Def.Id}_{Machine.Id}";
        var rig = Rig.Model(Machine.Def);
        AddChild(rig.Root);
        foreach (var c in Machine.Components)
            if (ComponentView.For(c, Sim, rig) is { } view)
                AddChild(view);
    }

    public override void _Process(double delta)
    {
        var m = Machine;
        if (m.ParentJointDef is { Crane: not null } joint && MachineViewOf(m.Parent!.Id) is { } carrier)
        {
            var a = m.Get<Attachable>()!.Def;
            GlobalTransform = carrier.GlobalTransform * m.Parent.JointFrame(joint).ToGodot() * new Transform3D(Basis.Identity, new Vector3(-a.X, 0f, -a.Z));
            return;
        }
        // Tilted with the ground under it, or a heap it drives onto.
        var (y, slope, tilt) = m.GroundPose(Sim.World);
        var pitch = Mathf.Atan(-slope);
        var roll = Mathf.Atan(tilt);
        var basis = new Basis(Vector3.Up, m.Heading) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        GlobalTransform = new Transform3D(basis, new Vector3(m.Position.X, y, m.Position.Y));
    }
}
