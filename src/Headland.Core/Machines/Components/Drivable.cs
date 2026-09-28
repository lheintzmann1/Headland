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
        set => _selected = value;
    }

    /// <summary>The implements of the chain in turn, then the vehicle again.</summary>
    public void SelectNext()
    {
        var implements = Machine.Chain().Skip(1).ToList();
        var i = Selected != null ? implements.IndexOf(Selected) : -1;
        Selected = i + 1 < implements.Count ? implements[i + 1] : null;
    }

    /// <summary>What the tool keys act on: the selected implement and the vehicle's own parts, or the whole chain.</summary>
    public IEnumerable<Machine> ToolScope => Selected is { } implement ? [Machine, implement] : Machine.Chain();

    /// <summary>What the driver asks for this tick.</summary>
    internal VehicleInput Input(float dt) => Controller?.GetInput(Machine, dt) ?? new VehicleInput { Brake = true };
}
