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

public sealed class WashingStation(Poi poi, WashingStationDef def) : PoiComponent<WashingStationDef>(poi, def);
