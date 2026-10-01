using Headland.Core.Components;
using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.World;

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

    /// <summary>
    /// The ground under one side (m²): each tire's width by the length it is pressed flat over, longer at low pressure
    /// (flotation) and shorter at high pressure (row-crop); a track's belt.
    /// </summary>
    [JsonIgnore]
    public float ContactArea => Type switch
    {
        "tracks" => Width * Length,
        "dual" => 2f * Width * Radius * 0.65f,
        "rowCrop" => Width * Radius * 0.5f,
        "flotation" => Width * Radius * 0.85f,
        _ => Width * Radius * 0.65f,
    };

    /// <summary>How well it grips next to a single tire: the more rubber on the ground, the better.</summary>
    [JsonIgnore]
    public float Grip => Type switch
    {
        "tracks" => 1.3f,
        "dual" or "flotation" => 1.1f,
        "rowCrop" => 0.95f,
        _ => 1f,
    };

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

/// <summary>An articulated machine's hinge: its front frame, with the axles ahead of the hinge, swings about it to steer.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ArticulationDef
{
    public float Z { get; set; }
}

/// <summary>How a running gear steers, from what it is built of.</summary>
public enum SteeringKind : byte
{
    /// <summary>It doesn't: a trailer, which its hitch pulls round.</summary>
    None,
    /// <summary>Steered axles, turning about the fixed ones (or about the middle of the steered ones).</summary>
    Axles,
    /// <summary>A hinge between a front and a rear frame, each with fixed axles.</summary>
    Articulated,
    /// <summary>Tracks and no steered axle: one track runs faster than the other, and it can turn on the spot.</summary>
    SkidSteer,
}

