using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>
/// Sells its fill types to the machines parked in its trigger, with the use key (FS: buyingStation): seed at the farm
/// shop, diesel at the gas station, into any fill unit that takes them. Filling a motor's tank is refueling.
/// </summary>
public sealed class BuyingStationDef : StationDef, IPriced
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>What it sells: these fill types, and those of <see cref="FillTypeCategories"/> once linked.</summary>
    public string[] FillTypes { get; set; } = [];
    public string[] FillTypeCategories { get; set; } = [];
    /// <summary>Smallest amount it sells.</summary>
    public float MinAmount { get; set; }
    public float PriceFactor { get; set; } = 1f;
    public Dictionary<string, float> PriceFactors { get; set; } = new();

    public override string Verb => "sells";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("fill", Trigger)];

    internal override IEnumerable<string> FillTypesOf(PoiDef poi) => FillTypes;

    internal override void Link(ContentDatabase content) => FillTypes = content.WithCategories(FillTypes, FillTypeCategories);

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        foreach (var error in content.CategoryErrors(FillTypeCategories)) yield return error;
        if (FillTypes.Length == 0) yield return "needs fillTypes or fillTypeCategories";
        if (MinAmount < 0f) yield return "minAmount must be >= 0";
        foreach (var e in Priced.Errors(this, FillTypes)) yield return e;
    }

    internal override Component Create(Poi poi) => new BuyingStation(poi, this);
}

public sealed class BuyingStation(Poi poi, BuyingStationDef def) : PoiComponent<BuyingStationDef>(poi, def);
