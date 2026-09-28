using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>Washes the machine chain parked in its trigger, for its price for a fully dirty machine.</summary>
public sealed class WashingStationDef : StationDef
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>What washing a fully dirty machine costs.</summary>
    public float Price { get; set; }

    public override string Verb => "washes";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("wash", Trigger)];

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        if (Price < 0f) yield return "price must be >= 0";
    }

    internal override Component Create(Poi poi) => new WashingStation(poi, this);
}

public sealed class WashingStation(Poi poi, WashingStationDef def) : PoiComponent<WashingStationDef>(poi, def), IActivatable
{
    /// <summary>The use key washes the chain in its bay (driven in, or the farm's machines there when the farmer walks in).</summary>
    public IEnumerable<Activation> Activations(ActivationUser user, Simulation sim)
    {
        if (user.InBay(Trigger("wash")!.Area, sim.Machines.All) is not var (root, distance)) yield break;
        var chain = root.Chain().ToList();
        var dirty = chain.Any(m => m.Dirt > 0.005f);
        yield return new Activation(dirty ? $"Wash (${chain.Sum(m => sim.Pois.WashPrice(this, m)):N0})" : "Wash", this, distance)
        {
            Run = () => sim.Pois.Wash(chain, this),
            Blocked = sim.Pois.Closed(Poi, Def) ?? (dirty ? null : "Nothing to wash"),
        };
    }
}
