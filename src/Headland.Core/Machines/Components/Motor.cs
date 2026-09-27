using Headland.Core.Content;
using Headland.Core.World;

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
        if (machine.Get<RunningGearDef>() is not { Kind: not SteeringKind.None }) yield return "needs a runningGear that steers";
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

    /// <summary>
    /// Speeds the vehicle up, brakes or coasts toward what the driver asks, as fast as the implements working in its
    /// chain, the engine's power and the ground allow. Returns its new speed (m/s).
    /// </summary>
    internal float Drive(Simulation sim, VehicleInput input, float dt)
    {
        var v = Machine;
        var world = sim.World;
        v.Status = null;
        var maxF = MaxSpeed;
        var maxR = MaxReverse;
        var demand = 0f;
        var totalMass = 0f;
        foreach (var m in v.Chain())
        {
            totalMass += m.SelfMassWithLoad(sim.Content);
            if (m.Get<WorkAreas>() is not { } w) continue;
            foreach (var area in w.Def.Areas)
            {
                if (!w.Working(area)) continue;
                maxF = MathF.Min(maxF, area.MaxWorkSpeedKmh * MathUtil.KmhToMs);
                demand += area.RequiredPowerHp;
            }
        }
        Load = demand / Def.PowerHp;
        if (demand > Def.PowerHp)
        {
            maxF *= MathF.Max(0.3f, Def.PowerHp / demand);
            v.Status = $"Needs {demand:N0} hp, has {Def.PowerHp:N0} hp";
        }
        var (cx, cz) = world.WorldToCell(v.Position);
        if (world.InBounds(cx, cz))
        {
            var i = world.CellIndex(cx, cz);
            if (world.IsWorkable((GroundType)world.Layers.Ground[i]) && world.Layers.Moisture[i] > 204)
                maxF *= 0.75f;
        }
        var accel = Def.Acceleration * Math.Clamp(v.SelfMassWithLoad(sim.Content) / MathF.Max(1f, totalMass), 0.25f, 1f);

        var s = v.Speed;
        if (input.Brake) s = MathUtil.MoveToward(s, 0f, Def.Braking * dt);
        else if (input.Throttle > 0.01f)
        {
            var target = maxF * input.Throttle;
            s = s < -0.01f ? MathUtil.MoveToward(s, 0f, Def.Braking * dt)
                : s < target ? MathF.Min(target, s + accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else if (input.Throttle < -0.01f)
        {
            var target = -maxR * -input.Throttle;
            s = s > 0.01f ? MathUtil.MoveToward(s, 0f, Def.Braking * dt)
                : s > target ? MathF.Max(target, s - accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else s = MathUtil.MoveToward(s, 0f, 1.5f * dt);
        if (s > maxF) s = MathUtil.MoveToward(s, maxF, Def.Braking * dt);
        return s;
    }
}
