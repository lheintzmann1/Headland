using System.Numerics;
using Headland.Core.Machines;

namespace Headland.Core.Helpers.Tasks;

/// <summary>
/// A bale collector's helper taking its load to where the bales go, beside the field: where it was hired when that was
/// off the field, else just off the field's edge nearest to it. It stops there along the edge and sets the bales down
/// behind it, each load further along than the one before.
/// </summary>
public sealed class SetDownBalesTask : HelperTask
{
    private const float SpeedKmh = 9f;
    /// <summary>Between one load's stop and the next: past a collector and the bales it set down.</summary>
    private const float Spacing = 16f;
    /// <summary>How far off the field the bales go when it was hired on it.</summary>
    private const float OffField = 8f;
    private const float StuckSeconds = 20f;
    private WaypointController? _driver;
    private (float distance, float seconds) _progress;

    /// <param name="spot">Where the first load is set down, and which way the collector stands there.</param>
    /// <param name="loads">Loads set down already.</param>
    internal SetDownBalesTask(Vector2 spot, float heading, int loads = 0)
    {
        Spot = spot;
        Heading = heading;
        Loads = loads;
    }

    public override string Kind => "setDownBales";

    public Vector2 Spot { get; }
    public float Heading { get; }
    public int Loads { get; private set; }

    /// <summary>Where the next load goes: where the collector stops, along the edge from the first.</summary>
    public Vector2 Stop => Spot + MathUtil.Forward(Heading) * (Spacing * Loads);

    /// <summary>
    /// Where a job's bales go: where the vehicle is when off the field, else just off the field's edge nearest to it;
    /// the collector standing along that edge, the way the vehicle faces, each load ahead of the one before.
    /// </summary>
    internal static (Vector2 spot, float heading) SpotFor(HelperJob job)
    {
        var (v, shape) = (job.Vehicle, job.Field.Shape);
        var (edge, along) = shape.ClosestEdgePoint(v.Position);
        if (Vector2.Dot(along, v.Forward) < 0f) along = -along;
        if (shape.Distance(v.Position) > 1f) return (v.Position, MathUtil.HeadingOf(along));
        var outward = new Vector2(-along.Y, along.X);
        if (shape.Contains(edge + outward)) outward = -outward;
        return (edge + outward * OffField, MathUtil.HeadingOf(along));
    }

    public override string Describe(HelperJob job) => "setting the bales down";

    protected override void Start(HelperJob job)
    {
        var stop = Stop;
        var dir = MathUtil.Forward(Heading);
        _driver = new WaypointController([stop - dir * 12f, stop], SpeedKmh);
        _progress = (float.MaxValue, 0f);
    }

    protected internal override VehicleInput Drive(HelperJob job, float dt)
    {
        var v = job.Vehicle;
        _driver ??= new WaypointController([Stop], SpeedKmh);
        var distance = Vector2.Distance(v.Position, Stop);
        _progress = distance < _progress.distance - 0.5f ? (distance, 0f) : (_progress.distance, _progress.seconds + dt);
        if (!_driver.Finished && _progress.seconds < StuckSeconds) return _driver.GetInput(v, dt);
        if (MathF.Abs(v.Speed) > 0.3f) return Brake;
        CollectBalesTask.LoaderOf(job)?.Unload(job.Sim);
        Loads++;
        Finish();
        return Brake;
    }
}
