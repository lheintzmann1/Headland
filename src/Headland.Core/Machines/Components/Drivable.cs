using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>A seat: the farmer gets in and drives, or a helper does. Needs a motor.</summary>
public sealed class DrivableDef : MachineComponentDef
{
    public override IEnumerable<string> Roles => ["steeringWheel"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (machine.Get<MotorDef>() == null) yield return "needs a motor";
    }

    internal override Component Create(Machine machine) => new Drivable(machine, this);
}

public sealed class Drivable(Machine machine, DrivableDef def) : MachineComponent<DrivableDef>(machine, def)
{
    private Machine? _selected;

    /// <summary>Who drives: the farmer's controls, a helper, or nobody (it brakes).</summary>
    public IVehicleController? Controller { get; set; }

    /// <summary>
    /// The implement of its chain the tool keys (lower, turn on, fold, tip…) act on (FS: the selected implement); null
    /// when it's the vehicle itself, and then they act on the whole chain. It goes back to the vehicle once the
    /// implement leaves the chain. Not saved: it's where the driver's hand is, not the machine's state.
    /// </summary>
    public Machine? Selected
    {
        get => _selected != null && _selected != Machine && _selected.Root == Machine ? _selected : null;
        set
        {
            _selected = value;
            Group = 1;
        }
    }

    /// <summary>
    /// The control group of the selection's crane the tool keys move (FS: a subselection), from 1: the select key steps
    /// through them before going to the next implement.
    /// </summary>
    public int Group { get; private set; } = 1;

    /// <summary>
    /// The next control group of the selection's crane, else the implements of the chain in turn (each from its first
    /// group), then the vehicle again.
    /// </summary>
    public void SelectNext() => (Selected, Group) = Next;

    /// <summary>What the select key selects next: an implement (null for the vehicle) and its control group.</summary>
    public (Machine? machine, int group) Next
    {
        get
        {
            if (Group < GroupsOf(Selected ?? Machine)) return (Selected, Group + 1);
            var implements = Machine.Chain().Skip(1).ToList();
            var i = Selected != null ? implements.IndexOf(Selected) : -1;
            return (i + 1 < implements.Count ? implements[i + 1] : null, 1);
        }
    }

    /// <summary>Whether the select key has anything to step through: implements, or control groups.</summary>
    public bool CanSelect => Machine.Attached.Count > 0 || GroupsOf(Machine) > 1;

    /// <summary>What a control group of <paramref name="m"/>'s crane is called: its name, else its number.</summary>
    public static string GroupName(Machine m, int group) =>
        m.Get<CraneArm>()?.Def.Groups.ElementAtOrDefault(group - 1) ?? $"group {group}";

    /// <summary>How many control groups <paramref name="m"/>'s crane has (1 without one).</summary>
    public static int GroupsOf(Machine m) => m.Get<CraneArm>()?.Def.GroupCount ?? 1;

    /// <summary>The control group the tool keys move on <paramref name="m"/>: the selected one on the selection, else its first.</summary>
    public int GroupOf(Machine m) => m == (Selected ?? Machine) ? Math.Min(Group, GroupsOf(m)) : 1;

    /// <summary>What the tool keys act on: the selected implement and the vehicle's own parts, or the whole chain.</summary>
    public IEnumerable<Machine> ToolScope => Selected is { } implement ? [Machine, implement] : Machine.Chain();

    /// <summary>The tool keys held for <paramref name="dt"/> seconds: the cranes in the tool scope move, each its control group.</summary>
    internal void DriveTools(VehicleInput input, float dt)
    {
        if (input.ToolY == 0f && input.ToolX == 0f) return;
        foreach (var m in ToolScope)
            m.Get<CraneArm>()?.Drive(GroupOf(m), input.ToolY, input.ToolX, dt);
    }

    /// <summary>What the driver asks for this tick.</summary>
    internal VehicleInput Input(float dt) => Controller?.GetInput(Machine, dt) ?? new VehicleInput { Brake = true };
}
