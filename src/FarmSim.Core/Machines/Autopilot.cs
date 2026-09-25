using System.Numerics;
using FarmSim.Core.World;

namespace FarmSim.Core.Machines;

/// <summary>
/// Pure-pursuit waypoint follower. The lookahead shrinks with speed for tight tracking, and it never looks past
/// the end of a "hard end" segment (a working lane), so the vehicle does not start turning before the lane ends.
/// </summary>
public sealed class WaypointController : IVehicleController
{
    private Vector2? _start;

    public WaypointController(IReadOnlyList<Vector2> waypoints, float speedKmh, IReadOnlyList<bool>? hardEnds = null)
    {
        Waypoints = waypoints;
        SpeedKmh = speedKmh;
        HardEnds = hardEnds;
    }

    public IReadOnlyList<Vector2> Waypoints { get; }
    /// <summary>Per waypoint: true if the segment arriving at it must be driven to its end before turning.</summary>
    public IReadOnlyList<bool>? HardEnds { get; }
    /// <summary>Index of the waypoint being driven to (the current segment ends there).</summary>
    public int Index { get; private set; }
    public float SpeedKmh { get; set; }
    public float MinLookahead { get; set; } = 2.2f;
    public float MaxLookahead { get; set; } = 4.5f;
    public float ArriveRadius { get; set; } = 1.0f;
    public bool Finished => Index >= Waypoints.Count;

    /// <summary>Direction of the segment currently driven (zero when finished).</summary>
    public Vector2 SegmentDirection
    {
        get
        {
            if (Finished) return Vector2.Zero;
            var d = Waypoints[Index] - SegmentStart;
            return d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.Zero;
        }
    }

    public VehicleInput GetInput(Machine v, float dt)
    {
        _start ??= v.Position;
        AdvancePastReachedWaypoints(v.Position);
        if (Finished) return new VehicleInput { Brake = true };

        var lookahead = Math.Clamp(1.6f + 0.55f * MathF.Abs(v.Speed), MinLookahead, MaxLookahead);
        var target = LookaheadPoint(v.Position, lookahead);
        var local = MathUtil.WorldToLocal(v.Position, v.Heading, target);
        var d2 = MathF.Max(local.LengthSquared(), 0.01f);
        var curvature = 2f * local.X / d2;
        var mot = v.Def.Motorized!;
        var steer = MathF.Atan(curvature * mot.Wheelbase) / (mot.MaxSteerDeg * MathUtil.Deg2Rad);

        // Slow down for sharp turns.
        var targetSpeed = SpeedKmh * MathUtil.KmhToMs * MathUtil.Lerp(1f, 0.45f, MathUtil.Saturate(MathF.Abs(steer)));
        var input = new VehicleInput { Steer = Math.Clamp(steer, -1f, 1f) };
        if (v.Speed > targetSpeed + 0.5f) input.Brake = true;
        else input.Throttle = Math.Clamp((targetSpeed - v.Speed) * 2f + targetSpeed / (mot.MaxSpeedKmh * MathUtil.KmhToMs), 0.05f, 1f);
        return input;
    }

    private Vector2 SegmentStart => Index == 0 ? _start!.Value : Waypoints[Index - 1];

    private bool IsHardEnd(int k) => HardEnds != null && k < HardEnds.Count && HardEnds[k];

    /// <summary>A waypoint is reached once the vehicle's projection passes the end of its segment.</summary>
    private void AdvancePastReachedWaypoints(Vector2 pos)
    {
        while (!Finished)
        {
            var a = SegmentStart;
            var b = Waypoints[Index];
            var seg = b - a;
            var len2 = seg.LengthSquared();
            var t = len2 < 1e-6f ? 1f : Vector2.Dot(pos - a, seg) / len2;
            // Hard-end segments are finished by passing their end, never by cutting the corner.
            if (t < 1f && (IsHardEnd(Index) || Vector2.Distance(pos, b) >= ArriveRadius)) return;
            Index++;
        }
    }

    /// <summary>Projects the vehicle onto the current segment, then walks the lookahead distance along the path.</summary>
    private Vector2 LookaheadPoint(Vector2 pos, float lookahead)
    {
        var a = SegmentStart;
        var seg = Waypoints[Index] - a;
        var len2 = seg.LengthSquared();
        var t = len2 < 1e-6f ? 1f : Math.Clamp(Vector2.Dot(pos - a, seg) / len2, 0f, 1f);
        var remaining = lookahead;
        var from = a + seg * t;
        for (var k = Index; k < Waypoints.Count; k++)
        {
            var step = Waypoints[k] - from;
            var len = step.Length();
            if (len >= remaining) return from + step / len * remaining;
            // Stay on a working lane until its end: extend straight ahead instead of peeking into the turn.
            if (IsHardEnd(k))
            {
                var dir = Waypoints[k] - (k == 0 ? _start!.Value : Waypoints[k - 1]);
                if (dir.LengthSquared() > 1e-6f) return Waypoints[k] + Vector2.Normalize(dir) * (remaining - len);
            }
            remaining -= len;
            from = Waypoints[k];
        }
        return Waypoints[^1];
    }
}

