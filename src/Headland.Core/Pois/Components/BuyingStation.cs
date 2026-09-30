using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;

namespace Headland.Core.Pois.Components;

/// <summary>
/// Sells its fill types to the machines parked in its trigger, with the use key (FS: buyingStation): seed at the farm
/// shop, diesel at the gas station, into any fill unit that takes them. Filling a motor's tank is refueling. One that
/// serves <see cref="FromStorage"/> (a farm's fuel tank) gives its owner's machines what the POI keeps, and its owner
/// orders more in bulk, on foot at its trigger.
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
    /// <summary>
    /// It serves from the POI's fill units, its owner's machines only and for nothing: what they keep was paid for when
    /// ordered (a farm's fuel tank). Its prices are then those of the orders.
    /// </summary>
    public bool FromStorage { get; set; }
    /// <summary>Serving from storage: how much its owner orders at a time (0: no orders), on foot at its trigger.</summary>
    public float OrderAmount { get; set; }

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
        if (OrderAmount < 0f || OrderAmount > 0f && !FromStorage) yield return "orderAmount is for a station serving from storage, and >= 0";
        if (FromStorage && poi.Get<FillUnitsDef>() is var units && FillTypes.FirstOrDefault(ft => units?.Units.Any(u => u.FillTypes.Contains(ft)) != true) is { } missing)
            yield return $"serving from storage, it needs fill units keeping {missing}";
    }

    internal override Component Create(Poi poi) => new BuyingStation(poi, this);
}

public sealed class BuyingStation(Poi poi, BuyingStationDef def) : PoiComponent<BuyingStationDef>(poi, def), IActivatable
{
    /// <summary>What the POI keeps for it to serve (<see cref="BuyingStationDef.FromStorage"/>).</summary>
    public FillUnits Storage => Poi.Get<FillUnits>()!;

    /// <summary>
    /// The use key refuels the machines of the chain parked in its trigger and buys what their other units take; serving
    /// from storage, it fills them from it, and on foot it orders more.
    /// </summary>
    public IEnumerable<Activation> Activations(ActivationUser user, Simulation sim)
    {
        var area = Trigger("fill")!.Area;
        if (Def.FromStorage)
        {
            foreach (var a in StorageActivations(user, sim, area)) yield return a;
            yield break;
        }
        var parked = user.In(area);
        if (parked.Count == 0) yield break;
        var wants = parked.SelectMany(m => sim.Pois.Wants(m, this)).Distinct().ToList();
        yield return new Activation(wants.Count > 0 ? Activation.Join(wants) : "Buy", this, user.Distance(area, parked))
        {
            Run = () => sim.Pois.Buy(parked, this),
            Blocked = sim.Pois.Closed(Poi, Def) ?? (wants.Count == 0 ? $"{Poi.Name} sells nothing the {parked[0].Def.Name} takes" : null),
        };
    }

    /// <summary>Serving from storage: on foot, an order of each of its fill types; parked, filling up from what it keeps.</summary>
    private IEnumerable<Activation> StorageActivations(ActivationUser user, Simulation sim, Obb area)
    {
        var other = user.FarmId != Poi.FarmId ? $"{Poi.Name} belongs to another farm" : null;
        if (user.Vehicle == null)
        {
            if (Def.OrderAmount <= 0f || !area.Contains(user.Position)) yield break;
            foreach (var ft in Def.FillTypes)
            {
                var (amount, cost) = sim.Pois.OrderOf(this, ft);
                var unit = sim.Content.FillTypes[ft].Unit;
                yield return new Activation($"Order {Def.OrderAmount:N0} {unit} of {sim.Content.FillTypes[ft].Name.ToLowerInvariant()} (${cost:N0})", this,
                    user.Distance(area, []))
                {
                    Run = () => sim.Pois.Order(this, ft),
                    Blocked = other ?? sim.Pois.Closed(Poi, Def) ?? (amount < 1f ? $"{Poi.Name} is full" : null),
                };
            }
            yield break;
        }
        var parked = user.In(area);
        if (parked.Count == 0) yield break;
        var wants = parked.SelectMany(m => sim.Pois.Wants(m, this)).Distinct().ToList();
        var kept = string.Join(", ", Def.FillTypes.Select(ft => $"{Storage.Level(ft):N0} {sim.Content.FillTypes[ft].Unit}"));
        yield return new Activation($"{(wants.Count > 0 ? Activation.Join(wants) : "Fill up")} ({kept} in {Poi.Name.ToLowerInvariant()})", this, user.Distance(area, parked))
        {
            Run = () => sim.Pois.FillFromStorage(parked, this),
            Blocked = other ?? sim.Pois.Closed(Poi, Def) ?? (wants.Count == 0 ? $"{Poi.Name} keeps nothing the {parked[0].Def.Name} takes"
                : Def.FillTypes.All(ft => Storage.Level(ft) < 1f) ? $"{Poi.Name} is empty" : null),
        };
    }
}
