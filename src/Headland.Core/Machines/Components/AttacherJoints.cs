using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class AttacherJointDef
{
    /// <summary>Joint types; an implement hitches to a joint of its attachable's type.</summary>
    public static readonly string[] Types = ["threePoint", "drawbar", "fifthWheel", "header", "frontLoader"];

    public string Id { get; set; } = "";
    /// <summary>One of <see cref="Types"/>.</summary>
    public string Type { get; set; } = "threePoint";
    public float X { get; set; }
    public float Z { get; set; }
    public float Y { get; set; } = 0.6f;
}

/// <summary>Where implements hitch: three-point linkages, drawbars, fifth wheels, a combine's feeder house.</summary>
public sealed class AttacherJointsDef : MachineComponentDef, IJointSource
{
    public AttacherJointDef[] Joints { get; set; } = [];

    IReadOnlyList<AttacherJointDef> IJointSource.Joints => Joints;

    /// <summary>A three-point linkage's lower links (rearLinkage…), lifted with the implement they carry.</summary>
    public override IEnumerable<string> Roles => Joints.Where(j => j.Type == "threePoint").Select(LinkageRole);

    public static string LinkageRole(AttacherJointDef joint) => $"{joint.Id}Linkage";

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        foreach (var j in Joints.Where(j => !AttacherJointDef.Types.Contains(j.Type)))
            yield return $"joint '{j.Id}' has unknown type '{j.Type}'";
    }

    internal override Component Create(Machine machine) => new AttacherJoints(machine, this);
}

/// <summary>The joints of a machine. What hangs on each is <see cref="Machine.Attached"/>.</summary>
public sealed class AttacherJoints(Machine machine, AttacherJointsDef def) : MachineComponent<AttacherJointsDef>(machine, def);
