using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Sowing: the crop the driver picked, from the area's fill unit, into a seedbed or plowed ground. Out of its sowing
/// window it still comes up, but weak.
/// </summary>
public sealed class SeederWork() : WorkType("seeder", "Sown")
{
    public override bool Sows => true;

    public override CropDef? Crop(WorkAreas areas, ContentDatabase content) => content.Crops[areas.Crop];

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        if (FillTypesOf(machine, area.FillUnit).Length == 0) yield return "a seeder needs the fillUnit its seed comes from";
    }

    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => CanSow(world, i);

    internal override int Work(WorkPass pass)
    {
        var crop = pass.Content.Crops[pass.Areas.Crop];
        var inWindow = pass.Sim.Crops.InSowingWindow(crop, pass.Sim.Clock.Month);
        var health = inWindow ? (byte)255 : (byte)150;
        if (!inWindow) pass.Areas.Report(new OutOfSeason(crop));
        var n = 0;
        foreach (var i in pass.Cells)
        {
            if (!CanSow(pass.World, i)) continue;
            if (!pass.Use(crop.SeedKgPerHa)) break;
            Sow(pass.World, pass.Content, i, pass.Areas.Crop, health, pass.Angle);
            n++;
        }
        return n;
    }

    public static bool CanSow(WorldMap world, int i)
    {
        var g = (GroundType)world.Layers.Ground[i];
        return g is GroundType.Cultivated or GroundType.Plowed && world.Layers.Crop[i] == 0;
    }

    /// <summary>Sows crop <paramref name="cropIndex"/> (into <see cref="ContentDatabase.Crops"/>) in cell <paramref name="i"/>.</summary>
    public static void Sow(WorldMap world, ContentDatabase content, int i, int cropIndex, byte health, byte angle)
    {
        var L = world.Layers;
        L.Ground[i] = (byte)content.Crops[cropIndex].SownGround;
        L.Crop[i] = (byte)(cropIndex + 1);
        L.Stage[i] = 0;
        L.Progress[i] = 0f;
        L.Chill[i] = 0;
        L.Health[i] = health;
        L.WorkAngle[i] = angle;
        MarkDirty(world, i, crop: true);
    }
}
