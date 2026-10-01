using Headland.Core.Content;
using Headland.Core.Crops;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// A header cutting ripe crops of its harvest groups for the thresher it hangs on, which threshes them into its tank,
/// and clearing dead ones: the field is left as stubble. The yield is the crop's, less what its health and weeds cost.
/// What the crop leaves when cut (its windrow: the straw) falls in the thresher's swath, behind it.
/// </summary>
public sealed class HarvesterWork() : WorkType("harvester")
{
    public override bool Draft => false;
    public override bool Harvests => true;
    public override IEnumerable<string> Roles => ["reel"];

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        if (area.HarvestGroups.Length == 0) yield return "a harvester needs harvestGroups";
        if (machine.Get<AttachableDef>() == null) yield return "a harvester hangs on a thresher: it needs an attachable";
    }

    /// <summary>It cuts while the thresher it hangs on threshes.</summary>
    internal override bool Working(WorkAreas areas, WorkAreaDef area) => areas.Machine.Parent?.Get<Thresher>() is { On: true };

    internal override void Start(WorkAreas areas)
    {
        if (areas.Machine.Parent?.Get<Thresher>() is { } thresher) thresher.On = true;
    }

    public override bool Handles(WorkAreaDef area, CropDef crop) => area.HarvestGroups.Contains(crop.HarvestGroup);

    /// <summary>Ripe and dead crops, whatever the header: a wrong one says so as it goes.</summary>
    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i)
    {
        var L = world.Layers;
        return L.Crop[i] != 0 && (L.Stage[i] == CropStage.Dead || content.Crops[L.Crop[i] - 1].Stages[L.Stage[i]].Harvestable);
    }

    internal override int Work(WorkPass pass)
    {
        var thresher = pass.Machine.Parent!.Get<Thresher>()!;
        var tank = thresher.Tank;
        var L = pass.Layers;
        var n = 0;
        CropDef? threshed = null;
        var threshedAmount = 0f;
        thresher.Refused = null;
        foreach (var i in pass.Cells)
        {
            var cropId = L.Crop[i];
            if (cropId == 0) continue;
            var def = pass.Content.Crops[cropId - 1];
            if (L.Stage[i] == CropStage.Dead)
            {
                Clear(pass.World, i, pass.Angle);
                n++;
                continue;
            }
            if (!def.Stages[L.Stage[i]].Harvestable) continue;
            if (!Handles(pass.Area, def))
            {
                pass.Areas.Report(new WrongHeader(def));
                continue;
            }
            var liters = CropSystem.YieldPerHa(L, def, i) * WorldMap.CellArea / 10000f;
            if (!tank.CanAccept(def.FillType) || tank.Free < liters)
            {
                thresher.Refused = tank.IsEmpty || tank.FillType == def.FillType
                    ? new TankFull()
                    : new TankHolds(pass.Content.FillTypes[tank.FillType!]);
                pass.Sim.Notifications.Post(thresher.Refused.Text, Severity.Warning, 10);
                break;
            }
            tank.Add(def.FillType, liters);
            if (def.Windrow is { } w && thresher.Def.Swath is { } swath)
            {
                // Across the header, into the swath behind the combine.
                var x = swath.X + Windrows.Gather(pass.InArea(i).X, pass.Area.Width, swath.Width);
                Windrows.Drop(pass.World, thresher.Machine.LocalToWorld(x, swath.Z), Windrows.FillOf(pass.Content, w.FillType),
                    MowerWork.WindrowOf(L, def, w, i));
            }
            threshed ??= def;
            threshedAmount += liters;
            Clear(pass.World, i, pass.Angle);
            n++;
        }
        // The tank takes one fill type at a time, so one tick threshes one crop.
        if (threshed != null)
            pass.Sim.Events.Publish(new CropHarvested(pass.Machine.Parent, threshed.Id, threshed.FillType, threshedAmount, pass.FieldId));
        return n;
    }

    /// <summary>
    /// After harvest (or clearing a dead crop): stubble, with a little nitrogen returned by residue. A new season starts:
    /// fertilizing counts from none again, and the herbicide's hold on the ground ends.
    /// </summary>
    public static void Clear(WorldMap world, int i, byte angle)
    {
        var L = world.Layers;
        L.Ground[i] = (byte)GroundType.Stubble;
        ClearCrop(L, i);
        L.WorkAngle[i] = angle;
        L.Nitrogen[i] = (byte)Math.Min(255, L.Nitrogen[i] + 4);
        L.Fertilized[i] = 0;
        if (L.Weeds[i] == WeedState.Sprayed) L.Weeds[i] = WeedState.None;
        MarkDirty(world, i, crop: true);
    }

    internal static void ClearCrop(FieldLayers L, int i)
    {
        L.Crop[i] = 0;
        L.Stage[i] = 0;
        L.Progress[i] = 0f;
        L.Health[i] = 0;
        L.Chill[i] = 0;
    }
}
