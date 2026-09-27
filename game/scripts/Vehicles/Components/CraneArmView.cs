using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// A crane's joints (roles: the joints' ids), each moved from the pose it's modeled in (its value 0): turned (yaw, pitch)
/// or slid along its own +Z (extend).
/// </summary>
public partial class CraneArmView : ComponentView
{
    public CraneArm Crane { get; init; } = null!;

    public override void _Process(double delta)
    {
        foreach (var j in Crane.Joints)
        {
            if (Rig.Part(j.Def.Id) is not { } part) continue;
            var v = j.Value;
            switch (j.Def.Axis)
            {
                case "yaw":
                    part.Node.Rotation = part.Rotation + new Vector3(0f, Mathf.DegToRad(v), 0f);
                    break;
                case "pitch":
                    part.Node.Rotation = part.Rotation + new Vector3(-Mathf.DegToRad(v), 0f, 0f);
                    break;
                default:
                    part.Node.Position = part.Position + Basis.FromEuler(part.Rotation) * new Vector3(0f, 0f, v / Rig.Scale);
                    break;
            }
        }
    }
}
