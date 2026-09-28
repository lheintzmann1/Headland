using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.World;

namespace Headland.Core.Machines.Components;

/// <summary>An engine: a machine with one drives itself (with a running gear that steers) rather than being pulled.</summary>
public sealed class MotorDef : MachineComponentDef
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

    internal override Component Create(Machine machine) => new Motor(machine, this);
}

public sealed class MotorSave
{
    public float Unburned { get; set; }
}

public sealed class Motor(Machine machine, MotorDef def) : MachineComponent<MotorDef, MotorSave>(machine, def), IConditionSource
{
    /// <summary>An idling engine burns this share of what it burns at full power.</summary>
    private const float IdleShare = 0.08f;
    /// <summary>Share of the engine's power that reaches the wheels.</summary>
    private const float Drivetrain = 0.85f;
    private const float Gravity = 9.81f;
    private const float WattsPerHp = 745.7f;
    /// <summary>Fuel left (as a share of the tank) below which the farmer is warned.</summary>
    private const float LowFuel = 0.1f;
    /// <summary>Fuel is taken from the tank in lots this big: a tick's worth is too little for a full tank's precision.</summary>
    private const float BurnLot = 0.01f;

    private bool _warnedLow;
    /// <summary>What the working implements asked for, when that was more than the engine has.</summary>
    private Underpowered? _underpowered;
    /// <summary>Fuel burned but not taken from the tank yet.</summary>
    private float _unburned;
    /// <summary>What holds the chain back going forward and backward (N), and the power the implements take from the engine (hp).</summary>
    private float _resistingForward, _resistingBackward, _taken;
    /// <summary>How much the driven wheels slip going forward and backward.</summary>
    private float _slipForward, _slipBackward;
    /// <summary>The power it delivers now (hp).</summary>
    private float _delivered;

    /// <summary>
    /// How fast it can go now, forward and backward (m/s): its top speeds, held down by the implements' work speed,
    /// the power left to move the chain on this ground and slope, and the wheels' slip.
    /// </summary>
    public float TopSpeed { get; private set; }
    public float TopReverse { get; private set; }

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

    /// <summary>The power it has: its own, less what wear took (<see cref="Wearable"/>).</summary>
    public float PowerHp => Def.PowerHp * Machine.PowerFactor();

    public IEnumerable<MachineCondition> Conditions
    {
        get
        {
            if (OutOfFuel) yield return new OutOfFuel();
            if (_underpowered != null) yield return _underpowered;
        }
    }

    /// <summary>
    /// Before the driver asks: reads the ground under the chain and the implements working in it, and works out how fast
    /// it can go (<see cref="TopSpeed"/>, <see cref="TopReverse"/>).
    /// </summary>
    internal void Prepare(Simulation sim)
    {
        var v = Machine;
        var maxF = MaxSpeed;
        var maxR = MaxReverse;
        float demand = 0f, draft = 0f, rolling = 0f, climbing = 0f;
        _taken = 0f;
        foreach (var m in v.Chain())
        {
            if (m.Get<RunningGear>() is { } gear)
            {
                var weight = OnWheels(m, sim.Content) * Gravity;
                gear.Touch(sim, weight);
                rolling += gear.Rolling * weight;
                climbing += gear.Grade * weight;
            }
            if (m.Get<WorkAreas>() is not { } w) continue;
            foreach (var area in w.Def.Areas)
            {
                if (!w.Working(area)) continue;
                maxF = MathF.Min(maxF, w.MaxSpeedKmh(area) * MathUtil.KmhToMs);
                demand += area.RequiredPowerHp;
                // Some work takes its power from the engine (a header threshing, a mower's or spreader's discs); the
                // rest is pulled through the ground, by as much force as takes that power at its work speed.
                if (!area.Work.Draft) _taken += area.RequiredPowerHp;
                else draft += area.RequiredPowerHp * WattsPerHp * Drivetrain / (area.MaxWorkSpeedKmh * MathUtil.KmhToMs);
            }
        }
        var power = PowerHp;
        _underpowered = demand > power ? new Underpowered(demand, power) : null;
        if (_underpowered != null) maxF *= MathF.Max(0.3f, power / demand);

        // What holds the chain back (going backward, a slope pulls the other way and nothing is drawn), as fast as the
        // power left for the wheels moves that, less what the wheels slip.
        _resistingForward = rolling + climbing + draft;
        _resistingBackward = rolling - climbing;
        var driven = v.Get<RunningGear>()!;
        var weightDriven = OnWheels(v, sim.Content) * Gravity;
        _slipForward = driven.SlipFor(_resistingForward, weightDriven);
        _slipBackward = driven.SlipFor(_resistingBackward, weightDriven);
        var wheelPower = MathF.Max(0.1f, 1f - _taken / power) * power * WattsPerHp * Drivetrain;
        if (_resistingForward > 0f) maxF = MathF.Min(maxF, wheelPower / _resistingForward);
        if (_resistingBackward > 0f) maxR = MathF.Min(maxR, wheelPower / _resistingBackward);
        TopSpeed = maxF * (1f - _slipForward);
        TopReverse = maxR * (1f - _slipBackward);
    }

