using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Tasks;

/// <summary>
/// A baler's helper drops what its chamber holds as a smaller bale: when the field is baled, so nothing is left in it,
/// or when what lies ahead is something else than what it holds (straw after grass).
/// </summary>
public sealed class DropBaleTask : HelperTask
{
    public override string Kind => "dropBale";

    public override string Describe(HelperJob job) => "dropping a bale";

    protected internal override VehicleInput Drive(HelperJob job, float dt)
    {
        if (MathF.Abs(job.Vehicle.Speed) > 0.1f) return Brake;
        foreach (var baler in job.Vehicle.Chain().Select(m => m.Get<Baler>()).OfType<Baler>())
            if (!baler.Chamber.IsEmpty) baler.Drop(job.Sim);
        Finish();
        return Brake;
    }
}
