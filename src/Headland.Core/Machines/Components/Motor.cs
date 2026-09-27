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
    /// <summary>Fuel burned in an hour at full power; by default what a diesel of its power burns (0.19 L per hp).</summary>
    public float? FuelPerHour { get; set; }

    public float FullPowerFuelPerHour => FuelPerHour ?? PowerHp * 0.19f;

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (PowerHp <= 0f || MaxSpeedKmh <= 0f || MaxReverseKmh <= 0f || Acceleration <= 0f || Braking <= 0f)
            yield return "powerHp, speeds, acceleration and braking must be > 0";
        if (FuelPerHour <= 0f) yield return "fuelPerHour must be > 0";
        if (machine.Get<RunningGearDef>() is not { SteeringKind: not SteeringKind.None }) yield return "needs a runningGear that steers";
        if (FuelUnit != null && !HasUnit(machine, FuelUnit)) yield return $"fuel unit '{FuelUnit}' missing";
    }

    internal override MachineComponent Create(Machine machine) => new Motor(machine, this);
}

public sealed class MotorSave
{
    public float Unburned { get; set; }
}

public sealed class Motor(Machine machine, MotorDef def) : MachineComponent<MotorDef, MotorSave>(machine, def)
{
    /// <summary>An idling engine burns this share of what it burns at full power.</summary>
    private const float IdleShare = 0.08f;
    /// <summary>Share of the engine's power that reaches the wheels.</summary>
    private const float Drivetrain = 0.85f;
    /// <summary>Force it takes to keep a machine rolling, as a share of its weight.</summary>
    private const float Rolling = 0.05f;
    private const float Gravity = 9.81f;
    private const float WattsPerHp = 745.7f;
    /// <summary>Fuel left (as a share of the tank) below which the farmer is warned.</summary>
    private const float LowFuel = 0.1f;
    /// <summary>Fuel is taken from the tank in lots this big: a tick's worth is too little for a full tank's precision.</summary>
    private const float BurnLot = 0.01f;

    private bool _warnedLow;
    /// <summary>Fuel burned but not taken from the tank yet.</summary>
    private float _unburned;

    /// <summary>The share of the engine's power it delivers: the working implements' and what it takes to move the chain.</summary>
    public float Load { get; internal set; }
    /// <summary>Fuel it burns now, per hour (0 with the engine off).</summary>
    public float FuelPerHour { get; private set; }

    public FillUnit? FuelTank => Machine.Unit(Def.FuelUnit);

    /// <summary>Its tank ran dry: the engine stops until it is refueled.</summary>
    public bool OutOfFuel => FuelTank is { IsEmpty: true };

    /// <summary>The engine runs while someone drives (the farmer or a helper) and there is fuel.</summary>
    public bool Running => Machine.Get<Drivable>()?.Controller != null && !OutOfFuel;

    public float MaxSpeed => Def.MaxSpeedKmh * MathUtil.KmhToMs;
    public float MaxReverse => Def.MaxReverseKmh * MathUtil.KmhToMs;

    /// <summary>
    /// Speeds the vehicle up, brakes or coasts toward what the driver asks, as fast as the implements working in its
    /// chain, the engine's power and the ground allow, and burns the fuel that takes. Without fuel it rolls to a stop.
    /// Returns its new speed (m/s).
    /// </summary>
    internal float Drive(Simulation sim, VehicleInput input, float dt)
    {
        var v = Machine;
        var world = sim.World;
        v.Status = null;
        if (OutOfFuel)
        {
            v.Status = "Out of fuel: refuel at a gas station";
            input = new VehicleInput { Steer = input.Steer, Brake = input.Brake };
        }
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

        // Power delivered: the implements', and at the wheels what keeps the chain rolling and speeds it up.
        var speedingUp = MathF.Max(0f, MathF.Abs(s) - MathF.Abs(v.Speed)) / dt;
        var wheels = totalMass * (Rolling * Gravity + speedingUp) * MathF.Abs(s) / Drivetrain / WattsPerHp;
        Load = Math.Clamp((demand + wheels) / Def.PowerHp, 0f, 1f);
        Burn(sim, dt);
        return s;
    }

    protected override MotorSave Capture(ContentDatabase content) => new() { Unburned = _unburned };

    protected override void Restore(MotorSave save, SaveContext context) => _unburned = Math.Clamp(save.Unburned, 0f, BurnLot);

    /// <summary>Burns fuel for the engine's load while it runs, and warns the farm when the tank runs low or dry.</summary>
    private void Burn(Simulation sim, float dt)
    {
        FuelPerHour = Running ? Def.FullPowerFuelPerHour * (IdleShare + (1f - IdleShare) * Load) : 0f;
        if (FuelPerHour <= 0f || FuelTank is not { } tank) return;
        _unburned += FuelPerHour * dt / 3600f;
        if (_unburned < BurnLot && _unburned < tank.Level) return;
        tank.Remove(_unburned);
        _unburned = 0f;
        if (Machine.FarmId != sim.Farms.Player.Id) return;
        if (tank.IsEmpty) sim.Notifications.Post($"{Machine.Def.Name} ran out of fuel", Severity.Warning, 10);
        else if (tank.Fraction < LowFuel && !_warnedLow) sim.Notifications.Post($"{Machine.Def.Name} is low on fuel", Severity.Warning);
        _warnedLow = tank.Fraction < LowFuel;
    }
}
