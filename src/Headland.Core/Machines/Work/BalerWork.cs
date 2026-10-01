using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Machines.Work;

/// <summary>
/// Baling: the pickup gathers what lies cut under it into the baler's chamber (<see cref="Baler"/>), which drops a bale
/// each time it's full. The chamber holds one fill type at a time: something else lying there is left until the bale is
/// dropped. What it picks up counts as harvested, so a bale job's contract can ask for the bales at a buyer.
/// </summary>
public sealed class BalerWork() : WorkType("baler", "Baled")
{
    public override bool Draft => false;
    public override bool Harvests => true;

    internal override IEnumerable<string> Errors(WorkAreaDef area, MachineDef machine, ContentDatabase content)
    {
        if (machine.Get<BalerDef>() == null) yield return "a baler's pickup needs a baler to fill";
    }

    /// <summary>A crop whose own produce lies cut in windrows (a meadow's grass): its contracts' goods are the bales.</summary>
    public override bool Handles(WorkAreaDef area, CropDef crop) => crop.Windrow?.FillType == crop.FillType;

    public override bool WouldChange(WorldMap world, ContentDatabase content, WorkAreaDef area, int i) => Windrows.Has(world.Layers, i);

    internal override int Work(WorkPass pass)
    {
        var baler = pass.Machine.Get<Baler>()!;
        var chamber = baler.Chamber;
        var L = pass.Layers;
        FillTypeDef? picked = null;
        var amount = 0f;
        var n = 0;
        foreach (var i in pass.Cells)
        {
            if (!Windrows.Has(L, i) || Windrows.FillTypeAt(pass.Content, L, i) is not { } ft || !chamber.Accepts(ft.Id)) continue;
            if (!chamber.IsEmpty && chamber.FillType != ft.Id)
            {
                baler.Refused = new BalerHolds(pass.Content.FillTypes[chamber.FillType!]);
                continue;
            }
            baler.Refused = null;
            // A full chamber drops its bale, and the pickup goes on.
            while (Windrows.Has(L, i))
            {
                var took = chamber.Add(ft.Id, Windrows.Take(pass.World, i, chamber.Free));
                amount += took;
                picked = ft;
                if (chamber.Free > 0.01f || baler.Drop(pass.Sim) == null) break;
            }
            n++;
        }
        if (picked != null) pass.Sim.Events.Publish(new WindrowPickedUp(pass.Machine, picked.Id, amount, pass.FieldId));
        return n;
    }
}
