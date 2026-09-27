using System.Text.Json.Serialization;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>
/// What runs on each side of an axle: a tire (single, row-crop, flotation), two side by side (dual), or a track.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WheelSetDef
{
    /// <summary>Kinds of wheel sets; see <see cref="Type"/>.</summary>
    public static readonly string[] Types = ["single", "dual", "rowCrop", "flotation", "tracks"];

    /// <summary>
    /// single: one tire. dual: a second tire outside the first. rowCrop: a narrow tire at high pressure, to run between
    /// rows. flotation: a wide tire at low pressure. tracks: a rubber track around a wheel at each end.
    /// </summary>
    public string Type { get; set; } = "single";
    /// <summary>A tire's radius; a track's end wheels' radius.</summary>
    public float Radius { get; set; } = 0.5f;
    /// <summary>A tire's width (each of a dual's); a track's belt width.</summary>
    public float Width { get; set; } = 0.4f;
    /// <summary>Dual: between the inner and the outer tire.</summary>
    public float Gap { get; set; } = 0.05f;
    /// <summary>Tracks: the belt on the ground, between its end wheels' centers.</summary>
    public float Length { get; set; }

    [JsonIgnore]
    public bool IsTracks => Type == "tracks";

    /// <summary>A track's belt thickness: its end wheels' centers are this much higher than their radius.</summary>
    public const float TrackThickness = 0.06f;
}

/// <summary>An axle: where it is, how far apart its wheels are, how they steer, and the wheels on each side.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AxleDef
{
    /// <summary>How an axle's wheels steer; see <see cref="RunningGearDef"/>.</summary>
    public static readonly string[] SteeringTypes = ["fixed", "front", "rear", "allWheel", "self"];

    public float Z { get; set; }
    /// <summary>From the middle of the left wheels to the middle of the right ones.</summary>
    public float Track { get; set; } = 1.8f;
    /// <summary>One of <see cref="SteeringTypes"/>.</summary>
    public string Steering { get; set; } = "fixed";
    public WheelSetDef Wheels { get; set; } = new();
}

/// <summary>How the driver's steering moves the axles that steer.</summary>
public enum SteeringMode : byte
{
    /// <summary>Front and rear axles steer; all-wheel ones stay straight.</summary>
    Normal,
    /// <summary>All-wheel axles steer against the front ones: a tighter turn.</summary>
    AllWheel,
    /// <summary>Every steered axle turns the same way: the machine moves sideways without turning.</summary>
    Crab,
}

