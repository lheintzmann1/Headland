using System.Text.Json;
using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Saves;

namespace Headland.Core.Machines.Components;

// A machine type is built from components (FS: specializations): running gear, a motor, fill units, work areas…
// Each kind has its def (under "components" in the machine's JSON), its runtime component with the state it keeps and
// saves, and a view in the game. Kinds are registered in ComponentKinds.

/// <summary>A component of a machine type, as its JSON gives it under <c>components</c>.</summary>
public abstract class ComponentDef
{
    /// <summary>Its kind: its key under <c>components</c>.</summary>
    [JsonIgnore]
    public string Kind => ComponentKinds.NameOf(GetType());

    /// <summary>Model node roles its view moves: the keys it allows in the machine's <c>visual.nodes</c>.</summary>
    [JsonIgnore]
    public virtual IEnumerable<string> Roles => [];

    /// <summary>What's wrong with it, checked against the rest of its machine.</summary>
    internal virtual IEnumerable<string> Errors(MachineDef machine, ContentDatabase content) => [];

    internal abstract MachineComponent Create(Machine machine);

    /// <summary>A fill unit of the machine's <c>fillUnits</c>, by id.</summary>
    protected static bool HasUnit(MachineDef machine, string? id) =>
        id != null && machine.Get<FillUnitsDef>()?.Units.Any(u => u.Id == id) == true;
}

/// <summary>A component of one machine: its runtime state, what it does each tick, and what it saves.</summary>
public abstract class MachineComponent(Machine machine)
{
    public Machine Machine { get; } = machine;

    public abstract ComponentDef Definition { get; }

    /// <summary>Every tick, once the vehicles have moved: animations, transfers.</summary>
    internal virtual void Update(Simulation sim, float dt)
    {
    }

    /// <summary>The machine was just hitched to another.</summary>
    internal virtual void OnHitched()
    {
    }

    /// <summary>The machine was just unhitched.</summary>
    internal virtual void OnDetached()
    {
    }

    /// <summary>The state to save, or null when it keeps none.</summary>
    internal virtual JsonElement? SaveState(ContentDatabase content) => null;

    /// <summary>Restores what <see cref="SaveState"/> wrote, once the machine is hitched as it was.</summary>
    internal virtual void LoadState(JsonElement data, SaveContext context)
    {
    }
}

public abstract class MachineComponent<TDef>(Machine machine, TDef def) : MachineComponent(machine) where TDef : ComponentDef
{
    public TDef Def { get; } = def;
    public override ComponentDef Definition => Def;
}

/// <summary>A component keeping state in saves, as a <typeparamref name="TSave"/> (missing properties load as defaults).</summary>
public abstract class MachineComponent<TDef, TSave>(Machine machine, TDef def) : MachineComponent<TDef>(machine, def)
    where TDef : ComponentDef where TSave : class, new()
{
    protected abstract TSave Capture(ContentDatabase content);

    protected abstract void Restore(TSave save, SaveContext context);

    internal sealed override JsonElement? SaveState(ContentDatabase content) => JsonSerializer.SerializeToElement(Capture(content), SaveGame.Json);

    internal sealed override void LoadState(JsonElement data, SaveContext context) =>
        Restore(data.Deserialize<TSave>(SaveGame.Json) ?? new TSave(), context);
}

/// <summary>What a component restoring from a save may need: the content, and where to say what couldn't be restored.</summary>
public sealed record SaveContext(ContentDatabase Content, List<string> Warnings);

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
