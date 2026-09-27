using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>An engine: a machine with one drives itself (with a running gear that steers) rather than being pulled.</summary>
public sealed class MotorDef : ComponentDef
{
    public float PowerHp { get; set; } = 100f;
    public float MaxSpeedKmh { get; set; } = 40f;
    public float MaxReverseKmh { get; set; } = 15f;
    /// <summary>Speeding up and braking (m/s²).</summary>
    public float Acceleration { get; set; } = 2.5f;
    public float Braking { get; set; } = 6f;
    /// <summary>Fill unit holding its fuel, filled up at refuel POIs.</summary>
    public string? FuelUnit { get; set; }

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (PowerHp <= 0f || MaxSpeedKmh <= 0f || MaxReverseKmh <= 0f || Acceleration <= 0f || Braking <= 0f)
            yield return "powerHp, speeds, acceleration and braking must be > 0";
        if (machine.Get<RunningGearDef>() is not { } gear || gear.Wheelbase() <= 0f) yield return "needs a runningGear that steers";
        if (FuelUnit != null && !HasUnit(machine, FuelUnit)) yield return $"fuel unit '{FuelUnit}' missing";
    }

    internal override MachineComponent Create(Machine machine) => new Motor(machine, this);
}

public sealed class Motor(Machine machine, MotorDef def) : MachineComponent<MotorDef>(machine, def)
{
    /// <summary>Power the working implements ask for, as a share of the engine's (above 1 it can't keep their speed).</summary>
    public float Load { get; internal set; }

    public FillUnit? FuelTank => Machine.Unit(Def.FuelUnit);

    public float MaxSpeed => Def.MaxSpeedKmh * MathUtil.KmhToMs;
    public float MaxReverse => Def.MaxReverseKmh * MathUtil.KmhToMs;
}