/// <summary>A planned field route: waypoints, and whether the segment arriving at each one is a working lane.</summary>
public sealed record FieldPath(List<Vector2> Points, List<bool> Work)
{
    public int LaneCount => Work.Count(w => w);
}

public static class FieldPlanner
{
    /// <summary>
    /// Back-and-forth lanes along the field's longer side, starting at the corner nearest <paramref name="startNear"/>.
    /// Lanes are visited in interleaved sets so most U-turns are plain semicircles wider than the turning circle;
    /// when two consecutive lanes are closer than that, an omega loop is used instead.
    /// <paramref name="margin"/> is how far past the field edge the vehicle drives before turning: it should cover
    /// the implement's distance behind the vehicle so the work area clears the edge.
    /// </summary>
    public static FieldPath Lanes(FieldInfo f, float workWidth, float minTurnRadius, float margin,
        Vector2? startNear = null, int? maxLanes = null)
    {
        // Plan in (u, v): u across the lanes, v along them.
        var alongZ = f.H >= f.W;
        var across = alongZ ? f.W : f.H;
        var along = alongZ ? f.H : f.W;
        var u0 = alongZ ? f.X : f.Z;
        var v0 = alongZ ? f.Z : f.X;
        Vector2 ToWorld(float u, float v) => alongZ ? new Vector2(u, v) : new Vector2(v, u);

        var laneCount = Math.Max(1, (int)MathF.Ceiling(across / workWidth - 0.01f));
        var skip = Math.Clamp((int)MathF.Ceiling(2f * minTurnRadius / workWidth), 1, laneCount);
        var order = new List<int>();
        for (var s = 0; s < skip; s++)
        {
            var set = Enumerable.Range(0, laneCount).Where(l => l % skip == s).ToList();
            if (s % 2 == 1) set.Reverse();
            order.AddRange(set);
        }

        var mirror = false;
        var startFar = false;
        if (startNear is { } p)
        {
            var pu = alongZ ? p.X : p.Y;
            var pv = alongZ ? p.Y : p.X;
            mirror = pu > u0 + across * 0.5f;
            startFar = pv > v0 + along * 0.5f;
        }
        if (mirror) order = order.Select(l => laneCount - 1 - l).ToList();
        if (maxLanes is { } max) order = order.Take(max).ToList();

        float LaneU(int l) => MathF.Min(u0 + (l + 0.5f) * workWidth, u0 + across - workWidth * 0.5f);
        var vNear = v0 - margin;
        var vFar = v0 + along + margin;

        var pts = new List<Vector2>();
        var work = new List<bool>();
        var forward = !startFar; // forward = driving toward increasing v
        var prevEnd = (u: 0f, v: 0f);
        for (var k = 0; k < order.Count; k++)
        {
            var u = LaneU(order[k]);
            var start = (u, v: forward ? vNear : vFar);
            var end = (u, v: forward ? vFar : vNear);
            if (k > 0)
            {
                // The previous lane ended on this lane's start side; turn outward (away from the field).
                var outward = forward ? -1f : 1f;
                foreach (var (tu, tv) in Turn(prevEnd, start, outward, minTurnRadius))
                {
                    pts.Add(ToWorld(tu, tv));
                    work.Add(false);
                }
            }
            pts.Add(ToWorld(start.u, start.v));
            work.Add(false);
            pts.Add(ToWorld(end.u, end.v));
            work.Add(true);
            prevEnd = end;
            forward = !forward;
        }
        return new FieldPath(pts, work);
    }

    /// <summary>
    /// Arc from lane end <paramref name="a"/> to lane start <paramref name="b"/> (same v), bulging toward
    /// <paramref name="outward"/>: a semicircle when the lanes are at least two turning radii apart, else an omega loop.
    /// </summary>
    private static IEnumerable<(float u, float v)> Turn((float u, float v) a, (float u, float v) b, float outward, float minR)
    {
        var half = MathF.Abs(b.u - a.u) * 0.5f;
        var r = MathF.Max(half, minR);
        var h = MathF.Sqrt(MathF.Max(0f, r * r - half * half));
        var cu = (a.u + b.u) * 0.5f;
        var cv = a.v + outward * h;
        var angA = MathF.Atan2(a.v - cv, a.u - cu);
        var angB = MathF.Atan2(b.v - cv, b.u - cu);
        var angOut = MathF.Atan2(outward, 0f);
        var ccw = Wrap2Pi(angB - angA);
        var sweep = Wrap2Pi(angOut - angA) <= ccw ? ccw : -(MathF.Tau - ccw);
        var n = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(sweep) / 0.3f));
        for (var i = 1; i < n; i++)
        {
            var t = angA + sweep * i / n;
            yield return (cu + r * MathF.Cos(t), cv + r * MathF.Sin(t));
        }
    }

    private static float Wrap2Pi(float a)
    {
        a %= MathF.Tau;
        return a < 0f ? a + MathF.Tau : a;
    }
}

