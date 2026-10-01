using Headland.Core.Content;
using Headland.Core.Crops;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Mowing: cuts the ripe crops of its harvest groups (grass), which grow back from their regrowth stage; one that doesn't
/// is cleared to stubble. What the crop leaves when cut (its windrow: the grass) lies in the work area's windrow for a
/// baler to pick up, and the next cut needs fertilizing again.
/// </summary>
public sealed class MowerWork() : WorkType("mower", "Mown")
{
    public override bool Draft => false;

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        if (area.HarvestGroups.Length == 0) yield return "a mower needs harvestGroups";
    }

    public override bool Handles(WorkAreaDef area, CropDef crop) => area.HarvestGroups.Contains(crop.HarvestGroup);

    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) =>
        Cuttable(world.Layers, content, area, i) != null;

    internal override int Work(WorkPass pass)
    {
        var n = 0;
        foreach (var i in pass.Cells)
        {
            if (Cuttable(pass.Layers, pass.Content, pass.Area, i) is not { } crop) continue;
            if (crop.Windrow is { } w) pass.Swath(i, Windrows.FillOf(pass.Content, w.FillType), WindrowOf(pass.Layers, crop, w, i));
            Cut(pass.World, crop, i, pass.Angle);
            n++;
        }
        return n;
    }

    /// <summary>What cutting <paramref name="crop"/> in cell <paramref name="i"/> leaves of its windrow: less as it yields less.</summary>
    public static float WindrowOf(FieldLayers L, CropDef crop, CropWindrowDef windrow, int i) =>
        windrow.PerHa * CropSystem.YieldPerHa(L, crop, i) / MathF.Max(1f, crop.YieldPerHa) * WorldMap.CellArea / 10000f;

    /// <summary>The ripe crop in cell <paramref name="i"/>, when the area cuts it.</summary>
    private CropDef? Cuttable(FieldLayers L, ContentDatabase content, WorkAreaDef area, int i)
    {
        if (L.Crop[i] == 0 || L.Stage[i] == CropStage.Dead) return null;
        var crop = content.Crops[L.Crop[i] - 1];
        return crop.Stages[L.Stage[i]].Harvestable && Handles(area, crop) ? crop : null;
    }

    /// <summary>Cuts <paramref name="crop"/> in cell <paramref name="i"/> back to its regrowth stage (or clears it).</summary>
    public static void Cut(WorldMap world, CropDef crop, int i, byte angle)
    {
        if (crop.RegrowStage is not { } stage)
        {
            HarvesterWork.Clear(world, i, angle);
            return;
        }
        var L = world.Layers;
        L.Stage[i] = (byte)stage;
        L.Progress[i] = 0f;
        L.WorkAngle[i] = angle;
        L.Fertilized[i] = 0;
        MarkDirty(world, i, crop: true);
    }
}
