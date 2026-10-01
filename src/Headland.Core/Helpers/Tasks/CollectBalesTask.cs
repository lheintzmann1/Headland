using System.Numerics;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Objects;

namespace Headland.Core.Helpers.Tasks;

/// <summary>
/// A bale collector's helper picking up the bales of its farm lying on its field, nearest first (ahead before behind):
/// it drives a line that takes each through the collector's pickup, slowly near it, and gives up on one it missed
/// twice. Done when the collector is full or no bale is left that it takes.
/// </summary>
public sealed class CollectBalesTask : HelperTask
{
    private const float ApproachKmh = 9f;
    private const float PickKmh = 5f;
    /// <summary>Passes over a bale without taking it before giving up on it.</summary>
    private const int Tries = 2;
    /// <summary>Seconds without getting nearer to the bale before giving up on this pass (something in the way).</summary>
    private const float StuckSeconds = 15f;
    /// <summary>How far from the field's edge a bale still counts as on it.</summary>
    private const float Margin = 3f;
    private readonly Dictionary<int, int> _missed;
    private WorldObject? _target;
    private WaypointController? _driver;
    private (float distance, float seconds) _progress;

    internal CollectBalesTask(Dictionary<int, int>? missed = null) => _missed = missed ?? [];

    public override string Kind => "collectBales";

    /// <summary>The bales it missed, by id, and how many times: saved.</summary>
    public IReadOnlyDictionary<int, int> Missed => _missed;

    internal static BaleLoader? LoaderOf(HelperJob job) => job.Vehicle.Chain().Select(m => m.Get<BaleLoader>()).FirstOrDefault(l => l != null);

    /// <summary>The bales left for it: its farm's, lying loose on the field (or just off it), that the collector takes, not given up on.</summary>
    public IEnumerable<WorldObject> Left(HelperJob job) => LoaderOf(job) is not { } loader
        ? []
        : job.Sim.Objects.All.Where(o => o.Holder == null && loader.Takes(o) && job.Field.Shape.Distance(o.Position) <= Margin
                                         && _missed.GetValueOrDefault(o.Id) < Tries);

    public override string? Check(HelperJob job) =>
        LoaderOf(job) is { Count: 0 } && !Left(job).Any() ? $"No bales of yours lie on {job.Field.Label}" : null;

    public override string Describe(HelperJob job) =>
        Left(job).Count() is var n && n == 1 ? "collecting bales, 1 left" : $"collecting bales, {n} left";

    protected override void Start(HelperJob job)
    {
        if (LoaderOf(job) is { } loader) loader.On = true;
        _target = null;
    }

    protected internal override VehicleInput Drive(HelperJob job, float dt)
    {
        if (LoaderOf(job) is not { } loader || loader.Count >= loader.Capacity)
        {
            Finish();
            return Brake;
        }
        loader.On = true;
        if (_target is { Holder: not null }) _target = null;
        if (_target == null)
        {
            if (Pick(job) is not { } bale)
            {
                Finish();
                return Brake;
            }
            Plan(job, loader, bale);
        }

        var pickup = loader.Def.Pickup.On(loader.Machine).Center;
        var distance = Vector2.Distance(pickup, _target!.Position);
        _progress = distance < _progress.distance - 0.5f ? (distance, 0f) : (_progress.distance, _progress.seconds + dt);
        _driver!.SpeedKmh = distance < 10f ? PickKmh : ApproachKmh;
        if (_driver.Finished || _progress.seconds > StuckSeconds)
        {
            _missed[_target.Id] = _missed.GetValueOrDefault(_target.Id) + 1;
            _target = null;
            return Brake;
        }
        return _driver.GetInput(job.Vehicle, dt);
    }

    /// <summary>The bale to go for next: the nearest, those ahead before those behind.</summary>
    private WorldObject? Pick(HelperJob job)
    {
        var v = job.Vehicle;
        return Left(job)
            .OrderBy(o => Vector2.Distance(v.Position, o.Position) + (Vector2.Dot(o.Position - v.Position, v.Forward) < 0f ? 15f : 0f))
            .FirstOrDefault();
    }

    /// <summary>
    /// A line toward <paramref name="bale"/> from where the vehicle is, beside it by as far as the pickup is off the
    /// vehicle's line, driven until the pickup has gone past it.
    /// </summary>
    private void Plan(HelperJob job, BaleLoader loader, WorldObject bale)
    {
        var v = job.Vehicle;
        var pick = loader.Def.Pickup;
        var pickup = MathUtil.WorldToLocal(v.Position, v.Heading, loader.Machine.LocalToWorld(pick.X, pick.Z));
        var heading = MathUtil.HeadingOf(bale.Position - v.Position);
        var line = bale.Position - MathUtil.Left(heading) * pickup.X;
        var lead = MathF.Min(8f, Vector2.Distance(v.Position, line) * 0.5f);
        Vector2[] points = [line - MathUtil.Forward(heading) * lead, line + MathUtil.Forward(heading) * (MathF.Max(0f, -pickup.Y) + 2f)];
        _driver = new WaypointController(points, ApproachKmh);
        _target = bale;
        _progress = (float.MaxValue, 0f);
    }

    protected internal override void Release(HelperJob job)
    {
        if (LoaderOf(job) is { } loader) loader.On = false;
    }
}
