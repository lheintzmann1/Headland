using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Mowing: cuts the ripe crops of its harvest groups (grass), which grow back from their regrowth stage; one that doesn't
/// is cleared to stubble. The cut crop lies on the field (balers will pick it up), and the next cut needs fertilizing
/// again.
/// </summary>
public sealed class MowerWork() : WorkType("mower")
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
            Cut(pass.World, crop, i, pass.Angle);
            n++;
        }
        return n;
    }

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
