using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>Three-point linkages (rearLinkage…), whose lower links follow the implement up and down.</summary>
public partial class AttacherJointsView : MachineComponentView
{
    public AttacherJoints Joints { get; init; } = null!;

    public override void _Process(double delta)
    {
        foreach (var j in Joints.Def.Joints)
        {
            if (Rig.Part(AttacherJointsDef.LinkageRole(j)) is not { } links) continue;
            // Up and down with the implement it carries; down without one.
            var lift = Machine.Attached.GetValueOrDefault(j.Id)?.Get<Attachable>() is { Def.Mode: "mounted" } a ? (1f - a.LowerAnim) * a.Def.Lift : 0f;
            links.Node.Position = links.Position + new Vector3(0f, lift / Rig.Scale, 0f);
        }
    }
}
