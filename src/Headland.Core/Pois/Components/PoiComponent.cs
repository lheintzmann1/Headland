using System.Text.Json.Serialization;
using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

// The kinds of components only POIs have (FS: the placeable specializations): what a POI does for the machines at its
// triggers (selling and buying stations, a silo, a workshop…) or on its own (production). See Components/Component.cs.

/// <summary>A kind of component that goes on POIs only.</summary>
public abstract class PoiComponentDef : ComponentDef
{
    internal sealed override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content) =>
        owner is PoiDef poi ? Errors(poi, content) : ["goes on POIs only"];

    /// <summary>What's wrong with it, checked against the rest of its POI.</summary>
    internal virtual IEnumerable<string> Errors(PoiDef poi, ContentDatabase content) => [];

    /// <summary>The areas where machines use it, each with how (unload, load, fill, repair, wash, delivery).</summary>
    internal virtual IEnumerable<(string type, AreaDef area)> Triggers => [];

    internal sealed override Component Create(Entity owner) => Create((Poi)owner);

    internal abstract Component Create(Poi poi);

    /// <summary>What's wrong with its fill types, prices and trigger, and with <paramref name="conditions"/>.</summary>
    protected static IEnumerable<string> Check(IConditions conditions, ContentDatabase content, IEnumerable<string> fillTypes)
    {
        foreach (var ft in fillTypes.Where(f => !content.FillTypes.ContainsKey(f))) yield return $"unknown fill type '{ft}'";
        if (conditions.OpenHours is { } hours && MathUtil.HoursError(hours) is { } error) yield return $"openHours {error}";
        if (conditions.Months.Any(m => m is < 1 or > 12)) yield return "months must be 1..12";
    }

    /// <summary>What's wrong with a trigger.</summary>
    protected static IEnumerable<string> Check(string name, AreaDef? trigger)
    {
        if (trigger?.Error() is { } error) yield return $"{name}: {error}";
    }
}

/// <summary>A component of one POI.</summary>
public abstract class PoiComponent<TDef>(Poi poi, TDef def) : Component<TDef>(poi, def) where TDef : ComponentDef
{
    public Poi Poi { get; } = poi;

    /// <summary>Its trigger of <paramref name="type"/>, if it has one.</summary>
    public PoiTrigger? Trigger(string type) => Poi.Triggers.FirstOrDefault(t => t.Station == this && t.Type == type);
}

/// <summary>A component of one POI keeping state in saves, as a <typeparamref name="TSave"/>.</summary>
public abstract class PoiComponent<TDef, TSave>(Poi poi, TDef def) : Component<TDef, TSave>(poi, def)
    where TDef : ComponentDef where TSave : class, new()
{
    public Poi Poi { get; } = poi;

    /// <summary>Its trigger of <paramref name="type"/>, if it has one.</summary>
    public PoiTrigger? Trigger(string type) => Poi.Triggers.FirstOrDefault(t => t.Station == this && t.Type == type);
}

/// <summary>When something a POI does is available: its opening hours and months.</summary>
public interface IConditions
{
    /// <summary>Hours it is open, [from, to) in game hours (past midnight when from > to). Missing: always.</summary>
    float[]? OpenHours { get; }
    /// <summary>Months it works in (1..12). Empty: all year.</summary>
    int[] Months { get; }
    /// <summary>What it does, for "only in" messages: "buys", "washes".</summary>
    string Verb { get; }
}

/// <summary>
/// A POI component machines use at its trigger, open at some hours and months: selling and buying stations, silos,
/// workshops, washing stations, delivery spots.
/// </summary>
public abstract class StationDef : PoiComponentDef, IConditions
{
    public float[]? OpenHours { get; set; }
    public int[] Months { get; set; } = [];

    [JsonIgnore]
    public abstract string Verb { get; }

    internal sealed override IEnumerable<string> Errors(PoiDef poi, ContentDatabase content)
    {
        foreach (var e in Check(this, content, FillTypesOf(poi))) yield return e;
        foreach (var (type, area) in Triggers)
        foreach (var e in Check($"{type} trigger", area))
            yield return e;
        foreach (var e in StationErrors(poi, content)) yield return e;
    }

    /// <summary>The fill types it names (checked to exist).</summary>
    internal virtual IEnumerable<string> FillTypesOf(PoiDef poi) => [];

    /// <summary>What else is wrong with it.</summary>
    internal virtual IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content) => [];
}

/// <summary>Price settings: a factor on the market price (filltypes.json), and fill types whose factor differs.</summary>
public interface IPriced
{
    float PriceFactor { get; }
    Dictionary<string, float> PriceFactors { get; }
}

internal static class Priced
{
    public static float FactorOf(this IPriced p, string fillType) => p.PriceFactors.GetValueOrDefault(fillType, p.PriceFactor);

    /// <summary>What's wrong with its factors, which must be for fill types in <paramref name="traded"/>.</summary>
    public static IEnumerable<string> Errors(IPriced p, IEnumerable<string> traded)
    {
        if (p.PriceFactor <= 0f || p.PriceFactors.Values.Any(f => f <= 0f)) yield return "price factors must be > 0";
        foreach (var ft in p.PriceFactors.Keys.Except(traded)) yield return $"price factor for '{ft}', which it does not trade";
    }
}
