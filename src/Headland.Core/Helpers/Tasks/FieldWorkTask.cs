using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Helpers.Tasks;

/// <summary>
/// Works a field lane by lane (FS: the field work task), handling the implements as a farmer would: lowers each one
/// just before it enters the field, raises it when its work area leaves the field or while turning, turns seeders and
/// threshers on (and one that isn't lowered, such as a spreader, on and off as it would lower it). It pauses for what
/// keeps the work from going on (<see cref="MachineCondition.Stops"/>: a full tank, out of seed or fuel), for its job to
/// see to or to stop the helper; the work goes on from there once it starts again.
/// </summary>
public sealed class FieldWorkTask : HelperTask
{
    /// <summary>Seconds of travel to lower in advance, past the time an implement takes to go down to work (~0.6 s).</summary>
    private const float LowerLeadSeconds = 0.05f;
    /// <summary>Meters of straight lane before a front work area reaches the field, to line up and lower it.</summary>
    private const float RunIn = 2.5f;
    private const float TurnSpeedKmh = 7f;
    private const float ReverseSpeedKmh = 5f;
    /// <summary>A lane is left out when less than this share of the field under it is left to do.</summary>
    private const float MinShareLeft = 0.03f;
    private readonly List<(Machine machine, WorkAreas areas)> _tools;
    private readonly float _workSpeedKmh;
    private readonly FieldInfo _field;

    /// <summary>
    /// Plans the route from where the vehicle stands, leaving out the lanes its implements have nothing left to do on
    /// (or takes back the route a save kept, with the margin it was planned with).
    /// </summary>
    internal FieldWorkTask(HelperJob job, float speedKmh, int? maxLanes, (float margin, FieldPath path)? saved = null)
    {
        var (vehicle, sim) = (job.Vehicle, job.Sim);
        _field = job.Field;
        SpeedKmh = speedKmh;
        MaxLanes = maxLanes;
        _tools = vehicle.Chain().Where(m => m.Has<WorkAreas>()).Select(m => (m, m.Get<WorkAreas>()!)).ToList();
        if (_tools.Count == 0)
        {
            Path = new FieldPath([], []);
            Driver = new WaypointController([], TurnSpeedKmh);
            return;
        }

        var width = _tools.Min(t => t.areas.MinWidth) * 0.97f; // slight overlap, no stripes
        // How far the rearmost work area trails behind the vehicle's reference point, and the frontmost reaches ahead.
        var reach = _tools.Max(t => -Along(vehicle, t, -0.5f));
        var ahead = _tools.Max(t => Along(vehicle, t, 0.5f));
        var minR = vehicle.Get<RunningGear>()!.Def.TurnRadius * 1.15f;
        // Past the field edge the rearmost work area clears the field before the turn, and after it the vehicle is
        // lined up in time to lower a front one (a combine's header) before it reaches the field.
        Margin = saved?.margin ?? MathF.Max(MathF.Max(0f, reach) + 0.8f, ahead + RunIn);
        // The implements' own speed: a worn one works slower, which the engine sees to (a reloaded helper aims the same).
        _workSpeedKmh = speedKmh > 0f ? speedKmh : _tools.Min(t => t.areas.Def.Areas.Min(a => a.MaxWorkSpeedKmh)) * 0.9f;
        // Backing up with a trailed implement would jackknife it.
        var canReverse = vehicle.Chain().All(m => m == vehicle || m.Get<Attachable>()?.Def.Mode == "mounted");
        Path = saved?.path ?? FieldPlanner.Lanes(_field, new LanePlan(width, minR, Margin)
        {
            From = vehicle.Position, Heading = vehicle.Heading, Trail = reach, MaxLanes = maxLanes, Reverse = canReverse,
            NeedsWork = (a, b, w) => ShareLeft(sim.World, sim.Content, a, b, w) >= MinShareLeft,
        });
        Driver = new WaypointController(Path.Points, TurnSpeedKmh, Path.Segments);
    }

    /// <summary>How far ahead of the vehicle's reference point the front (0.5) or rear (-0.5) edge of a tool's work areas is.</summary>
    private static float Along(Machine vehicle, (Machine machine, WorkAreas areas) tool, float edge)
    {
        var (center, _, length) = tool.areas.Bounds;
        return MathUtil.WorldToLocal(vehicle.Position, vehicle.Heading, tool.machine.LocalToWorld(center.X, center.Y + length * edge)).Y;
    }

    public override string Kind => "fieldWork";

    /// <summary>Work speed asked for (0 = the implements' own).</summary>
    public float SpeedKmh { get; }
    public int? MaxLanes { get; }
    public FieldPath Path { get; }
    public WaypointController Driver { get; }
    /// <summary>Headland distance driven past the field edge before turning.</summary>
    public float Margin { get; }

    public int LanesDone => Enumerable.Range(0, Math.Min(Driver.Index, Path.Points.Count)).Count(Path.EndsLane);

    /// <summary>
    /// It needs an implement (a header on a combine), the field's owner's leave for each kind of work it does (the farm's
    /// own land, or a contract for that work), and something left for it to do.
    /// </summary>
    public override string? Check(HelperJob job)
    {
        var vehicle = job.Vehicle;
        if (_tools.Count == 0) return vehicle.Has<Thresher>() ? "Attach a header first" : "Attach an implement first";
        var content = job.Sim.Content;
        foreach (var (_, areas) in _tools)
        foreach (var area in areas.Def.Areas)
            if (job.Sim.Farms.FieldBlocker(vehicle.FarmId, _field, area.Type, area.Work.Crop(areas, content)) is { } why)
                return why;
        return Path.LaneCount == 0 ? $"Nothing left for the {_tools[0].machine.Def.Name} to do on {_field.Label}" : null;
    }

