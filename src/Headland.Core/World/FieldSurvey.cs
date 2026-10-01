using Headland.Core.Content;
using Headland.Core.Crops;

namespace Headland.Core.World;

/// <summary>
/// How a field is doing (the statistics' fields): what grows on most of it and how far it is, the share in that state,
/// the weeds, the fertilizing, the moisture, and what its crop would yield harvested now.
/// </summary>
public sealed record FieldSurvey(
    FieldInfo Field, CropDef? Crop, string State, float StateShare, float Weeds, float Fertilized, float Moisture, float Yield)
{
    /// <summary>Surveys <paramref name="field"/> cell by cell.</summary>
    public static FieldSurvey Of(WorldMap world, ContentDatabase content, FieldInfo field)
    {
        var L = world.Layers;
        var cells = world.CellsOf(field);
        var legend = MapLayers.Legend(content, MapLayer.Growth);
        var states = new int[legend.Count + 1];
        var crops = new int[content.Crops.Count + 1];
        int weeds = 0, fertilized = 0;
        var moisture = 0f;
        foreach (var i in cells)
        {
            states[MapLayers.EntryAt(world, content, MapLayer.Growth, i) is var e and >= 0 ? e : legend.Count]++;
            if (L.Crop[i] != 0 && L.Stage[i] != CropStage.Dead) crops[L.Crop[i]]++;
            if (WeedState.Living(L.Weeds[i])) weeds++;
            if (L.Fertilized[i] > 0) fertilized++;
            moisture += L.Moisture[i] / 255f;
        }
        var n = Math.Max(1, cells.Length);
        var top = Array.IndexOf(states, states.Max());
        var cropIndex = Array.IndexOf(crops, crops.Skip(1).DefaultIfEmpty(0).Max(), 1);
        var crop = cropIndex > 0 && crops[cropIndex] > 0 ? content.Crops[cropIndex - 1] : null;
        // What the crop would yield harvested as it is now: its cells' yield by their health and weeds.
        var yield = 0f;
        if (crop != null)
            foreach (var i in cells)
                if (L.Crop[i] == cropIndex && L.Stage[i] != CropStage.Dead)
                    yield += CropSystem.YieldPerHa(L, crop, i) * WorldMap.CellArea / 10000f;
        return new FieldSurvey(field, crop, top < legend.Count ? legend[top].Name : "Grass", (float)states[top] / n,
            (float)weeds / n, (float)fertilized / n, moisture / n, yield);
    }
}
