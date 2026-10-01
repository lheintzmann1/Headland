using System.Numerics;
using Headland.Core.Content;

namespace Headland.Core.World;

/// <summary>
/// What lies cut on the fields (FS: windrows): the grass a mower leaves in a swath, the straw a combine drops behind it,
/// the hay a tedder spread. A cell holds units of one fill type (<see cref="FieldLayers.Windrow"/>,
/// <see cref="FieldLayers.WindrowFill"/>): tedders spread it and turn grass into hay, rakes gather it into windrows,
/// balers pick it up, and tilling works it into the ground.
/// </summary>
public static class Windrows
{
    /// <summary>Less than this in a cell counts as nothing: the cell is cleared.</summary>
    public const float Trace = 0.05f;

    /// <summary>Whether something lies on cell <paramref name="i"/>.</summary>
    public static bool Has(FieldLayers L, int i) => L.WindrowFill[i] != 0 && L.Windrow[i] >= Trace;

    /// <summary>What a cell stores for <paramref name="fillType"/>: its index in the fill type list, plus one.</summary>
    public static byte FillOf(ContentDatabase content, string fillType) => (byte)(content.FillTypeIndex(fillType) + 1);

    /// <summary>The fill type lying on cell <paramref name="i"/>, if any.</summary>
    public static FillTypeDef? FillTypeAt(ContentDatabase content, FieldLayers L, int i) =>
        L.WindrowFill[i] is var f and > 0 && f <= content.FillTypeList.Count ? content.FillTypeList[f - 1] : null;

    /// <summary>Puts <paramref name="amount"/> of <paramref name="fill"/> on cell <paramref name="i"/>, onto what lies there of it; another fill type lying there is lost.</summary>
    public static void Add(WorldMap world, int i, byte fill, float amount)
    {
        if (amount <= 0f || fill == 0) return;
        var L = world.Layers;
        if (L.WindrowFill[i] != fill) L.Windrow[i] = 0f;
        L.WindrowFill[i] = fill;
        L.Windrow[i] += amount;
        world.MarkWindrowDirty(i);
    }

    /// <summary>Puts <paramref name="amount"/> of <paramref name="fill"/> where <paramref name="p"/> is, on the map.</summary>
    public static void Drop(WorldMap world, Vector2 p, byte fill, float amount)
    {
        var (cx, cz) = world.WorldToCell(p);
        if (world.InBounds(cx, cz)) Add(world, world.CellIndex(cx, cz), fill, amount);
    }

    /// <summary>Takes up to <paramref name="amount"/> off cell <paramref name="i"/>; returns what it took (a trace left over is lost).</summary>
    public static float Take(WorldMap world, int i, float amount)
    {
        var L = world.Layers;
        var taken = MathF.Min(amount, L.Windrow[i]);
        L.Windrow[i] -= taken;
        if (L.Windrow[i] < Trace) Clear(L, i);
        world.MarkWindrowDirty(i);
        return taken;
    }

    /// <summary>Takes everything off cell <paramref name="i"/> (worked into the ground).</summary>
    public static void Clear(WorldMap world, int i)
    {
        if (world.Layers.WindrowFill[i] == 0) return;
        Clear(world.Layers, i);
        world.MarkWindrowDirty(i);
    }

    internal static void Clear(FieldLayers L, int i)
    {
        L.Windrow[i] = 0f;
        L.WindrowFill[i] = 0;
    }

    /// <summary>
    /// Where something lying <paramref name="x"/> across from the middle of a <paramref name="width"/> wide machine goes in
    /// the windrow of <paramref name="windrowWidth"/> in its middle (FS: a swath): within the windrow, it stays; outside,
    /// it's drawn in on its own side, the band outside squeezed onto that half of the windrow.
    /// </summary>
    public static float Gather(float x, float width, float windrowWidth)
    {
        var (half, w) = (width * 0.5f, windrowWidth * 0.5f);
        if (windrowWidth <= 0f || w >= half || MathF.Abs(x) <= w) return x;
        return MathF.Sign(x) * w * MathF.Min(1f, (MathF.Abs(x) - w) / (half - w));
    }
}