/// <summary>
/// A field helper: drives a vehicle over a field lane by lane and handles its implements like a farmer would:
/// lowers each one just before it enters the field, raises it when its work area leaves the field or while
/// turning, turns seeders/threshers on, and stops when out of seed or when the grain tank is full.
/// </summary>
public sealed class FieldWorkController : IVehicleController
{
    /// <summary>Seconds of travel to lower in advance (implements take ~0.6 s to reach working depth).</summary>
    private const float LowerLeadSeconds = 0.65f;
    private const float TurnSpeedKmh = 7f;
    private readonly List<Machine> _tools;
    private readonly float _workSpeedKmh;

    public FieldWorkController(Machine vehicle, FieldInfo field, float speedKmh = 0f, int? maxLanes = null)
    {
        Vehicle = vehicle;
        Field = field;
        _tools = vehicle.Chain().Where(m => m.Def.WorkArea != null).ToList();
        if (_tools.Count == 0) throw new InvalidOperationException($"{vehicle.Def.Name} has no implement to work with");

        var width = _tools.Min(t => t.Def.WorkArea!.Width) * 0.97f; // slight overlap, no stripes
        // How far the rearmost work area trails behind the vehicle's reference point.
        var reach = _tools.Max(t =>
        {
            var wa = t.Def.WorkArea!;
            var rear = t.LocalToWorld(wa.X, wa.Z - wa.Length * 0.5f);
            return -MathUtil.WorldToLocal(vehicle.Position, vehicle.Heading, rear).Y;
        });
        var mot = vehicle.Def.Motorized ?? throw new InvalidOperationException("Helpers drive motorized vehicles");
        var minR = mot.Wheelbase / MathF.Tan(mot.MaxSteerDeg * MathUtil.Deg2Rad) * 1.15f;
        Margin = MathF.Max(0f, reach) + 0.8f;
        _workSpeedKmh = speedKmh > 0f ? speedKmh : _tools.Min(t => t.Def.WorkArea!.MaxWorkSpeedKmh) * 0.9f;
        Path = FieldPlanner.Lanes(field, width, minR, Margin, vehicle.Position, maxLanes);
        Driver = new WaypointController(Path.Points, TurnSpeedKmh, Path.Work);

        foreach (var t in _tools)
        {
            t.Lowered = false;
            if (t.Def.WorkArea!.RequiresOn) t.TurnedOn = true;
        }
        if (vehicle.Def.HarvestTank != null) vehicle.TurnedOn = true;
    }

    public Machine Vehicle { get; }
    public FieldInfo Field { get; }
    public FieldPath Path { get; }
    public WaypointController Driver { get; }
    /// <summary>Headland distance driven past the field edge before turning.</summary>
    public float Margin { get; }
    public bool Stopped { get; private set; }
    public string? StopReason { get; private set; }
    public bool Finished => Driver.Finished || Stopped;

    public int LanesDone => Path.Work.Take(Math.Min(Driver.Index, Path.Work.Count)).Count(w => w);

    public VehicleInput GetInput(Machine v, float dt)
    {
        foreach (var t in _tools)
        {
            if (t.Status is not { } s || !(s.StartsWith("Out of seed") || s.StartsWith("Grain tank full") || s.StartsWith("Tank holds")))
                continue;
            Stopped = true;
            StopReason = s;
        }
        if (Finished)
        {
            foreach (var t in _tools) t.Lowered = false;
            return new VehicleInput { Brake = true };
        }

        var onLane = Path.Work[Driver.Index];
        Driver.SpeedKmh = onLane ? _workSpeedKmh : TurnSpeedKmh;
        var input = Driver.GetInput(v, dt);
        var laneDir = onLane ? Driver.SegmentDirection : Vector2.Zero;

        foreach (var t in _tools)
        {
            var wa = t.Def.WorkArea!;
            var fwd = t.Forward;
            var aligned = onLane && Vector2.Dot(fwd, laneDir) > 0.94f;
            var center = t.LocalToWorld(wa.X, wa.Z);
            if (t.Lowered)
            {
                // Lift as soon as the work area leaves the field (or the implement swings off the lane).
                if (!aligned || !Contains(center)) t.Lowered = false;
            }
            else
            {
                // Lower just before the work area's leading edge reaches the field.
                var lead = wa.Length * 0.5f + MathF.Max(0f, v.Speed) * LowerLeadSeconds;
                if (aligned && Contains(center + fwd * lead)) t.Lowered = true;
            }
        }
        return input;
    }

    private bool Contains(Vector2 p) =>
        p.X >= Field.X && p.X <= Field.X + Field.W && p.Y >= Field.Z && p.Y <= Field.Z + Field.H;
}
