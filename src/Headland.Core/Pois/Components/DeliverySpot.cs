using Headland.Core.Components;

namespace Headland.Core.Pois.Components;

/// <summary>Where new machines appear, or those leased for contracts (contracts.json) when it <see cref="Leases"/>.</summary>
public sealed class DeliverySpotDef : StationDef
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>Machines leased for contracts are delivered here.</summary>
    public bool Leases { get; set; }

    public override string Verb => "delivers";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("delivery", Trigger)];

    internal override Component Create(Poi poi) => new DeliverySpot(poi, this);
}

public sealed class DeliverySpot(Poi poi, DeliverySpotDef def) : PoiComponent<DeliverySpotDef>(poi, def)
{
    /// <summary>The area machines appear in.</summary>
    public PoiTrigger Lot => Trigger("delivery")!;
}
