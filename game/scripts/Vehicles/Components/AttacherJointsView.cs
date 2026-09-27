using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// Three-point linkages (rearLinkage…), whose lower links follow the implement up and down. A placeholder builds its
/// hitches: linkages, drawbar jaws and fifth-wheel plates.
/// </summary>
public partial class AttacherJointsView : ComponentView
{
    public AttacherJoints Joints { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        foreach (var j in Joints.Def.Joints)
        {
            // Toward the middle of the machine.
            var inward = j.Z > Machine.Def.Size.CenterZ ? -1f : 1f;
            switch (j.Type)
            {
                case "threePoint":
                    PlaceholderBuilder.Box(Rig, new Vector3(0.9f, 0.5f, 0.25f), new Vector3(j.X, j.Y + 0.2f, j.Z + inward * 0.25f), Materials.DarkSteel);
                    var links = new Node3D { Name = "Linkage" };
                    Rig.AddPart(links, new Vector3(j.X, 0f, j.Z));
                    foreach (var side in new[] { 0.38f, -0.38f })
                        PlaceholderBuilder.Box(links, new Vector3(0.07f, 0.07f, 0.45f), new Vector3(side, j.Y, inward * 0.22f), Materials.DarkSteel);
                    PlaceholderBuilder.Box(links, new Vector3(0.9f, 0.07f, 0.07f), new Vector3(0f, j.Y, 0f), Materials.Steel);
                    Rig.Add(AttacherJointsDef.LinkageRole(j), links);
                    break;
                case "drawbar":
                    PlaceholderBuilder.Box(Rig, new Vector3(0.16f, 0.1f, 0.4f), new Vector3(j.X, j.Y, j.Z + inward * 0.18f), Materials.DarkSteel);
                    break;
                case "fifthWheel":
                    PlaceholderBuilder.Box(Rig, new Vector3(1.1f, 0.12f, 1.0f), new Vector3(j.X, j.Y - 0.06f, j.Z), Materials.DarkSteel);
                    break;
            }
        }
    }

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
