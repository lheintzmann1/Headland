using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Fertilizing: spreads the fertilizer in the area's fill unit at its rate, giving the soil the nitrogen it holds, once
/// each time it passes over. It counts toward the season's fertilizing (<see cref="FieldLayers.Fertilized"/>) until the
/// crop is harvested or cut.
/// </summary>
public sealed class SpreaderWork() : WorkType("spreader", "Fertilized")
{
    public override bool Draft => false;

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        foreach (var e in SpreadErrors(area, machine, "fertilizer")) yield return e;
        foreach (var ft in FillTypesOf(machine, area.FillUnit).Where(ft => content.FillTypes.TryGetValue(ft, out var def) && def.Nitrogen <= 0f))
            yield return $"a spreader spreads fertilizer: '{ft}' has no nitrogen";
    }

    /// <summary>Field ground not fertilized yet this season.</summary>
    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) =>
        world.IsWorkable((GroundType)world.Layers.Ground[i]) && world.Layers.Fertilized[i] == 0;

    internal override int Work(WorkPass pass)
    {
        var L = pass.Layers;
        var unit = pass.Machine.Unit(pass.Area.FillUnit)!;
        var n = 0;
        foreach (var i in pass.Cells)
        {
            if (!pass.World.IsWorkable((GroundType)L.Ground[i]) || !pass.Fresh(i)) continue;
            var fertilizer = unit.FillType;
            if (!pass.Use(pass.Area.RatePerHa)) break;
            Fertilize(L, i, pass.Area.RatePerHa * pass.Content.FillTypes[fertilizer!].Nitrogen);
            n++;
        }
        return n;
    }

    /// <summary>Gives cell <paramref name="i"/> <paramref name="nitrogen"/> kg/ha, and counts it fertilized.</summary>
    public static void Fertilize(FieldLayers L, int i, float nitrogen)
    {
        L.Nitrogen[i] = (byte)Math.Min(255, L.Nitrogen[i] + (int)MathF.Round(nitrogen));
        L.Fertilized[i] = (byte)Math.Min(255, L.Fertilized[i] + 1);
    }
}
