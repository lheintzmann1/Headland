using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class WheelDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 0.5f;
    public float Width { get; set; } = 0.4f;
    public bool Steer { get; set; }
}

/// <summary>
/// Wheels, and how the steered ones turn. A vehicle's fixed wheels sit on its origin's axle (z = 0), so the steered
/// ones set its wheelbase: ahead of it for a tractor, behind it for a combine steering with its rear axle.
/// </summary>
public sealed class RunningGearDef : ComponentDef
{
    public WheelDef[] Wheels { get; set; } = [];
    /// <summary>Angle of the steered wheels at full lock.</summary>
    public float MaxSteerDeg { get; set; } = 38f;
    /// <summary>How fast the steered wheels turn (degrees per second).</summary>
    public float SteerRateDeg { get; set; } = 90f;

    /// <summary>From the fixed axle to the steered one; 0 without steered wheels.</summary>
    public float Wheelbase => Wheels.Any(w => w.Steer) ? MathF.Abs(Wheels.Where(w => w.Steer).Average(w => w.Z)) : 0f;

    /// <summary>The steered wheels are behind the fixed ones.</summary>
    public bool SteersRear => Wheels.Any(w => w.Steer) && Wheels.Where(w => w.Steer).Average(w => w.Z) < 0f;

    public float MaxSteer => MaxSteerDeg * MathUtil.Deg2Rad;

    /// <summary>Radius of the tightest circle the fixed axle drives.</summary>
    public float TurnRadius => Wheelbase / MathF.Tan(MaxSteer);

    public override IEnumerable<string> Roles => Wheels.Select((_, i) => $"wheel{i}");

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Wheels.Any(w => w.Radius <= 0f || w.Width <= 0f)) yield return "wheels need radius and width > 0";
        if (MaxSteerDeg is <= 0f or >= 80f || SteerRateDeg <= 0f) yield return "maxSteerDeg must be in (0, 80) and steerRateDeg > 0";
        var steered = Wheels.Where(w => w.Steer).ToList();
        if (steered.Any(w => w.Z > 0f) && steered.Any(w => w.Z < 0f)) yield return "steered wheels must all be ahead of the fixed axle, or all behind it";
        if (steered.Count > 0 && Wheels.Where(w => !w.Steer).Any(w => MathF.Abs(w.Z) > 0.01f))
            yield return "fixed wheels must sit on the origin's axle (z = 0) when others steer";
    }

    internal override MachineComponent Create(Machine machine) => new RunningGear(machine, this);
}

public sealed class RunningGearSave
{
    public float SteerAngle { get; set; }
    public float Distance { get; set; }
}

public sealed class RunningGear(Machine machine, RunningGearDef def) : MachineComponent<RunningGearDef, RunningGearSave>(machine, def)
{
    /// <summary>Angle of the steered wheels in radians, positive turning left.</summary>
    public float SteerAngle { get; set; }
    /// <summary>Distance rolled, which turns the wheels.</summary>
    public float Distance { get; set; }

    public float Wheelbase { get; } = def.Wheelbase;

    /// <summary>Turns the steered wheels toward <paramref name="angle"/> (radians), at their steering rate.</summary>
    internal void SteerToward(float angle, float dt) =>
        SteerAngle = MathUtil.MoveToward(SteerAngle, Math.Clamp(angle, -Def.MaxSteer, Def.MaxSteer), Def.SteerRateDeg * MathUtil.Deg2Rad * dt);

    /// <summary>How much the heading turns while the fixed axle drives <paramref name="speed"/> m/s for <paramref name="dt"/>.</summary>
    internal float Turn(float speed, float dt) => speed * MathF.Tan(SteerAngle) / Wheelbase * dt;

    protected override RunningGearSave Capture(ContentDatabase content) => new() { SteerAngle = SteerAngle, Distance = Distance };

    protected override void Restore(RunningGearSave save, SaveContext context)
    {
        SteerAngle = Math.Clamp(save.SteerAngle, -Def.MaxSteer, Def.MaxSteer);
        Distance = save.Distance;
    }
}
