using Headland.Core.Helpers.Tasks;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Jobs;

/// <summary>
/// Collects the bales lying on a field with a bale collector: it picks them up until it's full, sets them down beside
/// the field, and goes back for more until none is left.
/// </summary>
public sealed class CollectBalesJob() : HelperJobType("collectBales", "Bale collection")
{
    public override bool Fits(Machine vehicle) => vehicle.Chain().Any(m => m.Has<BaleLoader>());

    protected internal override IReadOnlyList<HelperTask> Tasks(HelperJob job, JobSetup setup)
    {
        var (spot, heading, loads) = setup.Stacks ?? (SetDownBalesTask.SpotFor(job) is var s ? (s.spot, s.heading, 0) : default);
        return [new CollectBalesTask(setup.Missed), new SetDownBalesTask(spot, heading, loads)];
    }

    /// <summary>Full, or with nothing left to collect, it sets its bales down; set down, back for more while some are left.</summary>
    protected internal override int? Next(HelperJob job, int done) => job.Tasks[done] switch
    {
        CollectBalesTask => CollectBalesTask.LoaderOf(job) is { Count: > 0 } ? IndexOf<SetDownBalesTask>(job) : null,
        _ => job.Tasks.OfType<CollectBalesTask>().First().Left(job).Any() ? IndexOf<CollectBalesTask>(job) : null,
    };
}
