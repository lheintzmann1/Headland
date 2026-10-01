using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Shallow tillage: stubble, grass, a meadow or a failed crop into a seedbed, with what lay cut on it. It kills small
/// weeds, and the herbicide's hold on the ground ends; grown weeds survive it (the plow buries them).
/// </summary>
public sealed class CultivatorWork() : WorkType("cultivator")
{
    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => CanTill(world, i);

    internal override int Work(WorkPass pass)
    {
        var n = 0;
        foreach (var i in pass.Cells)
            if (Till(pass.World, i, pass.Angle)) n++;
        return n;
    }

    public static bool CanTill(WorldMap world, int i)
    {
        var L = world.Layers;
        var g = (GroundType)L.Ground[i];
        return world.IsWorkable(g) && (g != GroundType.Cultivated || L.Crop[i] != 0 || L.Weeds[i] == WeedState.Small);
    }

    /// <summary>Tills cell <paramref name="i"/> into a seedbed, if that changes it.</summary>
    public static bool Till(WorldMap world, int i, byte angle)
    {
        if (!CanTill(world, i)) return false;
        var L = world.Layers;
        L.Ground[i] = (byte)GroundType.Cultivated;
        HarvesterWork.ClearCrop(L, i);
        L.WorkAngle[i] = angle;
        if (L.Weeds[i] != WeedState.Grown) L.Weeds[i] = WeedState.None;
        Windrows.Clear(world, i);
        MarkDirty(world, i, crop: true);
        return true;
    }
}
