using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>A seat: the farmer gets in and drives, or a helper does. Needs a motor.</summary>
public sealed class DrivableDef : ComponentDef
{
    public override IEnumerable<string> Roles => ["steeringWheel"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (machine.Get<MotorDef>() == null) yield return "needs a motor";
    }

    internal override MachineComponent Create(Machine machine) => new Drivable(machine, this);
}

public sealed class Drivable(Machine machine, DrivableDef def) : MachineComponent<DrivableDef>(machine, def)
{
    /// <summary>Who drives: the farmer's controls, a helper, or nobody (it brakes).</summary>
    public IVehicleController? Controller { get; set; }

    /// <summary>What the driver asks for this tick.</summary>
    internal VehicleInput Input(float dt) => Controller?.GetInput(Machine, dt) ?? new VehicleInput { Brake = true };
}
