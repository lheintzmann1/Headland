using Headland.Core.Helpers.Tasks;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Jobs;

/// <summary>Works a field with the implements attached (FS: field work): cultivating, sowing, spreading, mowing…</summary>
public sealed class FieldWorkJob() : HelperJobType("fieldWork", "Field work")
{
    public override bool Fits(Machine vehicle) => vehicle.Chain().Any(m => m.Has<WorkAreas>());

    protected internal override IReadOnlyList<HelperTask> Tasks(HelperJob job, JobSetup setup) =>
        [new FieldWorkTask(job, setup.SpeedKmh, setup.MaxLanes, setup.Route)];
}
