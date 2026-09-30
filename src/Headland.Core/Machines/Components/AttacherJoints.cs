using System.Text.Json.Serialization;
using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class AttacherJointDef
{
    public string Id { get; set; } = "";
    /// <summary>A joint type's id (jointtypes.json): an implement hitches to a joint of its attachable's type.</summary>
    public string Type { get; set; } = "threePoint";
    public float X { get; set; }
    public float Z { get; set; }
    public float Y { get; set; } = 0.6f;
    /// <summary>
    /// An implement on it switches the vehicle to its top lights (FS: useTopLights), as a front loader hides the
    /// headlights in the hood; by default for a joint in front of the vehicle's origin.
    /// </summary>
    public bool? UseTopLights { get; set; }

    /// <summary>Whether an implement on it switches the vehicle to its top lights (<see cref="UseTopLights"/>).</summary>
    [JsonIgnore]
    public bool TopLights => UseTopLights ?? Z > 0f;

    /// <summary>Its <see cref="Type"/>, once linked to the content.</summary>
    [JsonIgnore]
    public JointTypeDef? TypeDef { get; internal set; }

    /// <summary>What's wrong with a joint type's id: one the content doesn't have.</summary>
    internal static string? TypeError(string type, ContentDatabase content) =>
        content.JointTypes.ContainsKey(type) ? null : $"unknown type '{type}' (known: {string.Join(", ", content.JointTypes.Keys)})";
}

/// <summary>Where implements hitch: three-point linkages, drawbars, fifth wheels, a combine's feeder house.</summary>
public sealed class AttacherJointsDef : MachineComponentDef, IJointSource, ISpecSource
{
    public AttacherJointDef[] Joints { get; set; } = [];

    IReadOnlyList<AttacherJointDef> IJointSource.Joints => Joints;

    /// <summary>What implements hitch to, where the joint's own name says more: "Three-point linkage (rear), Drawbar".</summary>
    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content)
    {
        if (Joints.Length == 0) yield break;
        yield return new Spec("Hitches", string.Join(", ", Joints.Select(j =>
        {
            var name = j.TypeDef?.Name ?? j.Type;
            return string.Equals(j.Id, j.Type, StringComparison.OrdinalIgnoreCase) ? name : $"{name} ({j.Id})";
        })));
    }

    /// <summary>A linkage's lower links (rearLinkage…), lifted with the implement they carry.</summary>
    public override IEnumerable<string> Roles => Joints.Where(j => j.TypeDef?.Linkage == true).Select(LinkageRole);

    public static string LinkageRole(AttacherJointDef joint) => $"{joint.Id}Linkage";

    internal override void Link(ContentDatabase content)
    {
        foreach (var j in Joints) j.TypeDef = content.JointTypes.GetValueOrDefault(j.Type);
    }

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        foreach (var j in Joints)
            if (AttacherJointDef.TypeError(j.Type, content) is { } error) yield return $"joint '{j.Id}': {error}";
    }

    internal override Component Create(Machine machine) => new AttacherJoints(machine, this);
}

/// <summary>The joints of a machine. What hangs on each is <see cref="Machine.Attached"/>.</summary>
public sealed class AttacherJoints(Machine machine, AttacherJointsDef def) : MachineComponent<AttacherJointsDef>(machine, def);
