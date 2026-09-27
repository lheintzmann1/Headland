using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

// The kinds of components only machines have: running gear, a motor, work areas… (see Components/Component.cs).

/// <summary>A kind of component that goes on machines only.</summary>
public abstract class MachineComponentDef : ComponentDef
{
    internal sealed override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content) =>
        owner is MachineDef machine ? Errors(machine, content) : ["goes on machines only"];

    /// <summary>What's wrong with it, checked against the rest of its machine.</summary>
    internal virtual IEnumerable<string> Errors(MachineDef machine, ContentDatabase content) => [];

    internal sealed override Component Create(Entity owner) => Create((Machine)owner);

    internal abstract Component Create(Machine machine);

    /// <summary>A fill unit of the machine's <c>fillUnits</c>, by id.</summary>
    protected static bool HasUnit(MachineDef machine, string? id) =>
        id != null && machine.Get<FillUnitsDef>()?.Units.Any(u => u.Id == id) == true;
}

/// <summary>A component of one machine.</summary>
public abstract class MachineComponent<TDef>(Machine machine, TDef def) : Component<TDef>(machine, def) where TDef : ComponentDef
{
    public Machine Machine { get; } = machine;
}

/// <summary>A component of one machine keeping state in saves, as a <typeparamref name="TSave"/>.</summary>
public abstract class MachineComponent<TDef, TSave>(Machine machine, TDef def) : Component<TDef, TSave>(machine, def)
    where TDef : ComponentDef where TSave : class, new()
{
    public Machine Machine { get; } = machine;
}

/// <summary>A component the turn-on key switches: a seed drill, a thresher, a saw.</summary>
public interface ISwitchable
{
    /// <summary>False when this machine's kind of it runs without being turned on.</summary>
    bool CanTurnOn { get; }

    bool On { get; set; }
}

/// <summary>A component def with attacher joints, where implements hitch.</summary>
public interface IJointSource
{
    IReadOnlyList<AttacherJointDef> Joints { get; }
}
