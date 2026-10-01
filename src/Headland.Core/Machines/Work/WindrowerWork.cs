using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Raking (FS: a windrower): gathers what lies cut under it into a windrow in its middle, its
/// <see cref="WorkAreaDef.WindrowWidth"/>, for a baler to pick up. The ground is raked once as the rake reaches it.
/// </summary>
public sealed class WindrowerWork() : WorkType("windrower")
{
    public override bool Draft => false;

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        if (area.WindrowWidth <= 0f || area.WindrowWidth >= area.Width) yield return "a windrower needs a windrowWidth narrower than its width";
    }

    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => Windrows.Has(world.Layers, i);

    internal override int Work(WorkPass pass)
    {
        var L = pass.Layers;
        var moves = new List<(int from, byte fill, float amount)>();
        foreach (var i in pass.Cells)
        {
            if (!pass.Fresh(i) || !Windrows.Has(L, i)) continue;
            var x = pass.InArea(i).X;
            if (Windrows.Gather(x, pass.Area.Width, pass.Area.WindrowWidth) == x) continue;
            moves.Add((i, L.WindrowFill[i], L.Windrow[i]));
        }
        // All taken up first, then laid down: what lands on a cell raked this tick stays there.
        foreach (var (i, _, _) in moves) Windrows.Clear(pass.World, i);
        foreach (var (i, fill, amount) in moves) pass.Swath(i, fill, amount);
        return moves.Count;
    }
}
