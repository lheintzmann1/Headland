using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>How the segment arriving at a waypoint is driven.</summary>
public enum PathSegment : byte
{
    /// <summary>Forward with the implements raised: to the field, and turns.</summary>
    Drive,
    /// <summary>Forward along a working lane, driven to its end before turning.</summary>
    Work,
    /// <summary>Backward: the middle leg of a turn with reversing.</summary>
    Reverse,
}

/// <summary>
/// Pure-pursuit waypoint follower, forward and backward. The lookahead shrinks with speed for tight tracking, and it
/// never looks past the end of a working lane or a change of direction: the vehicle does not start turning before
/// the lane ends, and it comes to a stop where it starts reversing (or stops reversing).
/// </summary>
public sealed class WaypointController : IVehicleController
{
    /// <summary>Braking planned for stopping where the direction changes (m/s²).</summary>
    private const float StopDecel = 1.5f;
    /// <summary>Speed kept up to such a stop, so the vehicle doesn't stall short of it (m/s).</summary>
    private const float CreepSpeed = 0.4f;
    /// <summary>Steering error (rad) a standing vehicle waits out before pulling away.</summary>
    private const float SteerTolerance = 0.25f;
    private Vector2? _start;

    public WaypointController(IReadOnlyList<Vector2> waypoints, float speedKmh, IReadOnlyList<PathSegment>? segments = null)
    {
        Waypoints = waypoints;
        SpeedKmh = speedKmh;
        Segments = segments;
    }

    public IReadOnlyList<Vector2> Waypoints { get; }
    /// <summary>Per waypoint: how the segment arriving at it is driven (all <see cref="PathSegment.Drive"/> when null).</summary>
    public IReadOnlyList<PathSegment>? Segments { get; }
    /// <summary>Index of the waypoint being driven to (the current segment ends there).</summary>
    public int Index { get; internal set; }
    /// <summary>Where the vehicle was when it started driving (the first segment starts there).</summary>
    internal Vector2? Start { get => _start; set => _start = value; }
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

        var reverse = Segment(Index) == PathSegment.Reverse;
        var lookahead = Math.Clamp(1.6f + 0.55f * MathF.Abs(v.Speed), MinLookahead, MaxLookahead);
        var target = LookaheadPoint(v.Position, lookahead);
        var local = MathUtil.WorldToLocal(v.Position, v.Heading, target);
        var d2 = MathF.Max(local.LengthSquared(), 0.01f);
        // The same law steers backward: the fixed axle then leads, and steering left swings it left.
        var curvature = 2f * local.X / d2;
        var motor = v.Get<Motor>()!;
        var gear = v.Get<RunningGear>()!;
        var maxSteer = gear.Def.MaxSteer;
        var steer = Math.Clamp(gear.SteerFor(curvature, reverse) / maxSteer, -1f, 1f);
        var input = new VehicleInput { Steer = steer };
        // Standing, turn the wheels first: pulling away with them far off would leave the path.
        if (MathF.Abs(v.Speed) < 0.1f && MathF.Abs(steer * maxSteer - gear.SteerAngle) > SteerTolerance)
        {
            input.Brake = true;
            return input;
        }

