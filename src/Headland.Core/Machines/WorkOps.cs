using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>Per-cell state transitions applied by implements. Each returns true when the cell changed.</summary>
public static class WorkOps
{
    public static bool Cultivate(WorldMap world, int i, byte angle)
    {
        var L = world.Layers;
        var g = (GroundType)L.Ground[i];
        if (!world.IsWorkable(g)) return false;
        if (g == GroundType.Cultivated && L.Crop[i] == 0) return false;
        L.Ground[i] = (byte)GroundType.Cultivated;
        ClearCrop(L, i);
        L.WorkAngle[i] = angle;
        MarkDirty(world, i);
        return true;
    }

    public static bool CanSow(WorldMap world, int i)
    {
        var g = (GroundType)world.Layers.Ground[i];
        return (g is GroundType.Cultivated or GroundType.Plowed) && world.Layers.Crop[i] == 0;
    }

    public static void Sow(WorldMap world, int i, int cropIndex, byte health, byte angle)
    {
        var L = world.Layers;
        L.Ground[i] = (byte)GroundType.Seeded;
        L.Crop[i] = (byte)(cropIndex + 1);
        L.Stage[i] = 0;
        L.Progress[i] = 0f;
        L.Chill[i] = 0;
        L.Health[i] = health;
        L.WorkAngle[i] = angle;
        MarkDirty(world, i);
    }

    /// <summary>After harvest (or clearing a dead crop): stubble, with a little nitrogen returned by residue.</summary>
    public static void ClearToStubble(WorldMap world, int i, byte angle)
    {
        var L = world.Layers;
        L.Ground[i] = (byte)GroundType.Stubble;
        ClearCrop(L, i);
        L.WorkAngle[i] = angle;
        L.Nitrogen[i] = (byte)Math.Min(255, L.Nitrogen[i] + 4);
        MarkDirty(world, i);
    }

    private static void ClearCrop(FieldLayers L, int i)
    {
        L.Crop[i] = 0;
        L.Stage[i] = 0;
        L.Progress[i] = 0f;
        L.Health[i] = 0;
        L.Chill[i] = 0;
    }

    private static void MarkDirty(WorldMap world, int i) => world.MarkCellDirty(i % world.CellsX, i / world.CellsX, crop: true);
}
