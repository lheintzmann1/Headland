using Headland.Core.Helpers.Tasks;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Jobs;

/// <summary>
/// Harvests a field with a combine: it unloads into a trailer driven alongside as it goes, and with its tank full it
/// waits, its pipe out, for a trailer to unload into, then goes on harvesting.
/// </summary>
public sealed class HarvestJob() : HelperJobType("harvest", "Harvest")
{
    public override bool Fits(Machine vehicle) => vehicle.Chain().Any(m => m.Has<Thresher>());

    protected internal override IReadOnlyList<HelperTask> Tasks(HelperJob job, JobSetup setup)
    {
        var work = new FieldWorkTask(job, setup.SpeedKmh, setup.MaxLanes, setup.Route);
        return job.Vehicle.Chain().Any(m => m.Has<Pipe>()) ? [work, new UnloadTask()] : [work];
    }

    protected internal override int? AfterPause(HelperJob job, int paused, MachineCondition reason) =>
        reason is TankFull or TankHolds && IndexOf<UnloadTask>(job) is var unload and >= 0 ? unload : null;

    /// <summary>Unloaded, back to the field work, unless the field was done.</summary>
    protected internal override int? Next(HelperJob job, int done) =>
        job.Tasks[done] is UnloadTask && job.FieldWork is { State: not TaskState.Done } ? IndexOf<FieldWorkTask>(job) : null;

    protected internal override void Update(HelperJob job, float dt)
    {
        if (job.Current is FieldWorkTask && job.Tasks.OfType<UnloadTask>().FirstOrDefault() is { } unload) unload.OnTheGo(job, job.Sim, dt);
    }
}