        // Slow down for sharp turns, and to a stop where the direction changes.
        var targetSpeed = SpeedKmh * MathUtil.KmhToMs * MathUtil.Lerp(1f, 0.45f, MathUtil.Saturate(MathF.Abs(steer)));
        if (DistanceToStop(v.Position) is { } stop) targetSpeed = MathF.Min(targetSpeed, MathF.Max(CreepSpeed, MathF.Sqrt(2f * StopDecel * stop)));
        var speed = reverse ? -v.Speed : v.Speed;
        // Throttle is a share of the top speed the engine can reach now (on this ground and slope, with these
        // implements): aim at the speed wanted and half again what it is short of it, so it settles without overshooting.
        var top = MathF.Max(0.1f, reverse ? motor.TopReverse : motor.TopSpeed);
        if (speed > targetSpeed + 0.5f) input.Brake = true;
        else input.Throttle = Math.Clamp((targetSpeed + 0.5f * (targetSpeed - speed)) / top, 0.05f, 1f) * (reverse ? -1f : 1f);
        return input;
    }

    private Vector2 SegmentStart => Index == 0 ? _start!.Value : Waypoints[Index - 1];

    private PathSegment Segment(int k) => Segments != null && k < Segments.Count ? Segments[k] : PathSegment.Drive;

    /// <summary>True if the vehicle changes direction at waypoint <paramref name="k"/>.</summary>
    private bool IsCusp(int k) =>
        k + 1 < Waypoints.Count && (Segment(k) == PathSegment.Reverse) != (Segment(k + 1) == PathSegment.Reverse);

    /// <summary>A lane's end or a change of direction: reached by driving past it, never by cutting the corner.</summary>
    private bool IsHardEnd(int k) =>
        IsCusp(k) || Segment(k) == PathSegment.Work && (k + 1 >= Waypoints.Count || Segment(k + 1) != PathSegment.Work);

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
            if (t < 1f && (IsHardEnd(Index) || Vector2.Distance(pos, b) >= ArriveRadius)) return;
            Index++;
        }
    }

    /// <summary>Distance along the path to the next change of direction, when one is close.</summary>
    private float? DistanceToStop(Vector2 pos)
    {
        var seg = Waypoints[Index] - SegmentStart;
        var len = seg.Length();
        var d = len > 1e-3f ? MathF.Max(0f, Vector2.Dot(Waypoints[Index] - pos, seg / len)) : 0f;
        for (var k = Index; ; k++)
        {
            if (IsCusp(k)) return d;
            if (k + 1 >= Waypoints.Count || d > 15f) return null;
            d += Vector2.Distance(Waypoints[k], Waypoints[k + 1]);
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
            // Stay on a working lane (or a leg ending in a stop) until its end: extend straight ahead instead of
            // peeking into what comes next.
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

/// <summary>A planned field route: waypoints, and how the segment arriving at each one is driven.</summary>
public sealed record FieldPath(List<Vector2> Points, List<PathSegment> Segments)
{
    /// <summary>True if waypoint <paramref name="k"/> ends a working lane.</summary>
    public bool EndsLane(int k) =>
        Segments[k] == PathSegment.Work && (k + 1 == Segments.Count || Segments[k + 1] != PathSegment.Work);

    public int LaneCount => Enumerable.Range(0, Segments.Count).Count(EndsLane);
}

/// <summary>What a field route is planned for: the implements, the vehicle, and what's left to do.</summary>
/// <param name="WorkWidth">Distance between lanes at most (the swath, less a little overlap).</param>
/// <param name="TurnRadius">The tightest circle the vehicle drives in turns.</param>
/// <param name="Margin">
/// How far past the field edge the vehicle drives before turning: it should cover the implement's distance behind
/// the vehicle so the work area clears the edge.
/// </param>
public sealed record LanePlan(float WorkWidth, float TurnRadius, float Margin)
{
    /// <summary>Where the vehicle stands and which way it points (null: from the field's first corner).</summary>
    public Vector2? From { get; init; }
    public float? Heading { get; init; }
    /// <summary>How far behind <see cref="From"/> the rearmost work area ends (negative when it is ahead, like a header).</summary>
    public float Trail { get; init; }
    public int? MaxLanes { get; init; }
    /// <summary>The vehicle may back up (it pulls no trailed implement), so close lanes are joined by three-point turns.</summary>
    public bool Reverse { get; init; }
    /// <summary>Whether the swath of the given width along a → b (over the field) still needs working; lanes that don't are left out.</summary>
    public Func<Vector2, Vector2, float, bool>? NeedsWork { get; init; }
}

public static class FieldPlanner
{
    /// <summary>How closely a vehicle must point along the lanes (cos 20°) to go on along its own line.</summary>
    private const float LinedUp = 0.94f;

    /// <summary>
    /// Back-and-forth lanes worked one after the other, along the field's longer side from the corner nearest the
    /// vehicle. A vehicle pointing along either side of the field (within 20°) over it or just outside goes on along
    /// its own line instead: the lanes are laid out from it and the first one starts where it stands (standing at a
    /// lane's end facing out, it turns into the next one); it works toward the nearer edge first, then crosses over to
    /// the other side, taking in on the way what was left behind it on its first lane.
    /// Lanes at least a turning circle apart are joined by two quarter circles and a straight; closer ones by a
    /// three-point turn when the vehicle can reverse, else by a bulb turn that loops out and back in. On a field that
    /// isn't a rectangle each lane spans the field under its whole swath, and a turn goes out to the farthest lane end
    /// it passes.
    /// </summary>
    public static FieldPath Lanes(FieldInfo f, LanePlan plan)
    {
        var shape = f.Shape;
        var width = plan.WorkWidth;
        var frame = new Frame(shape, shape.Size.Y >= shape.Size.X, width);
        var pos = (u: 0f, v: 0f);
        var dir = 0f; // along v when lined up on a lane
        if (plan.From is { } p)
        {
            pos = frame.ToFrame(p);
            if (plan.Heading is { } h && MathUtil.Forward(h) is var fwd && MathF.Max(MathF.Abs(fwd.X), MathF.Abs(fwd.Y)) >= LinedUp)
            {
                var lined = new Frame(shape, MathF.Abs(fwd.Y) > MathF.Abs(fwd.X), width);
                var lp = lined.ToFrame(p);
                if (lp.u >= lined.U0 - width * 0.5f && lp.u <= lined.U0 + lined.Across + width * 0.5f)
                {
                    (frame, pos) = (lined, lp);
                    dir = MathF.Sign(lined.AlongZ ? fwd.Y : fwd.X);
                }
            }
        }

        var (us, anchor) = frame.LaneCenters(dir != 0f ? pos.u : null);
        (float lo, float hi) FieldSpan(float u) => frame.Span(u - width * 0.5f, u + width * 0.5f, 0f);
        (float near, float far) Span(float u) => frame.Span(u - width * 0.5f, u + width * 0.5f, plan.Margin);
        bool Needs(float u, float a, float b) => plan.NeedsWork?.Invoke(frame.ToWorld(u, a), frame.ToWorld(u, b), width) ?? true;
        var todo = us.Select(u => FieldSpan(u) is var (lo, hi) && Needs(u, lo, hi)).ToList();

        var pts = new List<Vector2>();
        var segs = new List<PathSegment>();
        void Add(float u, float v, PathSegment s)
        {
            pts.Add(frame.ToWorld(u, v));
            segs.Add(s);
        }

        (float u, float v)? prev = null; // where the last lane ended, heading out of the field
        var forward = true; // the next lane is driven toward increasing v
        var lanes = plan.MaxLanes ?? int.MaxValue;
        if (dir != 0f)
        {
            // Lined up on lane `anchor`: work the rest of it from here, or turn from here if the work area is past it.
            var u = us[anchor];
            var (near, far) = Span(u);
            var (start, end) = dir > 0f ? (near, far) : (far, near);
            var (fieldLo, fieldHi) = FieldSpan(u);
            var (fieldStart, fieldEnd) = dir > 0f ? (fieldLo, fieldHi) : (fieldHi, fieldLo);
            var split = Math.Clamp(pos.v - dir * plan.Trail, fieldLo, fieldHi); // where the work area is
            if ((fieldEnd - split) * dir <= 1f) prev = pos;
            else if (Needs(u, split, fieldEnd))
            {
                if ((start - pos.v) * dir > 0.5f) Add(u, start, PathSegment.Drive);
                else if (MathF.Abs(pos.u - u) > 0.05f && pos.v + dir * MathF.Max(4f, 6f * MathF.Abs(pos.u - u)) is var join && (end - join) * dir > 1f)
                    Add(u, join, PathSegment.Work); // onto the lane line
                Add(u, end, PathSegment.Work);
                prev = (u, end);
                todo[anchor] = (split - fieldStart) * dir > 1f && Needs(u, fieldStart, split);
                lanes--;
            }
            else dir = 0f; // nothing to do ahead: start over from a corner
            forward = dir < 0f;
        }

        var order = Enumerable.Range(0, us.Count).Where(l => todo[l]).ToList();
        if (prev is { } at)
        {
            // Toward the nearer edge first, then the other side. A lane under way (what's left behind the vehicle on
            // it) is done on the way back over, or right away when there is only one side left.
            var same = order.Where(l => MathF.Abs(us[l] - at.u) <= 0.01f).ToList();
            var below = order.Where(l => us[l] < at.u - 0.01f).Reverse().ToList();
            var above = order.Where(l => us[l] > at.u + 0.01f).ToList();
            var (first, second) = below.Count > 0 && (above.Count == 0 || at.u - us[below[^1]] <= us[above[^1]] - at.u)
                ? (below, above)
                : (above, below);
            (second.Count > 0 ? second : first).InsertRange(0, same);
            order = [.. first, .. second];
        }
        else if (plan.From != null && order.Count > 0)
        {
            if (MathF.Abs(pos.u - us[order[^1]]) < MathF.Abs(pos.u - us[order[0]])) order.Reverse();
            var (near, far) = Span(us[order[0]]);
            forward = MathF.Abs(pos.v - near) <= MathF.Abs(pos.v - far);
        }

        foreach (var l in order.Take(Math.Max(0, lanes)))
        {
            var u = us[l];
            var (near, far) = Span(u);
            var (start, end) = forward ? (near, far) : (far, near);
            if (prev is { } pe)
            {
                // Turn outward (away from the field), level with the farthest lane end in between.
                var outward = forward ? -1f : 1f;
                var (lo, hi) = frame.Span(MathF.Min(pe.u, u) - width * 0.5f, MathF.Max(pe.u, u) + width * 0.5f, plan.Margin);
                var turnV = forward ? MathF.Min(pe.v, lo) : MathF.Max(pe.v, hi);
                if (MathF.Abs(turnV - pe.v) > 0.01f) Add(pe.u, turnV, PathSegment.Drive);
                Turn(pe.u, u, turnV, outward, plan.TurnRadius, plan.Reverse, Add);
            }
            else Add(u, start, PathSegment.Drive);
            Add(u, end, PathSegment.Work);
            prev = (u, end);
            forward = !forward;
        }
        return new FieldPath(pts, segs);
    }

    /// <summary>
    /// Turn from a lane end at <paramref name="ua"/>, heading <paramref name="outward"/> along v, into the lane at
    /// <paramref name="ub"/>, both ends at <paramref name="v"/>; adds every waypoint after the lane end up to the next
    /// lane's start. Two quarter circles joined by a straight, driven backward when the lanes are closer than a turning
    /// circle and the vehicle can reverse; else, that close, a bulb: out away from the next lane, around, and back in.
    /// </summary>
    private static void Turn(float ua, float ub, float v, float outward, float r, bool reverse, Action<float, float, PathSegment> add)
    {
        var side = ub >= ua ? 1f : -1f;
        var d = MathF.Abs(ub - ua);
        if (d >= 2f * r || reverse)
        {
            var quarter = -side * outward * MathF.PI * 0.5f;
            Arc(add, (ua + side * r, v), r, side > 0f ? MathF.PI : 0f, quarter);
            var across = d - 2f * r;
            if (MathF.Abs(across) > 0.05f) add(ub - side * r, v + outward * r, across > 0f ? PathSegment.Drive : PathSegment.Reverse);
            Arc(add, (ub - side * r, v), r, outward * MathF.PI * 0.5f, quarter);
            return;
        }
        // Out by α, around by π + 2α, in by α: three arcs of radius r, the middle one centered halfway between the lanes.
        var alpha = MathF.Acos((d + 2f * r) / (4f * r));
        var turn = side * outward;
        var c = (u: ua - side * r, v);
        var a = side > 0f ? 0f : MathF.PI;
        ReadOnlySpan<float> sweeps = [turn * alpha, -turn * (MathF.PI + 2f * alpha), turn * alpha];
        foreach (var sweep in sweeps)
        {
            Arc(add, c, r, a, sweep);
            // The next arc bends the other way: its center mirrors this one's through the point reached.
            a += sweep;
            c = (c.u + 2f * r * MathF.Cos(a), c.v + 2f * r * MathF.Sin(a));
            a += MathF.PI;
        }
    }

    /// <summary>Forward along a circle from angle <paramref name="a0"/> by <paramref name="sweep"/>, the start left out.</summary>
    private static void Arc(Action<float, float, PathSegment> add, (float u, float v) c, float r, float a0, float sweep)
    {
        var n = Math.Max(2, (int)MathF.Ceiling(MathF.Abs(sweep) / 0.3f));
        for (var i = 1; i <= n; i++)
        {
            var t = a0 + sweep * i / n;
            add(c.u + r * MathF.Cos(t), c.v + r * MathF.Sin(t), PathSegment.Drive);
        }
    }

    /// <summary>The field seen along one of its sides: u across the lanes, v along them.</summary>
    private sealed class Frame(Polygon shape, bool alongZ, float width)
    {
        public bool AlongZ { get; } = alongZ;
        public float U0 { get; } = alongZ ? shape.Min.X : shape.Min.Y;
        public float V0 { get; } = alongZ ? shape.Min.Y : shape.Min.X;
        public float Across { get; } = alongZ ? shape.Size.X : shape.Size.Y;
        public float Along { get; } = alongZ ? shape.Size.Y : shape.Size.X;

        public Vector2 ToWorld(float u, float v) => AlongZ ? new Vector2(u, v) : new Vector2(v, u);
        public (float u, float v) ToFrame(Vector2 p) => AlongZ ? (p.X, p.Y) : (p.Y, p.X);

        /// <summary>The field's extent along v under the strip <paramref name="u1"/>..<paramref name="u2"/>, widened by <paramref name="margin"/>.</summary>
        public (float lo, float hi) Span(float u1, float u2, float margin)
        {
            var (lo, hi) = shape.Extent(AlongZ, u1, u2) ?? (V0, V0 + Along);
            return (lo - margin, hi + margin);
        }

        /// <summary>
        /// Lane centers, ascending, at most a lane width apart and evenly spaced: edge to edge, or through
        /// <paramref name="anchor"/> and evenly on each side of it (an anchor within half a lane of the edge lane moves
        /// onto it rather than leave a sliver). Also returns the index of the anchor's lane.
        /// </summary>
        public (List<float> us, int anchor) LaneCenters(float? anchor)
        {
            var lo = U0 + width * 0.5f;
            var hi = U0 + Across - width * 0.5f;
            if (hi - lo < 0.01f) return ([U0 + Across * 0.5f], 0);
            var a = anchor ?? lo;
            a = a < lo + width * 0.5f ? lo : a > hi - width * 0.5f ? hi : a;
            var below = (int)MathF.Ceiling((a - lo) / width - 0.01f);
            var above = (int)MathF.Ceiling((hi - a) / width - 0.01f);
            var us = new List<float>();
            for (var i = below; i > 0; i--) us.Add(a - (a - lo) * i / below);
            us.Add(a);
            for (var i = 1; i <= above; i++) us.Add(a + (hi - a) * i / above);
            return (us, below);
        }
    }
}
