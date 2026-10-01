using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>Plowing: turns the soil over, burying whatever grew or lay cut on it, grown weeds too. Sown like a seedbed.</summary>
public sealed class PlowWork() : WorkType("plow", "Plowed")
{
    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => CanPlow(world, i);

    internal override int Work(WorkPass pass)
    {
        var n = 0;
        foreach (var i in pass.Cells)
            if (Plow(pass.World, i, pass.Angle)) n++;
        return n;
    }

    public static bool CanPlow(WorldMap world, int i)
    {
        var L = world.Layers;
        var g = (GroundType)L.Ground[i];
        return world.IsWorkable(g) && (g != GroundType.Plowed || L.Crop[i] != 0 || WeedState.Living(L.Weeds[i]));
    }

    /// <summary>Plows cell <paramref name="i"/>, if that changes it.</summary>
    public static bool Plow(WorldMap world, int i, byte angle)
    {
        if (!CanPlow(world, i)) return false;
        var L = world.Layers;
        L.Ground[i] = (byte)GroundType.Plowed;
        HarvesterWork.ClearCrop(L, i);
        L.WorkAngle[i] = angle;
        L.Weeds[i] = WeedState.None;
        Windrows.Clear(world, i);
        MarkDirty(world, i, crop: true);
        return true;
    }
}
