using Headland.Core.Content;
using Headland.Core.Crops;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Spraying herbicide, from the area's fill unit at its rate: the weeds die, and no new ones come up until the ground is
/// tilled or the crop harvested (<see cref="WeedState.Sprayed"/>). Sprayed where weeds grow: tilled, sown or stubble
/// ground, not a meadow.
/// </summary>
public sealed class SprayerWork() : WorkType("sprayer")
{
    public override bool Draft => false;

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content) =>
        SpreadErrors(area, machine, "herbicide");

    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => CanSpray(world, i);

    internal override int Work(WorkPass pass)
    {
        var n = 0;
        foreach (var i in pass.Cells)
        {
            if (!CanSpray(pass.World, i)) continue;
            if (!pass.Use(pass.Area.RatePerHa)) break;
            Spray(pass.World, i);
            n++;
        }
        return n;
    }

    public static bool CanSpray(WorldMap world, int i) =>
        CropSystem.WeedsGrowOn((GroundType)world.Layers.Ground[i]) && world.Layers.Weeds[i] != WeedState.Sprayed;

    public static void Spray(WorldMap world, int i)
    {
        world.Layers.Weeds[i] = WeedState.Sprayed;
        MarkDirty(world, i, crop: false);
    }
}