/// <summary>
/// Axles, and how the machine steers. The machine turns about a point on its length, the turning center: the middle
/// of the axles that hold it on its line (the fixed ones), else the middle of those that steer. Each steered axle turns
/// so that its wheels roll around that point, the farthest one at <see cref="MaxSteerDeg"/> at full lock: ahead of the
/// center into the turn, behind it the other way. A self-steering axle turns freely after the path, and locks straight
/// when reversing. An articulated machine steers by swinging its front frame about a hinge instead, and a tracked one
/// without steered axles by running one track faster than the other. Unknown properties are refused, so the wheel list
/// from before axles doesn't load as no wheels.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunningGearDef : MachineComponentDef
{
    public AxleDef[] Axles { get; set; } = [];
    /// <summary>Full lock: the angle of the steered axle farthest from the turning center, or of the hinge.</summary>
    public float MaxSteerDeg { get; set; } = 38f;
    /// <summary>How fast the steering turns (degrees per second).</summary>
    public float SteerRateDeg { get; set; } = 90f;
    /// <summary>Articulated steering: the hinge.</summary>
    public ArticulationDef? Articulation { get; set; }
    /// <summary>Skid steer: how fast it turns on the spot at full lock (degrees per second).</summary>
    public float SpinRateDeg { get; set; } = 30f;

    public float MaxSteer => MaxSteerDeg * MathUtil.Deg2Rad;

    [JsonIgnore]
    public SteeringKind SteeringKind =>
        Articulation != null ? SteeringKind.Articulated
        : Axles.Any(a => Steered(a, SteeringMode.Normal)) ? SteeringKind.Axles
        : Axles.Any(a => a.Wheels.IsTracks) ? SteeringKind.SkidSteer
        : SteeringKind.None;

    /// <summary>The steering modes the driver switches between: with all-wheel axles, all-wheel and crab too.</summary>
    public SteeringMode[] Modes =>
        Axles.Any(a => a.Steering == "allWheel") ? [SteeringMode.Normal, SteeringMode.AllWheel, SteeringMode.Crab] : [SteeringMode.Normal];

    /// <summary>The driver steers the axle in this mode.</summary>
    public static bool Steered(AxleDef a, SteeringMode mode) =>
        a.Steering is "front" or "rear" || a.Steering == "allWheel" && mode != SteeringMode.Normal;

    /// <summary>The axle holds the machine on its line: it rolls straight, and the turning center is on its line.</summary>
    public static bool Holds(AxleDef a, SteeringMode mode, bool reverse) =>
        a.Steering == "fixed" || a.Steering == "allWheel" && mode == SteeringMode.Normal || a.Steering == "self" && reverse;

    /// <summary>The axle is on an articulated machine's front frame.</summary>
    public bool OnFrontFrame(float z) => Articulation != null && z > Articulation.Z;

    /// <summary>
    /// Where along its length the machine turns about: the middle of the axles holding it (on an articulated machine,
    /// of those of its rear frame), else of those steering.
    /// </summary>
    public float Pivot(SteeringMode mode, bool reverse)
    {
        float sum = 0f, n = 0f, min = float.MaxValue, max = float.MinValue;
        foreach (var a in Axles)
        {
            if (OnFrontFrame(a.Z)) continue;
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

    /// <summary>Articulated: from the rear frame's axles to the hinge, and from the hinge to the front frame's axles.</summary>
    public (float rear, float front) Frames
    {
        get
        {
            if (Articulation is not { } h) return (0f, 0f);
            var front = Axles.Where(a => OnFrontFrame(a.Z)).Select(a => a.Z).DefaultIfEmpty(h.Z).Average();
            return (h.Z - Pivot(SteeringMode.Normal, false), front - h.Z);
        }
    }

    /// <summary>The ground under all its wheels and tracks (m²).</summary>
    [JsonIgnore]
    public float ContactArea => Axles.Sum(a => 2f * a.Wheels.ContactArea);

    /// <summary>How well its wheels and tracks grip, next to single tires.</summary>
    [JsonIgnore]
    public float Grip => Axles.Length == 0 ? 1f : Axles.Average(a => a.Wheels.Grip);

    /// <summary>Skid steer turns like a wheelbase as long as its tracks.</summary>
    public float TrackLength => Axles.Where(a => a.Wheels.IsTracks).Select(a => a.Wheels.Length).DefaultIfEmpty(0f).Max();

    /// <summary>Radius of the tightest circle the turning center drives in normal steering (while moving).</summary>
    public float TurnRadius => SteeringKind switch
    {
        SteeringKind.Articulated => Frames is var (rear, front) ? (rear * MathF.Cos(MaxSteer) + front) / MathF.Sin(MaxSteer) : 0f,
        SteeringKind.SkidSteer => TrackLength / MathF.Tan(MaxSteer),
        _ => Wheelbase() / MathF.Tan(MaxSteer),
    };

    public override IEnumerable<string> Roles =>
        Axles.SelectMany((a, i) => new[] { SideRole(a, i, "L"), SideRole(a, i, "R") }).Concat(Articulation != null ? ["frontFrame"] : []);

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
        if (MaxSteerDeg is <= 0f or >= 80f || SteerRateDeg <= 0f || SpinRateDeg <= 0f)
            yield return "maxSteerDeg must be in (0, 80), steerRateDeg and spinRateDeg > 0";
        if (Axles.Length == 0 || Axles.Any(a => !AxleDef.SteeringTypes.Contains(a.Steering))) yield break;

        if (Articulation is { } hinge)
        {
            if (Axles.Any(a => a.Steering != "fixed")) yield return "an articulated machine steers with its hinge: its axles are fixed";
            if (!Axles.Any(a => a.Z < hinge.Z) || !Axles.Any(a => a.Z > hinge.Z)) yield return "the hinge needs axles on both sides of it";
            else if (MathF.Abs(Pivot(SteeringMode.Normal, false)) > 0.01f)
                yield return $"the origin must be where it turns about: z = {Pivot(SteeringMode.Normal, false):0.##}, the middle of its rear frame's axles";
            yield break;
        }
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

    internal override Component Create(Machine machine) => new RunningGear(machine, this);
}

public sealed class RunningGearSave
{
    public float SteerAngle { get; set; }
    public float Distance { get; set; }
    public float Turned { get; set; }
    /// <summary>The steering mode, when not normal: allWheel or crab.</summary>
    public string? Mode { get; set; }
}

public sealed class RunningGear(Machine machine, RunningGearDef def) : MachineComponent<RunningGearDef, RunningGearSave>(machine, def), IActionSource, IReadoutSource
{
    /// <summary>Skid steer turns on the spot up to this speed (m/s), less the faster it goes.</summary>
    private const float SpinSpeed = 1f;
    /// <summary>Soft ground holds back this much more of a machine's weight per 100 kPa of ground pressure.</summary>
    private const float Sinkage = 0.08f;

    /// <summary>Angles of the self-steering axles (the others follow the steering), radians, positive left.</summary>
    private readonly float[] _selfAngles = new float[def.Axles.Length];
    private readonly (float rear, float front) _frames = def.Frames;
    private readonly float _trackLength = def.TrackLength;
    private readonly float _contactArea = MathF.Max(0.01f, def.ContactArea);
    private readonly float _gripFactor = def.Grip;
    private bool _braking;
    private float _grip;

    /// <summary>
    /// The steering in radians, positive turning left: the angle of the steered axle farthest from the turning center,
    /// turned into the turn (in crab steering, of every steered axle); of an articulated machine's hinge, its front
    /// frame turned left; skid steer turns as a steered axle a track's length ahead would.
    /// </summary>
    public float SteerAngle { get; set; }
    /// <summary>Distance the turning center rolled, which turns the wheels.</summary>
    public float Distance { get; set; }
    /// <summary>How far the machine has turned while rolling (radians, positive left): the wheels on the outside roll further.</summary>
    public float Turned { get; set; }
    public SteeringMode Mode { get; set; }
    public SteeringKind SteeringKind { get; } = def.SteeringKind;

    /// <summary>The steering key, on a vehicle that steers more than one way (normal, all-wheel, crab).</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Machine.Parent != null || Def.Modes.Length < 2) return;
        actions.Add(InputActions.Steering, $"Steering: {MachineSystem.SteeringName(Mode)}", () =>
        {
            if (Machine.Get<Drivable>()?.Controller is FieldWorkController)
            {
                sim.Notifications.Post("The helper steers: dismiss them first");
                return;
            }
            var modes = Def.Modes;
            Mode = modes[(Array.IndexOf(modes, Mode) + 1) % modes.Length];
            sim.Notifications.Post($"Steering: {MachineSystem.SteeringName(Mode)}");
        });
    }

    /// <summary>Its weight over the ground under its wheels and tracks (kPa): lower on duals, flotation tires and tracks.</summary>
    public float Pressure { get; private set; }
    /// <summary>The share of its weight it takes to keep it rolling on the ground under it: more when soft, and deeper in it.</summary>
    public float Rolling { get; private set; }
    /// <summary>The ground's rise for each meter forward (negative downhill).</summary>
    public float Grade { get; private set; }
    /// <summary>The share of its driven wheels' turning lost to slipping (0..1).</summary>
    public float Slip { get; internal set; }

    /// <summary>The driven wheels slipping, and a steering mode other than the normal one.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (Slip > 0.05f) yield return new Status($"Slip {Slip * 100f:0}%", Slip > 0.15f ? Tone.Warning : Tone.Dim);
        if (Mode != SteeringMode.Normal)
        {
            var name = MachineSystem.SteeringName(Mode);
            yield return new Status($"{char.ToUpperInvariant(name[0])}{name[1..]} steering", Tone.Info);
        }
    }

    /// <summary>From the turning center to the farthest steered axle, going forward or backward in the current mode.</summary>
    public float WheelbaseFor(bool reverse) => Def.Wheelbase(Mode, reverse);

    /// <summary>
    /// Reads the ground under it, carrying <paramref name="weight"/> newtons: how steep, how hard to roll on and how
    /// well it grips. Roads are firm; the rest softens with the soil's moisture and the rain on it, and gives less grip.
    /// </summary>
    internal void Touch(Simulation sim, float weight)
    {
        var world = sim.World;
        var (pos, forward) = (Machine.Position, Machine.Forward);
        Grade = (world.SurfaceAt(pos + forward) - world.SurfaceAt(pos - forward)) * 0.5f;
        Pressure = weight / _contactArea / 1000f;
        var (cx, cz) = world.WorldToCell(pos);
        var (ground, moisture) = world.InBounds(cx, cz) && world.CellIndex(cx, cz) is var i
            ? ((GroundType)world.Layers.Ground[i], world.Layers.Moisture[i] / 255f)
            : (GroundType.Grass, 0.5f);
        var wet = sim.Weather.GroundWetness;
        if (WorldMap.IsSealed(ground))
        {
            Rolling = 0.02f;
            _grip = 0.8f * (1f - 0.25f * wet) * _gripFactor;
            return;
        }
        var soft = MathF.Max(MathUtil.Saturate((moisture - 0.55f) / 0.35f), 0.5f * wet);
        var (rolling, grip) = ground switch
        {
            GroundType.Grass => (0.05f, 0.6f),
            GroundType.Stubble => (0.06f, 0.7f),
            GroundType.Seeded => (0.07f, 0.6f),
            GroundType.Cultivated => (0.08f, 0.6f),
            GroundType.Plowed => (0.1f, 0.55f),
            GroundType.Dirt => (0.04f, 0.65f),
            _ => (0.06f, 0.6f),
        };
        Rolling = rolling + Sinkage * soft * Pressure / 100f;
        _grip = grip * (1f - 0.45f * soft) * _gripFactor;
    }

    /// <summary>
    /// How much its driven wheels would slip pulling <paramref name="pull"/> newtons with <paramref name="weight"/> on
    /// them, on the ground it last touched: a little as long as the ground holds, then more and more as the pull nears
    /// what it grips.
    /// </summary>
    internal float SlipFor(float pull, float weight)
    {
        var ratio = MathF.Max(0f, pull) / MathF.Max(1f, _grip * weight);
        return ratio <= 1f ? 0.02f * ratio + 0.13f * ratio * ratio : MathF.Min(0.9f, 0.15f + 0.75f * (ratio - 1f));
    }

    /// <summary>Distance rolled by the wheels or track <paramref name="x"/> meters left of the turning center.</summary>
    public float SideDistance(float x) => Distance - Turned * x;

    /// <summary>How far a part at <paramref name="z"/> turns with the front frame of an articulated machine (radians, positive left).</summary>
    public float FrameAngle(float z) => Def.OnFrontFrame(z) ? SteerAngle : 0f;

    /// <summary>
    /// Where a point of the machine's space is once an articulated machine's front frame swung (itself behind the hinge),
    /// and how far it turned.
    /// </summary>
    public (float x, float z, float angle) Swing(float x, float z)
    {
        var angle = FrameAngle(z);
        if (angle == 0f) return (x, z, 0f);
        var hinge = Def.Articulation!.Z;
        var (sin, cos) = MathF.SinCos(angle);
        var dz = z - hinge;
        return (x * cos + dz * sin, hinge - x * sin + dz * cos, angle);
    }

    /// <summary>Turns the steering toward the driver's, with less lock the faster it goes.</summary>
    internal void Steer(VehicleInput input, float speed, float dt)
    {
        _braking = input.Brake;
        var speedFactor = MathUtil.Lerp(1f, 0.4f, MathUtil.Saturate(MathF.Abs(speed) / (40f * MathUtil.KmhToMs)));
        SteerToward(Math.Clamp(input.Steer, -1f, 1f) * Def.MaxSteer * speedFactor, dt);
    }

    /// <summary>Turns the steering toward <paramref name="angle"/> (radians), at its steering rate.</summary>
    internal void SteerToward(float angle, float dt) =>
        SteerAngle = MathUtil.MoveToward(SteerAngle, Math.Clamp(angle, -Def.MaxSteer, Def.MaxSteer), Def.SteerRateDeg * MathUtil.Deg2Rad * dt);

    /// <summary>The steering that makes the turning center drive a circle of <paramref name="curvature"/> (1/radius, positive left).</summary>
    public float SteerFor(float curvature, bool reverse)
    {
        switch (SteeringKind)
        {
            case SteeringKind.Articulated:
                // sin φ = κ (rear cos φ + front): a sine and a cosine make one shifted sine.
                var (rear, front) = _frames;
                var amplitude = MathF.Sqrt(1f + curvature * curvature * rear * rear);
                return MathF.Atan(curvature * rear) + MathF.Asin(Math.Clamp(curvature * front / amplitude, -1f, 1f));
            case SteeringKind.SkidSteer:
                return MathF.Atan(curvature * _trackLength);
            default:
                return MathF.Atan(curvature * WheelbaseFor(reverse));
        }
    }

    /// <summary>
    /// How the machine moves while its origin goes <paramref name="speed"/> m/s forward for <paramref name="dt"/>: how
    /// far its heading turns (radians, positive left), and how far its origin goes sideways (meters, positive left)
    /// when the turning center is elsewhere (all-wheel, crab).
    /// </summary>
    internal (float turn, float sideways) Motion(float speed, float dt)
    {
        switch (SteeringKind)
        {
            case SteeringKind.Articulated:
            {
                // The rear axle rolls toward the hinge while the front one, swung, rolls round it.
                var (rear, front) = _frames;
                return (speed * MathF.Sin(SteerAngle) / (rear * MathF.Cos(SteerAngle) + front) * dt, 0f);
            }
            case SteeringKind.SkidSteer:
            {
                var turn = speed * MathF.Tan(SteerAngle) / _trackLength * dt;
                // Slow enough, and not braking, the tracks run against each other.
                if (!_braking && MathF.Abs(speed) < SpinSpeed)
                    turn += SteerAngle / Def.MaxSteer * Def.SpinRateDeg * MathUtil.Deg2Rad * (1f - MathF.Abs(speed) / SpinSpeed) * dt;
                return (turn, 0f);
            }
        }
        if (Mode == SteeringMode.Crab) return (0f, speed * MathF.Tan(SteerAngle) * dt);
        var reverse = speed < 0f;
        var wheelbase = WheelbaseFor(reverse);
        if (wheelbase <= 0f) return (0f, 0f);
        var turned = speed * MathF.Tan(SteerAngle) / wheelbase * dt;
        return (turned, -turned * Def.Pivot(Mode, reverse));
    }

    /// <summary>
    /// After the machine moved <paramref name="forward"/> and <paramref name="sideways"/> meters (in its own frame) and
    /// turned by <paramref name="turned"/> radians: the wheels roll, and self-steering axles turn after its path.
    /// </summary>
    internal void Roll(float forward, float sideways, float turned, float dt)
    {
        // Slipping, the wheels turn further than the ground goes.
        Distance += forward / (1f - Slip);
        Turned += turned / (1f - Slip);
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
        SteerAngle = SteerAngle, Distance = Distance, Turned = Turned, Mode = Mode == SteeringMode.Normal ? null : ModeName(Mode),
    };

    protected override void Restore(RunningGearSave save, SaveContext context)
    {
        SteerAngle = Math.Clamp(save.SteerAngle, -Def.MaxSteer, Def.MaxSteer);
        Distance = save.Distance;
        Turned = save.Turned;
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
