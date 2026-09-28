using Headland.Core.Components;
using System.Text.Json.Serialization;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Consoles on a tractor's sides that a front loader arm hitches to, through a joint of type <see cref="Type"/> between
/// them: <see cref="X"/>, <see cref="Z"/> and <see cref="Y"/> place the arm's pivots, <see cref="Width"/> apart.
/// </summary>
public sealed class FrontLoaderBracketDef : MachineComponentDef, IJointSource
{
    private AttacherJointDef[]? _joints;

    /// <summary>Id of the joint it adds.</summary>
    public string Joint { get; set; } = "frontLoader";
    /// <summary>The joint's type (jointtypes.json): what loader arms hitch to.</summary>
    public string Type { get; set; } = "frontLoader";
    public float X { get; set; }
    public float Z { get; set; } = 1.5f;
    public float Y { get; set; } = 1.2f;
    public float Width { get; set; } = 1.8f;

    [JsonIgnore]
    public IReadOnlyList<AttacherJointDef> Joints => _joints ??= [new AttacherJointDef { Id = Joint, Type = Type, X = X, Z = Z, Y = Y }];

    internal override void Link(ContentDatabase content)
    {
        foreach (var j in Joints) j.TypeDef = content.JointTypes.GetValueOrDefault(j.Type);
    }

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (string.IsNullOrWhiteSpace(Joint)) yield return "needs a joint id";
        if (AttacherJointDef.TypeError(Type, content) is { } error) yield return error;
        if (Width <= 0f) yield return "width must be > 0";
    }

    internal override Component Create(Machine machine) => new FrontLoaderBracket(machine, this);
}

public sealed class FrontLoaderBracket(Machine machine, FrontLoaderBracketDef def) : MachineComponent<FrontLoaderBracketDef>(machine, def)
{
    /// <summary>The loader arm hitched to it.</summary>
    public Machine? Arm => Machine.Attached.GetValueOrDefault(Def.Joint);
}
