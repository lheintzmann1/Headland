using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>
/// A lot where machines appear, facing the POI's front: those bought or leased at the shop when it <see cref="Sales"/>,
/// those leased for contracts (contracts.json) when it <see cref="Leases"/>.
/// </summary>
public sealed class DeliverySpotDef : StationDef
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>Machines bought or leased at the shop are delivered here (a dealer's lot).</summary>
    public bool Sales { get; set; }
    /// <summary>Machines leased for contracts are delivered here.</summary>
    public bool Leases { get; set; }

    public override string Verb => "delivers";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("delivery", Trigger)];

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        if (!Sales && !Leases) yield return "delivers nothing: needs sales or leases";
    }

    internal override Component Create(Poi poi) => new DeliverySpot(poi, this);
}

public sealed class DeliverySpot(Poi poi, DeliverySpotDef def) : PoiComponent<DeliverySpotDef>(poi, def)
{
    /// <summary>The area machines appear in.</summary>
    public PoiTrigger Lot => Trigger("delivery")!;
}
