using Headland.Core.Helpers.Tasks;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Jobs;

/// <summary>
/// Bales what lies cut on a field: the field done, it drops what's left in the chamber as a bale, and so it does when
/// what lies ahead is something else than what the chamber holds, before going on.
/// </summary>
public sealed class BaleJob() : HelperJobType("bale", "Baling")
{
    public override bool Fits(Machine vehicle) => vehicle.Chain().Any(m => m.Has<Baler>());

    protected internal override IReadOnlyList<HelperTask> Tasks(HelperJob job, JobSetup setup) =>
        [new FieldWorkTask(job, setup.SpeedKmh, setup.MaxLanes, setup.Route), new DropBaleTask()];

    protected internal override int? AfterPause(HelperJob job, int paused, MachineCondition reason) =>
        reason is BalerHolds ? IndexOf<DropBaleTask>(job) : null;

    /// <summary>The field done, the bale dropped; a bale dropped before it was, back to the field.</summary>
    protected internal override int? Next(HelperJob job, int done) => job.Tasks[done] switch
    {
        FieldWorkTask => IndexOf<DropBaleTask>(job),
        _ => job.FieldWork is { State: not TaskState.Done } ? IndexOf<FieldWorkTask>(job) : null,
    };
}
