using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>
/// Demand at a selling station: the price of a fill type drops as loads of it come in and recovers day by day, and
/// now and then a fill type is in high demand for a few days.
/// </summary>
public sealed class DemandDef
{
    /// <summary>Price drop for each 100,000 units sold (0.04 = 4%), down to <see cref="Floor"/>.</summary>
    public float Drop { get; set; } = 0.04f;
    /// <summary>The lowest the demand factor goes.</summary>
    public float Floor { get; set; } = 0.7f;
    /// <summary>Demand factor regained per game day.</summary>
    public float Recovery { get; set; } = 0.02f;
    /// <summary>Chance per game day that one of the fill types goes in high demand (one at a time per station).</summary>
    public float HighChance { get; set; } = 0.03f;
    /// <summary>High demand: [min, max] price factor, and [min, max] game days it lasts.</summary>
    public float[] HighFactor { get; set; } = [1.2f, 1.5f];
    public int[] HighDays { get; set; } = [1, 3];
}

/// <summary>
/// Buys the loads of its fill types tipped or piped into its trigger (FS: sellingStation). A sale of a fill type the
/// POI's fill units keep goes into them, so a mill takes only what it has room to mill.
/// </summary>
public sealed class SellingStationDef : StationDef, IPriced
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>What it buys: these fill types, and those of <see cref="FillTypeCategories"/> once linked.</summary>
    public string[] FillTypes { get; set; } = [];
    public string[] FillTypeCategories { get; set; } = [];
    /// <summary>Smallest load it takes.</summary>
    public float MinAmount { get; set; }
    public float PriceFactor { get; set; } = 1f;
    public Dictionary<string, float> PriceFactors { get; set; } = new();
    /// <summary>How prices react to what farmers sell here.</summary>
    public DemandDef Demand { get; set; } = new();

    public override string Verb => "buys";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("unload", Trigger)];

    internal override IEnumerable<string> FillTypesOf(PoiDef poi) => FillTypes;

    internal override void Link(ContentDatabase content) => FillTypes = content.WithCategories(FillTypes, FillTypeCategories);

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        foreach (var error in content.CategoryErrors(FillTypeCategories)) yield return error;
        if (FillTypes.Length == 0) yield return "needs fillTypes or fillTypeCategories";
        if (MinAmount < 0f) yield return "minAmount must be >= 0";
        foreach (var e in Priced.Errors(this, FillTypes)) yield return e;
        var d = Demand;
        if (d.Drop < 0 || d.Floor is <= 0 or > 1 || d.Recovery < 0 || d.HighChance is < 0 or > 1)
            yield return "demand needs drop >= 0, floor in (0, 1], recovery >= 0 and highChance in [0, 1]";
        if (d.HighFactor is not [>= 1f, var fMax] || fMax < d.HighFactor[0] || d.HighDays is not [>= 1, var dMax] || dMax < d.HighDays[0])
            yield return "demand needs highFactor [min, max] >= 1 and highDays [min, max] >= 1";
    }

    internal override Component Create(Poi poi) => new SellingStation(poi, this);
}

/// <summary>A fill type a selling station pays more for until <paramref name="EndDay"/> (a day index).</summary>
public sealed record HighDemand(string FillType, float Factor, int EndDay);

public sealed class HighDemandSave
{
    public string FillType { get; set; } = "";
    public float Factor { get; set; }
    /// <summary>Day index it ends on, at midnight.</summary>
    public int EndDay { get; set; }
}

public sealed class SellingStationSave
{
    /// <summary>Demand factors below 1, by fill type.</summary>
    public Dictionary<string, float> Demand { get; set; } = new();
    public HighDemandSave? HighDemand { get; set; }
}

public sealed class SellingStation(Poi poi, SellingStationDef def) : PoiComponent<SellingStationDef, SellingStationSave>(poi, def)
{
    /// <summary>Demand factors below 1 after big sales, by fill type (they recover over time).</summary>
    internal Dictionary<string, float> Demand { get; } = new();

    /// <summary>The fill type in high demand here, if any.</summary>
    public HighDemand? HighDemand { get; internal set; }

    /// <summary>How much of the full price sales of <paramref name="fillType"/> get: 1, less after big sales.</summary>
    public float DemandOf(string fillType) => Demand.GetValueOrDefault(fillType, 1f);

    protected override SellingStationSave Capture(ContentDatabase content) => new()
    {
        Demand = new(Demand),
        HighDemand = HighDemand is { } h ? new HighDemandSave { FillType = h.FillType, Factor = h.Factor, EndDay = h.EndDay } : null,
    };

    protected override void Restore(SellingStationSave save, SaveContext context)
    {
        foreach (var (ft, demand) in save.Demand)
            if (context.Content.FillTypes.ContainsKey(ft)) Demand[ft] = Math.Clamp(demand, 0f, 1f);
        if (save.HighDemand is { } h && context.Content.FillTypes.ContainsKey(h.FillType)) HighDemand = new HighDemand(h.FillType, h.Factor, h.EndDay);
    }
}