/// <summary>
/// Axles, and how the steered ones turn. The machine turns about a point on its length, the turning center: the middle
/// of the axles that hold it on its line (the fixed ones), else the middle of those that steer. Each steered axle turns
/// so that its wheels roll around that point, the farthest one at <see cref="MaxSteerDeg"/> at full lock: ahead of the
/// center into the turn, behind it the other way. A self-steering axle turns freely after the path, and locks straight
/// when reversing. Unknown properties are refused, so the wheel list from before axles doesn't load as no wheels.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunningGearDef : ComponentDef
{
    public AxleDef[] Axles { get; set; } = [];
    /// <summary>Angle of the steered wheels farthest from the turning center at full lock.</summary>
    public float MaxSteerDeg { get; set; } = 38f;
    /// <summary>How fast the steered wheels turn (degrees per second).</summary>
    public float SteerRateDeg { get; set; } = 90f;

    public float MaxSteer => MaxSteerDeg * MathUtil.Deg2Rad;

    /// <summary>The steering modes the driver switches between: with all-wheel axles, all-wheel and crab too.</summary>
    public SteeringMode[] Modes =>
        Axles.Any(a => a.Steering == "allWheel") ? [SteeringMode.Normal, SteeringMode.AllWheel, SteeringMode.Crab] : [SteeringMode.Normal];

    /// <summary>The driver steers the axle in this mode.</summary>
    public static bool Steered(AxleDef a, SteeringMode mode) =>
        a.Steering is "front" or "rear" || a.Steering == "allWheel" && mode != SteeringMode.Normal;

    /// <summary>The axle holds the machine on its line: it rolls straight, and the turning center is on its line.</summary>
    public static bool Holds(AxleDef a, SteeringMode mode, bool reverse) =>
        a.Steering == "fixed" || a.Steering == "allWheel" && mode == SteeringMode.Normal || a.Steering == "self" && reverse;

    /// <summary>Where along its length the machine turns about: the middle of the axles holding it, else of those steering.</summary>
    public float Pivot(SteeringMode mode, bool reverse)
    {
        float sum = 0f, n = 0f, min = float.MaxValue, max = float.MinValue;
        foreach (var a in Axles)
        {
            if (Holds(a, mode, reverse))
            {
                sum += a.Z;
                n++;
            }
            else if (Steered(a, mode))
            {
                min = MathF.Min(min, a.Z);
                max = MathF.Max(max, a.Z);
            }
        }
        return n > 0f ? sum / n : min <= max ? (min + max) * 0.5f : 0f;
    }

    /// <summary>From the turning center to the farthest steered axle; 0 when none steers.</summary>
    public float Wheelbase(SteeringMode mode = SteeringMode.Normal, bool reverse = false)
    {
        var pivot = Pivot(mode, reverse);
        var wheelbase = 0f;
        foreach (var a in Axles)
            if (Steered(a, mode))
                wheelbase = MathF.Max(wheelbase, MathF.Abs(a.Z - pivot));
        return wheelbase;
    }

    /// <summary>Radius of the tightest circle the turning center drives in normal steering.</summary>
    public float TurnRadius => Wheelbase() / MathF.Tan(MaxSteer);

    public override IEnumerable<string> Roles => Axles.SelectMany((a, i) => new[] { SideRole(a, i, "L"), SideRole(a, i, "R") });

    /// <summary>The model node role of one side of an axle: wheel0L (the tires) or track0L.</summary>
    public static string SideRole(AxleDef axle, int index, string side) => $"{(axle.Wheels.IsTracks ? "track" : "wheel")}{index}{side}";

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Axles.Length == 0) yield return "needs axles";
        foreach (var a in Axles.Where(a => !AxleDef.SteeringTypes.Contains(a.Steering)))
            yield return $"unknown steering '{a.Steering}' (known: {string.Join(", ", AxleDef.SteeringTypes)})";
        if (Axles.Any(a => a.Track <= 0f || a.Wheels.Radius <= 0f || a.Wheels.Width <= 0f)) yield return "axles need a track, and wheels a radius and width > 0";
        foreach (var w in Axles.Select(a => a.Wheels).Where(w => !WheelSetDef.Types.Contains(w.Type)))
            yield return $"unknown wheels type '{w.Type}' (known: {string.Join(", ", WheelSetDef.Types)})";
        if (Axles.Any(a => a.Wheels.Gap < 0f)) yield return "a dual's gap must be >= 0";
        if (Axles.Any(a => a.Wheels.IsTracks && a.Wheels.Length <= 0f)) yield return "tracks need a length > 0";
        if (Axles.Any(a => a.Wheels.IsTracks && a.Steering != "fixed")) yield return "tracks don't steer: they go on fixed axles";
        if (MaxSteerDeg is <= 0f or >= 80f || SteerRateDeg <= 0f) yield return "maxSteerDeg must be in (0, 80) and steerRateDeg > 0";
        if (Axles.Length == 0 || Axles.Any(a => !AxleDef.SteeringTypes.Contains(a.Steering))) yield break;

        var holds = Axles.Any(a => Holds(a, SteeringMode.Normal, false));
        if (!holds && !(Axles.Any(a => a.Steering == "front") && Axles.Any(a => a.Steering == "rear")))
            yield return "needs a fixed axle to turn about (or front and rear steered ones)";
        var pivot = Pivot(SteeringMode.Normal, false);
        if (MathF.Abs(pivot) > 0.01f) yield return $"the origin must be where it turns about: z = {pivot:0.##}, the middle of its fixed axles";
        if (Axles.Any(a => a.Steering == "front" && a.Z <= pivot) || Axles.Any(a => a.Steering == "rear" && a.Z >= pivot))
            yield return "front axles must be ahead of where it turns about, rear ones behind it";
        if (Axles.Any(a => a.Steering == "allWheel"))
        {
            if (Axles.Any(a => a.Steering == "fixed")) yield return "allWheel axles can't go with fixed ones, which crab steering would drag sideways";
            if (!Axles.Any(a => a.Steering is "front" or "rear")) yield return "allWheel axles need a front or rear axle to steer in normal steering";
        }
    }

    internal override MachineComponent Create(Machine machine) => new RunningGear(machine, this);
}

public sealed class RunningGearSave
{
    public float SteerAngle { get; set; }
    public float Distance { get; set; }
    /// <summary>The steering mode, when not normal: allWheel or crab.</summary>
    public string? Mode { get; set; }
}

public sealed class RunningGear(Machine machine, RunningGearDef def) : MachineComponent<RunningGearDef, RunningGearSave>(machine, def)
{
    /// <summary>Angles of the self-steering axles (the others follow the steering), radians, positive left.</summary>
    private readonly float[] _selfAngles = new float[def.Axles.Length];