    /// <summary>
    /// Speeds the vehicle up, brakes or coasts toward what the driver asks, up to its top speeds, and burns the fuel
    /// that takes. Without fuel it rolls to a stop. Returns its new speed (m/s).
    /// </summary>
    internal float Drive(Simulation sim, VehicleInput input, float dt)
    {
        var v = Machine;
        if (OutOfFuel) input = new VehicleInput { Steer = input.Steer, Brake = input.Brake };
        var totalMass = 0f;
        foreach (var m in v.Chain()) totalMass += m.SelfMassWithLoad(sim.Content);
        var accel = Def.Acceleration * Math.Clamp(v.SelfMassWithLoad(sim.Content) / MathF.Max(1f, totalMass), 0.25f, 1f);

        var s = v.Speed;
        if (input.Brake) s = MathUtil.MoveToward(s, 0f, Def.Braking * dt);
        else if (input.Throttle > 0.01f)
        {
            var target = TopSpeed * input.Throttle;
            s = s < -0.01f ? MathUtil.MoveToward(s, 0f, Def.Braking * dt)
                : s < target ? MathF.Min(target, s + accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else if (input.Throttle < -0.01f)
        {
            var target = -TopReverse * -input.Throttle;
            s = s > 0.01f ? MathUtil.MoveToward(s, 0f, Def.Braking * dt)
                : s > target ? MathF.Max(target, s - accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else s = MathUtil.MoveToward(s, 0f, 1.5f * dt);
        if (s > TopSpeed) s = MathUtil.MoveToward(s, TopSpeed, Def.Braking * dt);

        // Power delivered: what the implements take, and at the wheels (spinning faster than the ground goes by as they slip) what
        // moves the chain against what holds it back and speeds it up.
        var (resisting, slip) = s < 0f ? (_resistingBackward, _slipBackward) : (_resistingForward, _slipForward);
        v.Get<RunningGear>()!.Slip = s == 0f ? 0f : slip;
        var speedingUp = MathF.Max(0f, MathF.Abs(s) - MathF.Abs(v.Speed)) / dt;
        var wheels = MathF.Max(0f, resisting + totalMass * speedingUp) * MathF.Abs(s) / (1f - slip) / Drivetrain / WattsPerHp;
        _delivered = MathF.Min(_taken + wheels, PowerHp);
        Load = _delivered / PowerHp;
        Burn(sim, dt);
        return s;
    }

    /// <summary>
    /// The mass a machine's wheels carry: its own and what it carries, less the share of it resting on the hitch it hangs
    /// on, plus the share of the trailers hitched to it resting on their hitches (a semi-trailer on a fifth wheel).
    /// </summary>
    private static float OnWheels(Machine m, ContentDatabase content)
    {
        var mass = Carried(m, content) * (1f - OnHitch(m));
        foreach (var child in m.Attached.Values)
            if (child.Get<Attachable>() is { Def.Mode: "trailed" } a)
                mass += Carried(child, content) * a.Def.HitchLoad;
        return mass;
    }

    /// <summary>Its own mass with its load, and that of the implements mounted on it (or on those) without wheels of their own.</summary>
    private static float Carried(Machine m, ContentDatabase content)
    {
        var mass = m.SelfMassWithLoad(content);
        foreach (var child in m.Attached.Values)
            if (child.Get<Attachable>()?.Def.Mode == "mounted" && !child.Has<RunningGear>())
                mass += Carried(child, content);
        return mass;
    }

    /// <summary>The share of a hitched trailer's weight resting on its hitch.</summary>
    private static float OnHitch(Machine m) => m.Parent != null && m.Get<Attachable>() is { Def.Mode: "trailed" } a ? a.Def.HitchLoad : 0f;

    protected override MotorSave Capture(ContentDatabase content) => new() { Unburned = _unburned };

    protected override void Restore(MotorSave save, SaveContext context) => _unburned = Math.Clamp(save.Unburned, 0f, BurnLot);

    /// <summary>
    /// Burns fuel for the power it delivers while it runs, more when worn, and warns the farm when the tank runs low or
    /// dry.
    /// </summary>
    private void Burn(Simulation sim, float dt)
    {
        var share = IdleShare + (1f - IdleShare) * _delivered / Def.PowerHp;
        FuelPerHour = Running ? Def.FullPowerFuelPerHour * Machine.UsageFactor() * share : 0f;
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
