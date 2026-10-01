using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Tedding: spreads what lies cut under it evenly across its width, turning it into what it dries into (the fill type's
/// <see cref="FillTypeDef.Tedded"/>: grass into hay), at once as FS's tedder does. Something else lying among it (straw
/// on a meadow) is left where it is.
/// </summary>
public sealed class TedderWork() : WorkType("tedder", "Tedded")
{
    public override bool Draft => false;

    /// <summary>Where something lies that it dries: helpers ted the lanes with grass on them.</summary>
    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) =>
        Windrows.Has(world.Layers, i) && Windrows.FillTypeAt(content, world.Layers, i)?.Tedded != null;

    internal override int Work(WorkPass pass)
    {
        var L = pass.Layers;
        var amounts = new Dictionary<byte, float>();
        foreach (var i in pass.Cells)
        {
            if (!Windrows.Has(L, i)) continue;
            var (fill, amount) = Tedded(pass.Content, L, i);
            amounts[fill] = amounts.GetValueOrDefault(fill) + amount;
        }
        if (amounts.Count == 0) return 0;
        // The most of one fill type is spread over the cells under it that hold it or nothing.
        var (spread, total) = amounts.MaxBy(kv => kv.Value);
        var cells = pass.Cells.Where(i => !Windrows.Has(L, i) || Tedded(pass.Content, L, i).fill == spread).ToList();
        var each = total / cells.Count;
        var n = 0;
        foreach (var i in cells)
        {
            var changed = L.WindrowFill[i] != spread || MathF.Abs(L.Windrow[i] - each) >= Windrows.Trace;
            Windrows.Clear(pass.World, i);
            if (each >= Windrows.Trace) Windrows.Add(pass.World, i, spread, each);
            // Only ground it reaches this tick counts as tedded, not what it spreads over again.
            if (changed && pass.Fresh(i)) n++;
        }
        return n;
    }

    /// <summary>What cell <paramref name="i"/> holds once tedded: its fill, or what that dries into, and how much.</summary>
    private static (byte fill, float amount) Tedded(ContentDatabase content, FieldLayers L, int i)
    {
        var ft = Windrows.FillTypeAt(content, L, i);
        return ft?.Tedded is { } to ? (Windrows.FillOf(content, to.FillType), L.Windrow[i] * to.Factor) : (L.WindrowFill[i], L.Windrow[i]);
    }
}