    /// <summary>
    /// The steering lock in radians, positive turning left: the angle of the steered axle farthest from the turning
    /// center, turned into the turn (in crab steering, of every steered axle).
    /// </summary>
    public float SteerAngle { get; set; }
    /// <summary>Distance rolled, which turns the wheels.</summary>
    public float Distance { get; set; }
    public SteeringMode Mode { get; set; }

    /// <summary>From the turning center to the farthest steered axle, going forward or backward in the current mode.</summary>
    public float WheelbaseFor(bool reverse) => Def.Wheelbase(Mode, reverse);

    /// <summary>Turns the steered wheels toward <paramref name="angle"/> (radians), at their steering rate.</summary>
    internal void SteerToward(float angle, float dt) =>
        SteerAngle = MathUtil.MoveToward(SteerAngle, Math.Clamp(angle, -Def.MaxSteer, Def.MaxSteer), Def.SteerRateDeg * MathUtil.Deg2Rad * dt);

    /// <summary>
    /// How the machine moves while its origin goes <paramref name="speed"/> m/s forward for <paramref name="dt"/>: how
    /// far its heading turns (radians, positive left), and how far its origin goes sideways (meters, positive left)
    /// when the turning center is elsewhere (all-wheel, crab).
    /// </summary>
    internal (float turn, float sideways) Motion(float speed, float dt)
    {
        if (Mode == SteeringMode.Crab) return (0f, speed * MathF.Tan(SteerAngle) * dt);
        var reverse = speed < 0f;
        var wheelbase = WheelbaseFor(reverse);
        if (wheelbase <= 0f) return (0f, 0f);
        var turn = speed * MathF.Tan(SteerAngle) / wheelbase * dt;
        return (turn, -turn * Def.Pivot(Mode, reverse));
    }

    /// <summary>
    /// After the machine moved <paramref name="forward"/> and <paramref name="sideways"/> meters (in its own frame) and
    /// turned by <paramref name="turned"/> radians: the wheels roll, and self-steering axles turn after its path.
    /// </summary>
    internal void Roll(float forward, float sideways, float turned, float dt)
    {
        Distance += forward;
        var rate = Def.SteerRateDeg * MathUtil.Deg2Rad * dt;
        for (var i = 0; i < Def.Axles.Length; i++)
        {
            var a = Def.Axles[i];
            if (a.Steering != "self") continue;
            // Its wheels point where it goes; backing up they lock straight.
            var target = forward > 1e-4f ? Math.Clamp(MathF.Atan((sideways + turned * a.Z) / forward), -Def.MaxSteer, Def.MaxSteer) : 0f;
            _selfAngles[i] = MathUtil.MoveToward(_selfAngles[i], target, rate);
        }
    }

    /// <summary>Angle of an axle's wheels (the middle of the axle), radians, positive left.</summary>
    public float AxleAngle(int axle) => WheelAngle(axle, 0f);

    /// <summary>
    /// Angle of the wheel <paramref name="x"/> meters left of an axle's middle: the inner wheel of a turn turns more
    /// than the outer one, so that both roll around the turning center.
    /// </summary>
    public float WheelAngle(int axle, float x)
    {
        var a = Def.Axles[axle];
        if (a.Steering == "self") return _selfAngles[axle];
        if (!RunningGearDef.Steered(a, Mode)) return 0f;
        if (Mode == SteeringMode.Crab) return SteerAngle;
        var reverse = Machine.Speed < 0f;
        var wheelbase = WheelbaseFor(reverse);
        if (wheelbase <= 0f) return 0f;
        var curvature = MathF.Tan(SteerAngle) / wheelbase;
        return MathF.Atan(curvature * (a.Z - Def.Pivot(Mode, reverse)) / MathF.Max(0.2f, 1f - curvature * x));
    }

    protected override RunningGearSave Capture(ContentDatabase content) => new()
    {
        SteerAngle = SteerAngle, Distance = Distance, Mode = Mode == SteeringMode.Normal ? null : ModeName(Mode),
    };

    protected override void Restore(RunningGearSave save, SaveContext context)
    {
        SteerAngle = Math.Clamp(save.SteerAngle, -Def.MaxSteer, Def.MaxSteer);
        Distance = save.Distance;
        Mode = Def.Modes.FirstOrDefault(m => ModeName(m) == save.Mode);
    }

    /// <summary>A mode as data and saves name it: normal, allWheel, crab.</summary>
    public static string ModeName(SteeringMode mode) => mode switch
    {
        SteeringMode.AllWheel => "allWheel",
        SteeringMode.Crab => "crab",
        _ => "normal",
    };
}