    /// <summary>
    /// Takes over the implements: unfolded with their ridge markers up (FS), and what their work needs turned on (seeders,
    /// the thresher a header hangs on). Each is lowered as it reaches the field; one that isn't lowered is turned on then.
    /// </summary>
    protected override void Start(HelperJob job)
    {
        foreach (var (m, areas) in _tools)
        {
            if (m.Get<AnimatedParts>() is { } parts) parts.Folded = false;
            if (m.Get<RidgeMarker>() is { } markers) markers.State = 0;
            if (!SwitchedOnLanes(m, areas))
                foreach (var a in areas.Def.Areas)
                    a.Work.Start(areas);
        }
    }

    /// <summary>A tool that works turned on without being lowered (a spreader): turned on over the field like others are lowered.</summary>
    internal static bool SwitchedOnLanes(Machine tool, WorkAreas areas) => areas.CanTurnOn && tool.Get<Attachable>() is not { Def.Lowerable: true };

    public override string Describe(HelperJob job) => $"lane {Math.Min(LanesDone + 1, Path.LaneCount)}/{Path.LaneCount}";

    protected internal override VehicleInput Drive(HelperJob job, float dt)
    {
        var v = job.Vehicle;
        if (v.Chain().SelectMany(m => m.Conditions).FirstOrDefault(c => c.Stops) is { } reason)
        {
            Pause(reason);
            return Brake;
        }
        if (Driver.Finished) return Done(job);

        var segment = Path.Segments[Driver.Index];
        var onLane = segment == PathSegment.Work;
        Driver.SpeedKmh = segment switch
        {
            PathSegment.Work => _workSpeedKmh,
            PathSegment.Reverse => ReverseSpeedKmh,
            _ => TurnSpeedKmh,
        };
        var input = Driver.GetInput(v, dt);
        if (Driver.Finished) return Done(job);
        var laneDir = onLane ? Driver.SegmentDirection : Vector2.Zero;

        foreach (var (t, areas) in _tools)
        {
            var a = t.Get<Attachable>();
            var switched = SwitchedOnLanes(t, areas);
            if (a is not { Def.Lowerable: true } && !switched) continue;
            var (local, width, length) = areas.Bounds;
            var fwd = t.Forward;
            var aligned = onLane && Vector2.Dot(fwd, laneDir) > 0.94f;
            var center = t.LocalToWorld(local);
            var side = MathUtil.Left(t.Heading) * (width * 0.45f);
            // Lowered just before the work area's leading edge reaches the field, lifted as soon as the work area
            // leaves it (or the implement swings off the lane).
            var lead = (a?.LowerSeconds ?? 0f) + LowerLeadSeconds;
            var ahead = center + fwd * (length * 0.5f + MathF.Max(0f, v.Speed) * lead);
            var down = switched ? areas.On : a!.Lowered;
            var want = down ? aligned && (Touches(center, side) || Touches(ahead, side)) : aligned && Touches(ahead, side);
            if (want == down) continue;
            if (switched) areas.On = want;
            else a!.Lowered = want;
        }
        return input;
    }

    /// <summary>The route driven to its end: the implements raised, it's done.</summary>
    private VehicleInput Done(HelperJob job)
    {
        Release(job);
        Finish();
        return Brake;
    }

    /// <summary>Raises its implements, and switches off those it turned on over the field.</summary>
    protected internal override void Release(HelperJob job)
    {
        foreach (var (t, areas) in _tools)
        {
            if (t.Get<Attachable>() is { } a) a.Lowered = false;
            if (SwitchedOnLanes(t, areas)) areas.On = false;
        }
    }

    /// <summary>
    /// True if any part of a line across the work area is over the field. Work never spills out of the field
    /// (MachineSystem clips a helper's work to it), so on slanted edges the implement stays down across the edge.
    /// </summary>
    private bool Touches(Vector2 center, Vector2 side) =>
        _field.Contains(center) || _field.Contains(center + side) || _field.Contains(center - side);

    /// <summary>Share of the field under a swath <paramref name="width"/> wide along a → b that the implements would still change.</summary>
    private float ShareLeft(WorldMap world, ContentDatabase content, Vector2 a, Vector2 b, float width)
    {
        var length = Vector2.Distance(a, b);
        if (length < 0.01f) return 0f;
        Span<Vector2> corners = stackalloc Vector2[4];
        MathUtil.RectCorners((a + b) * 0.5f, MathUtil.HeadingOf(b - a), width * 0.5f, length * 0.5f, corners);
        var cells = new List<int>();
        Geometry.RasterizeConvex(corners, WorldMap.CellSize, world.CellsX, world.CellsZ, cells);
        int inside = 0, left = 0;
        foreach (var i in cells)
        {
            if (!_field.Contains(world.CellCenter(i % world.CellsX, i / world.CellsX))) continue;
            inside++;
            if (_tools.Any(t => t.areas.Def.Areas.Any(a => a.Work.WouldChange(world, content, a, i)))) left++;
        }
        return inside > 0 ? (float)left / inside : 0f;
    }
}
