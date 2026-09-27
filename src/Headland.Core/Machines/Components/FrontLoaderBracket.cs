using System.Text.Json.Serialization;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Consoles on a tractor's sides that a front loader arm hitches to, through a joint of type "frontLoader" between
/// them: <see cref="X"/>, <see cref="Z"/> and <see cref="Y"/> place the arm's pivots, <see cref="Width"/> apart.
/// </summary>
public sealed class FrontLoaderBracketDef : ComponentDef, IJointSource
{
    private AttacherJointDef[]? _joints;

    /// <summary>Id of the joint it adds.</summary>
    public string Joint { get; set; } = "frontLoader";
    public float X { get; set; }
    public float Z { get; set; } = 1.5f;
    public float Y { get; set; } = 1.2f;
    public float Width { get; set; } = 1.8f;

    [JsonIgnore]
    public IReadOnlyList<AttacherJointDef> Joints => _joints ??= [new AttacherJointDef { Id = Joint, Type = "frontLoader", X = X, Z = Z, Y = Y }];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (string.IsNullOrWhiteSpace(Joint)) yield return "needs a joint id";
        if (Width <= 0f) yield return "width must be > 0";
    }

    internal override MachineComponent Create(Machine machine) => new FrontLoaderBracket(machine, this);
}

public sealed class FrontLoaderBracket(Machine machine, FrontLoaderBracketDef def) : MachineComponent<FrontLoaderBracketDef>(machine, def)
{
    /// <summary>The loader arm hitched to it.</summary>
    public Machine? Arm => Machine.Attached.GetValueOrDefault(Def.Joint);
}
