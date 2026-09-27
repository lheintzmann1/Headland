using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// A crane's joints (roles: the joints' ids), each moved from the pose it's modeled in (its value 0): turned (yaw, pitch)
/// or slid along its own +Z (extend). The placeholder nests a boom per joint.
/// </summary>
public partial class CraneArmView : ComponentView
{
    public CraneArm Crane { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var parent = Rig.Root;
        foreach (var j in Crane.Def.Joints)
        {
            var node = new Node3D { Name = j.Id, Position = Vec(j.Offset) };
            parent.AddChild(node);
            if (j.Length > 0f)
            {
                var thick = j.Axis == "extend" ? 0.16f : 0.24f;
                PlaceholderBuilder.Box(node, new Vector3(thick, thick, j.Length), new Vector3(0f, 0f, j.Length * 0.5f), j.Axis == "extend" ? Materials.Steel : Body);
            }
            else if (j.Axis == "yaw")
                node.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.4f, Height = 0.3f, RadialSegments = 16 },
                    Position = new Vector3(0f, -0.15f, 0f),
                    MaterialOverride = Materials.Get(Materials.DarkSteel, 0.7f),
                });
            Rig.Add(j.Id, node);
            parent = node;
        }
    }

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
