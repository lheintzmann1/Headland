using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Objects;
using Headland.Core.Pois.Components;
using Headland.Core.World;

namespace Headland.Core.Pois;

/// <summary>
/// A load being unloaded at a POI, totalled until the machine stops: sold, stored by the POI's owner, or taken for a
/// contract.
/// </summary>
internal sealed record Delivery(Poi Poi, string FillType, float Amount, float Income, bool Stored, Contract? Contract = null);

/// <summary>A machine filling up from a silo at its loading spout, with what it took so far.</summary>
internal sealed record Loading(Silo Silo, string FillType, float Amount);

/// <summary>
/// What POIs do, through their components. Loads tipped or piped into a selling station's trigger are sold, into a
/// silo's pit stored; the owner's trailers fill up at a silo's spout; machines parked at a buying station buy supplies
/// and fuel, at a workshop get repaired (or their options changed), at a washing station washed; production points
/// turn stored goods into others every hour; machines from the shop appear on a dealer's lot. Machines move the goods (tipping,
/// piping); the POI's components decide what happens to them, and offer the use key what it does in their triggers
/// (<see cref="IActivatable"/>).
/// </summary>
public sealed class PoiSystem
{
    private readonly Simulation _sim;
    private readonly Dictionary<Machine, Delivery> _deliveries = new();
    private readonly HashSet<Machine> _unloading = [];
    private readonly Dictionary<Machine, Loading> _loading = new();
    private HashSet<string>? _fuels;
    private float _sinceObjects;

    public PoiSystem(Simulation sim, ulong seed)
    {
        _sim = sim;
        Rng = new Rng(seed);
    }

    /// <summary>Rolls high-demand events.</summary>
    internal Rng Rng { get; }

    public IReadOnlyList<Poi> All => _sim.World.Pois;

    /// <summary>Loads being unloaded, by machine: published as one sale or delivery when the machine stops.</summary>
    internal Dictionary<Machine, Delivery> Deliveries => _deliveries;
    /// <summary>Machines being loaded at a silo's spout.</summary>
    internal Dictionary<Machine, Loading> Loadings => _loading;

    private ContentDatabase Content => _sim.Content;
    private Economy Economy => _sim.Economy;

    public Poi? ById(string id) => _sim.World.PoiById(id);

    /// <summary>The first trigger of <paramref name="type"/> covering <paramref name="p"/>.</summary>
    public PoiTrigger? TriggerAt(Vector2 p, string type)
    {
        foreach (var poi in All)
        foreach (var t in poi.Triggers)
            if (t.Type == type && t.Contains(p)) return t;
        return null;
    }

    /// <summary>What a unit of <paramref name="fillType"/> sells for at <paramref name="station"/> now: the market price times its factor, the demand and any high demand.</summary>
    public float Price(SellingStation station, string fillType)
    {
        var price = Economy.Price(fillType, _sim.Clock.Month) * station.Def.FactorOf(fillType) * station.DemandOf(fillType);
        return station.HighDemand is { } high && high.FillType == fillType ? price * high.Factor : price;
    }

    /// <summary>What a unit of <paramref name="fillType"/> costs at <paramref name="station"/> now: the market price times its factor and the price level.</summary>
    public float Price(BuyingStation station, string fillType) =>
        Economy.Price(fillType, _sim.Clock.Month) * station.Def.FactorOf(fillType) * Economy.PriceLevel;

    /// <summary>What <paramref name="m"/>'s load of <paramref name="fillType"/> sells for at <paramref name="trigger"/> per unit, or null if it doesn't sell there.</summary>
    public float? SalePrice(Machine m, PoiTrigger trigger, string fillType) =>
        trigger.Station is SellingStation s && s.Def.FillTypes.Contains(fillType) && Closed(s.Poi, s.Def) == null ? Price(s, fillType) : null;

    /// <summary>What repainting a machine whose paint is worn through costs, as a share of its price.</summary>
    public const float RepaintShare = 0.02f;

    /// <summary>
    /// What repairing <paramref name="m"/> costs: 1% of its price for each 100% of wear, times the workshop's factor,
    /// the price level and <paramref name="factor"/> (more where it stands than in the workshop's bay).
    /// </summary>
    public float RepairPrice(Workshop workshop, Machine m, float factor = 1f) =>
        factor * workshop.Def.RepairPriceFactor * m.Def.Price / 100f * (m.Get<Wearable>()?.Wear ?? 0f) * Economy.PriceLevel;

    /// <summary>What repainting <paramref name="m"/> costs: <see cref="RepaintShare"/> of its price for paint worn through, as repairs.</summary>
    public float RepaintPrice(Workshop workshop, Machine m, float factor = 1f) =>
        factor * workshop.Def.RepairPriceFactor * m.Def.Price * RepaintShare * (m.Get<Wearable>()?.PaintWear ?? 0f) * Economy.PriceLevel;

    public float WashPrice(WashingStation station, Machine m) => station.Def.Price * m.Dirt * Economy.PriceLevel;

    /// <summary>Fill types machines burn: those their motor's fuel unit takes. Buying them is refueling.</summary>
    private bool IsFuel(string fillType) => (_fuels ??= Content.Machines.Values
        .SelectMany(m => m.Get<MotorDef>()?.FuelUnit is { } unit ? m.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == unit)?.FillTypes ?? [] : [])
        .ToHashSet()).Contains(fillType);

    /// <summary>What the farmer can do at a trigger, one line per thing ("Sell wheat, barley").</summary>
    public IEnumerable<string> Describe(PoiTrigger trigger) => trigger.Station switch
    {
        Silo s when trigger.Type == "load" => [With(s.Poi, s.Def, $"Load {Names(s.FillTypes)}")],
        Silo s => [With(s.Poi, s.Def, $"Store {Names(s.FillTypes)}", s.Def.MinAmount, s.FillTypes)],
        SellingStation s when trigger.Type == "objects" => [With(s.Poi, s.Def, $"Sell {Names(s.Def.FillTypes)}: leave {Carriers(s.Def.FillTypes)} here")],
        SellingStation s => [With(s.Poi, s.Def, $"Sell {Names(s.Def.FillTypes)}", s.Def.MinAmount, s.Def.FillTypes)],
        BuyingStation { Def.FromStorage: true } b => new[]
            {
                $"Fill up with {Names(b.Def.FillTypes)} from {b.Poi.Name.ToLowerInvariant()}",
                b.Def.OrderAmount > 0f ? $"Order {Names(b.Def.FillTypes)} here, on foot" : null,
            }
            .OfType<string>()
            .Select(text => With(b.Poi, b.Def, text)),
        BuyingStation b => new[]
            {
                (fillTypes: b.Def.FillTypes.Where(IsFuel).ToList(), what: "Refuel with"),
                (fillTypes: b.Def.FillTypes.Where(f => !IsFuel(f)).ToList(), what: "Buy"),
            }
            .Where(x => x.fillTypes.Count > 0)
            .Select(x => With(b.Poi, b.Def, $"{x.what} {Names(x.fillTypes)}", b.Def.MinAmount, x.fillTypes)),
        Workshop w => w.Def.Configure != null
            ? [With(w.Poi, w.Def, "Repair machines"), With(w.Poi, w.Def, "Change machines' options")]
            : [With(w.Poi, w.Def, "Repair machines")],
        WashingStation w => [With(w.Poi, w.Def, "Wash machines")],
        ProductionPoint p when trigger.Type == "pallets" => [OnPallets(p)],
        DeliverySpot d => new[] { (d.Def.Sales, "Machines bought or leased at the shop wait here"), (d.Def.Leases, "Machines leased for contracts wait here") }
            .Where(x => x.Item1)
            .Select(x => With(d.Poi, d.Def, x.Item2)),
        _ => [],
    };

    internal string Names(IEnumerable<string> fillTypes) =>
        string.Join(", ", fillTypes.Select(f => Content.FillTypes[f].Name.ToLowerInvariant()));

    /// <summary>What a production point's pallet area is for: "Flour comes out here on pallets".</summary>
    private string OnPallets(ProductionPoint p)
    {
        var made = p.Def.Productions.SelectMany(x => x.Outputs).Where(o => o.Mode == "pallet").Select(o => Content.FillTypes[o.FillType].Name).Distinct().ToList();
        return $"{string.Join(", ", made)} {(made.Count == 1 ? "comes" : "come")} out here on pallets";
    }

    /// <summary>The objects that can hold some of <paramref name="fillTypes"/>, by name: "round bales", "pallets".</summary>
    private string Carriers(string[] fillTypes)
    {
        var names = Content.Objects.Values
            .Where(o => o.Get<FillUnitsDef>()?.Units.Any(u => u.FillTypes.Any(fillTypes.Contains)) == true)
            .Select(o => $"{o.Name.ToLowerInvariant()}s")
            .ToList();
        return names.Count > 0 ? string.Join(" or ", names) : "them";
    }

    // ------------------------------------------------------------------ Conditions

    /// <summary>Why <paramref name="what"/> (a station, a production) is not available now (closed, out of season), or null when it is.</summary>
    public string? Closed(Poi poi, IConditions what) => Closed(poi, what, _sim.Clock.HourOfDay, _sim.Clock.Month);

    private static string? Closed(Poi poi, IConditions what, float hour, int month)
    {
        if (what.Months.Length > 0 && !what.Months.Contains(month))
            return $"{poi.Name} {what.Verb} only in {MachineSystem.Months(what.Months)}";
        if (what.OpenHours is [var from, var to] && !MathUtil.InHours(hour, from, to))
            return $"{poi.Name} is closed: open {Hours(from, to)}";
        return null;
    }

    private static string Hours(float from, float to) => $"{Clock(from)}–{Clock(to)}";

    private static string Clock(float hour) => $"{(int)hour}:{(int)MathF.Round((hour - (int)hour) * 60f):00}";

    /// <summary>A label with when and how much: "Buy seeds (7:00–19:00, from 500 L)".</summary>
    private string With(Poi poi, IConditions c, string what, float minAmount = 0f, IEnumerable<string>? fillTypes = null)
    {
        var parts = new List<string>();
        if (c.OpenHours is [var from, var to]) parts.Add(Hours(from, to));
        if (c.Months.Length > 0) parts.Add(MachineSystem.Months(c.Months));
        if (minAmount > 0f) parts.Add($"from {minAmount:N0} {(fillTypes?.FirstOrDefault() is { } ft ? Content.FillTypes[ft].Unit : "units")}");
        return parts.Count > 0 ? $"{what} ({string.Join(", ", parts)})" : what;
    }

    // ------------------------------------------------------------------ Unloading

    /// <summary>The fill units a load unloaded at <paramref name="trigger"/> goes into: a silo's, or the POI's when they keep it; else away to the market.</summary>
    private static FillUnits? Destination(PoiTrigger trigger, string fillType) => trigger.Station switch
    {
        Silo s => s.Storage,
        SellingStation s when s.Poi.Get<FillUnits>() is { } units && units.Keeps(fillType) => units,
        _ => null,
    };

    /// <summary>
    /// Why <paramref name="m"/>, carrying <paramref name="load"/> units of <paramref name="fillType"/>, can't unload
    /// at <paramref name="trigger"/>, or null if it can: the fill type isn't taken, it's another farm's silo, it's
    /// closed, the storage is full or the load is under the minimum.
    /// </summary>
    public string? UnloadBlocker(Machine m, PoiTrigger trigger, string fillType, float load)
    {
        var poi = trigger.Poi;
        var ft = Content.FillTypes[fillType];
        StationDef station;
        float minAmount;
        switch (trigger.Station)
        {
            case Silo silo:
                if (!silo.FillTypes.Contains(fillType)) return $"{poi.Name} does not store {ft.Name}";
                if (m.FarmId != poi.FarmId) return $"{poi.Name} belongs to another farm";
                (station, minAmount) = (silo.Def, silo.Def.MinAmount);
                break;
            case SellingStation sell:
                if (!sell.Def.FillTypes.Contains(fillType)) return $"{poi.Name} does not buy {ft.Name}";
                (station, minAmount) = (sell.Def, sell.Def.MinAmount);
                break;
            default:
                return $"{poi.Name} does not buy {ft.Name}";
        }
        if (Closed(poi, station) is { } closed) return closed;
        if (Destination(trigger, fillType) is { } storage && storage.Free(fillType) < 1f) return $"{poi.Name} has no room for {ft.Name}";
        // A load under way may finish below the minimum.
        var underWay = _deliveries.TryGetValue(m, out var d) && d.Poi == poi && d.FillType == fillType;
        if (load < minAmount && !underWay) return $"{poi.Name} takes loads of {minAmount:N0} {ft.Unit} or more";
        return null;
    }

    /// <summary>
    /// Takes up to <paramref name="amount"/> from <paramref name="m"/> unloading at <paramref name="trigger"/>; returns
    /// what it took. The machine checks <see cref="UnloadBlocker"/> with its whole load first. Goods a contract of the
    /// machine's farm is owed here go to the contract, unpaid; the rest is sold or stored.
    /// </summary>
    public float Unload(Machine m, PoiTrigger trigger, string fillType, float amount)
    {
        if (amount <= 0f || UnloadBlocker(m, trigger, fillType, float.PositiveInfinity) != null) return 0f;
        var poi = trigger.Poi;
        if (Destination(trigger, fillType) is { } storage) amount = storage.Add(fillType, amount);
        if (amount <= 0f) return 0f;
        if (trigger.Station is not SellingStation sell)
        {
            Record(m, new Delivery(poi, fillType, amount, 0f, true));
            return amount;
        }
        var (contract, credited) = _sim.Contracts.Credit(m.FarmId, poi, fillType, amount);
        if (credited > 0f) Record(m, new Delivery(poi, fillType, credited, 0f, false, contract));
        if (amount - credited is var rest and > 0f)
        {
            var income = rest * Price(sell, fillType);
            Earn(m.FarmId, income, MoneyCategory.Sales);
            Record(m, new Delivery(poi, fillType, rest, income, false));
        }
        var demand = sell.Def.Demand;
        sell.Demand[fillType] = MathF.Max(demand.Floor, sell.DemandOf(fillType) - demand.Drop * amount / 100_000f);
        return amount;
    }

    /// <summary>Adds to the machine's load under way, first publishing the one before when it was a different one.</summary>
    private void Record(Machine m, Delivery add)
    {
        if (_deliveries.TryGetValue(m, out var d) && (d.Poi, d.FillType, d.Stored, d.Contract) != (add.Poi, add.FillType, add.Stored, add.Contract)) Flush(m);
        _deliveries[m] = _deliveries.TryGetValue(m, out d) ? d with { Amount = d.Amount + add.Amount, Income = d.Income + add.Income } : add;
        _unloading.Add(m);
    }

    /// <summary>
    /// After the machines moved: the POIs' components run, machines that stopped unloading this tick publish their
    /// sale or delivery, and loading trailers fill up.
    /// </summary>
    internal void Update(float dt)
    {
        foreach (var poi in All)
        foreach (var c in poi.Components)
            c.Update(_sim, dt);
        foreach (var m in _deliveries.Keys.Where(m => !_unloading.Contains(m)).ToList()) Flush(m);
        _unloading.Clear();
        UpdateLoading(dt);
        SellObjects(dt);
    }

    // ------------------------------------------------------------------ Objects

    /// <summary>Real seconds between looks at what lies in the object triggers.</summary>
    private const float ObjectCheckSeconds = 0.5f;

    /// <summary>
    /// Objects left lying in an open selling station's object trigger (bales set down there), holding what it buys, are
    /// sold together, by farm and fill type: what a contract of the farm is owed here goes to the contract, unpaid.
    /// </summary>
    private void SellObjects(float dt)
    {
        _sinceObjects += dt;
        if (_sinceObjects < ObjectCheckSeconds) return;
        _sinceObjects = 0f;
        foreach (var poi in All)
        {
            if (poi.Get<SellingStation>() is not { } sell || sell.Trigger("objects") is not { } trigger || Closed(poi, sell.Def) != null) continue;
            var lying = _sim.Objects.LooseIn(trigger.Area)
                .Where(o => o.Content is { IsEmpty: false } c && sell.Def.FillTypes.Contains(c.FillType!))
                .GroupBy(o => (o.FarmId, o.Def, fillType: o.Content!.FillType!))
                .ToList();
            foreach (var group in lying) SellObjects(sell, group.Key.FarmId, group.Key.fillType, group.ToList());
        }
    }

    private void SellObjects(SellingStation sell, int farmId, string fillType, List<WorldObject> objects)
    {
        var poi = sell.Poi;
        var amount = objects.Sum(o => o.Content!.Level);
        foreach (var o in objects) _sim.Objects.Remove(o);
        var (contract, credited) = _sim.Contracts.Credit(farmId, poi, fillType, amount);
        if (credited > 0f) _sim.Events.Publish(new ContractDelivery(contract!, null, poi, fillType, credited));
        if (amount - credited is var rest and >= 1f)
        {
            var income = rest * Price(sell, fillType);
            Earn(farmId, income, MoneyCategory.Sales);
            _sim.Events.Publish(new ObjectsSold(poi, farmId, objects[0].Def, fillType, objects.Count, rest, income));
        }
        var demand = sell.Def.Demand;
        sell.Demand[fillType] = MathF.Max(demand.Floor, sell.DemandOf(fillType) - demand.Drop * amount / 100_000f);
    }

    /// <summary>A machine leaving the map: its load under way is published, and its loading stops.</summary>
    internal void Forget(Machine m)
    {
        Flush(m);
        _unloading.Remove(m);
        FinishLoading(m);
    }

    private void Flush(Machine m)
    {
        if (!_deliveries.Remove(m, out var d) || d.Amount < 1f) return;
        if (d.Contract != null) _sim.Events.Publish(new ContractDelivery(d.Contract, m, d.Poi, d.FillType, d.Amount));
        else if (d.Stored) _sim.Events.Publish(new FillStored(m, d.Poi, d.FillType, d.Amount));
        else _sim.Events.Publish(new FillSold(m, d.Poi, d.FillType, d.Amount, d.Income));
    }

    // ------------------------------------------------------------------ Loading

    /// <summary>Machines of the chain standing under a silo's spout, with that silo.</summary>
    private IEnumerable<(Machine m, Silo silo)> AtSpouts(Machine vehicle)
    {
        foreach (var m in vehicle.Chain())
            if (TriggerAt(m.Footprint.Center, "load")?.Station is Silo silo) yield return (m, silo);
    }

    /// <summary>Fill units that take loads (not fuel tanks).</summary>
    private static IEnumerable<FillUnit> Cargo(Machine m) => m.FillUnits.Where(u => u != m.Get<Motor>()?.FuelTank);

    /// <summary>The <see cref="Cargo"/> units a load can go into now: those no closed cover keeps it out of.</summary>
    private static IEnumerable<FillUnit> Open(Machine m) => Cargo(m).Where(u => !m.ClosedOver(u));

    /// <summary>
    /// Whether <paramref name="m"/> stands at a fill trigger that could fill <paramref name="unit"/> (FS: fill triggers):
    /// a station selling what it takes, or its farm's silo spout loading what it takes.
    /// </summary>
    public bool AtFillTrigger(Machine m, FillUnit unit)
    {
        var at = m.Footprint.Center;
        if (TriggerAt(at, "fill")?.Station is BuyingStation shop && shop.Def.FillTypes.Any(unit.Accepts)) return true;
        return TriggerAt(at, "load")?.Station is Silo silo && silo.Poi.FarmId == m.FarmId && silo.FillTypes.Any(unit.Accepts);
    }

    /// <summary>What a silo holds that it loads, by fill type.</summary>
    private static IEnumerable<(string fillType, float stock)> Stock(Silo silo) =>
        silo.FillTypes.Select(ft => (ft, stock: silo.Storage.Level(ft))).Where(x => x.stock >= 1f);

    /// <summary>What the vehicle's chain can load where it stands: what it already carries first, then by stock.</summary>
    public IReadOnlyList<string> LoadChoices(Machine vehicle)
    {
        var choices = new List<(string fillType, float stock, bool carried)>();
        foreach (var (m, silo) in AtSpouts(vehicle))
        {
            if (m.FarmId != silo.Poi.FarmId || Closed(silo.Poi, silo.Def) != null) continue;
            foreach (var (ft, stock) in Stock(silo))
                if (Open(m).Any(u => u.CanAccept(ft)))
                    choices.Add((ft, stock, Open(m).Any(u => u.FillType == ft)));
        }
        return choices.OrderByDescending(c => c.carried).ThenByDescending(c => c.stock).Select(c => c.fillType).Distinct().ToList();
    }

    /// <summary>The silo the vehicle's chain loads from where it stands: that of the first spout it's under.</summary>
    public Silo? LoadingSilo(Machine vehicle) => AtSpouts(vehicle).Select(x => x.silo).FirstOrDefault();

    /// <summary>True while some machine of the chain is being loaded.</summary>
    public bool IsLoading(Machine vehicle) => vehicle.Chain().Any(_loading.ContainsKey);

    /// <summary>What <paramref name="m"/> itself is being loaded with, or null.</summary>
    public string? LoadingFillType(Machine m) => _loading.GetValueOrDefault(m)?.FillType;

    /// <summary>Starts loading <paramref name="fillType"/> into every machine of the chain under a spout that takes it.</summary>
    public bool StartLoading(Machine vehicle, string fillType)
    {
        var started = false;
        foreach (var (m, silo) in AtSpouts(vehicle))
        {
            if (_loading.ContainsKey(m) || m.FarmId != silo.Poi.FarmId || !silo.FillTypes.Contains(fillType) || silo.Storage.Level(fillType) < 1f) continue;
            if (Closed(silo.Poi, silo.Def) != null || !Open(m).Any(u => u.CanAccept(fillType))) continue;
            _loading[m] = new Loading(silo, fillType, 0f);
            started = true;
        }
        return started;
    }

    public void StopLoading(Machine vehicle)
    {
        foreach (var m in vehicle.Chain().Where(_loading.ContainsKey).ToList()) FinishLoading(m);
    }

    /// <summary>Why nothing can be loaded at the chain's spout.</summary>
    internal string LoadBlocker(Machine vehicle)
    {
        var (m, silo) = AtSpouts(vehicle).OrderByDescending(x => Cargo(x.m).Any()).First();
        if (m.FarmId != silo.Poi.FarmId) return $"{silo.Poi.Name} belongs to another farm";
        if (Closed(silo.Poi, silo.Def) is { } closed) return closed;
        if (!Stock(silo).Any()) return $"{silo.Poi.Name} is empty";
        return Stock(silo).Any(s => Cargo(m).Any(u => u.CanAccept(s.fillType))) ? $"Open the cover of {m.Def.Name} first" : $"Nothing stored here fits {m.Def.Name}";
    }

    private void UpdateLoading(float dt)
    {
        foreach (var (m, l) in _loading.ToList())
        {
            var storage = l.Silo.Storage;
            var unit = Open(m).FirstOrDefault(u => u.CanAccept(l.FillType));
            if (unit == null || l.Silo.Trigger("load")?.Contains(m.Footprint.Center) != true || storage.Level(l.FillType) < 0.001f)
            {
                FinishLoading(m);
                continue;
            }
            var moved = unit.Add(l.FillType, storage.Remove(l.FillType, MathF.Min(l.Silo.Def.LoadRate * dt, unit.Free)));
            _loading[m] = l with { Amount = l.Amount + moved };
        }
    }

    private void FinishLoading(Machine m)
    {
        if (!_loading.Remove(m, out var l) || l.Amount < 1f) return;
        _sim.Events.Publish(new FillLoaded(m, l.Silo.Poi, l.FillType, l.Amount));
    }

    // ------------------------------------------------------------------ Deliveries

    /// <summary>The delivery spots machines from the shop are delivered at: the dealers'.</summary>
    private IEnumerable<DeliverySpot> Dealers => All.Select(p => p.Get<DeliverySpot>()).OfType<DeliverySpot>().Where(d => d.Def.Sales);

    /// <summary>Where a new <paramref name="def"/> would be delivered: a free spot on the lot of the first open dealer with one.</summary>
    private (DeliverySpot dealer, Vector2 center)? SalesLot(MachineDef def) => Dealers
        .Where(d => Closed(d.Poi, d.Def) == null)
        .Select(d => (dealer: d, center: FreeSpot(d.Lot.Area, def)))
        .FirstOrDefault(x => x.center != null) is ({ } dealer, { } center) ? (dealer, center) : null;

    /// <summary>Why a new <paramref name="def"/> can't be delivered now, or null when it can: no dealer, closed, or no room.</summary>
    public string? DeliveryBlocker(MachineDef def)
    {
        if (SalesLot(def) != null) return null;
        var dealers = Dealers.ToList();
        if (dealers.Count == 0) return "No dealer delivers machines on this map";
        return dealers.FirstOrDefault(d => Closed(d.Poi, d.Def) == null) is { } open
            ? $"No room on the lot of {open.Poi.Name}: clear it first"
            : Closed(dealers[0].Poi, dealers[0].Def);
    }

    /// <summary>
    /// Puts a new machine of <paramref name="farmId"/>, with <paramref name="def"/>'s options, in a free spot of the first
    /// open dealer's lot with one, facing the POI's front. Null when there is none.
    /// </summary>
    public (Machine machine, Poi poi)? Deliver(MachineDef def, int farmId)
    {
        if (SalesLot(def) is not var (dealer, center)) return null;
        var heading = dealer.Lot.Area.Heading;
        var m = _sim.Machines.Spawn(def.Id, center - MathUtil.Forward(heading) * def.Size.CenterZ, heading, farmId, def.Choices);
        return (m, dealer.Poi);
    }

    /// <summary>
    /// Puts a set of new machines of <paramref name="farmId"/> on <paramref name="spot"/>, a delivery trigger: each
    /// implement hitched to the first vehicle of the set with a free joint for it, and each vehicle with what it pulls
    /// in a free place facing the POI's front. Null when they don't all fit, and then none are delivered.
    /// </summary>
    public List<Machine>? DeliverSet(IReadOnlyList<string> machineDefIds, int farmId, PoiTrigger spot)
    {
        var area = spot.Area;
        var others = _sim.Machines.All.ToList();
        var set = machineDefIds.Select(id => _sim.Machines.Spawn(id, area.Center, area.Heading, farmId)).ToList();
        foreach (var implement in set.Where(m => m.Has<Attachable>()))
        {
            var (vehicle, joint) = set.Where(v => v.Has<Motor>())
                .SelectMany(v => v.Def.Joints.Select(j => (v, j)))
                .FirstOrDefault(x => x.j.Type == implement.Get<Attachable>()!.Def.Type && !x.v.Attached.ContainsKey(x.j.Id));
            if (vehicle != null) _sim.Machines.Hitch(vehicle, joint.Id, implement);
        }
        var obstacles = _sim.World.Obstacles.Where(o => Vector2.Distance(o.Center, area.Center) < area.BoundingRadius + o.BoundingRadius).ToList();
        foreach (var root in set.Where(m => m.Parent == null))
        {
            if (!Place(root, area, obstacles, others))
            {
                foreach (var m in set) _sim.Machines.All.Remove(m);
                return null;
            }
            others.AddRange(root.Chain());
        }
        return set;
    }

    /// <summary>Moves a machine and what it pulls to the first place in <paramref name="area"/> clear of the rest, front row first.</summary>
    private bool Place(Machine root, Obb area, List<Obstacle> obstacles, List<Machine> others)
    {
        const float gap = 1f;
        for (var y = area.HalfExtents.Y; y >= -area.HalfExtents.Y; y -= 1f)
        for (var x = -area.HalfExtents.X; x <= area.HalfExtents.X; x += 1f)
        {
            _sim.Machines.Teleport(root, area.Center + area.AxisX * x + area.AxisY * y, area.Heading);
            if (root.Chain().All(m => Clear(m.Footprint))) return true;
        }
        return false;

        bool Clear(Obb box)
        {
            Span<Vector2> corners = stackalloc Vector2[4];
            MathUtil.RectCorners(box.Center, box.Heading, box.HalfExtents.X, box.HalfExtents.Y, corners);
            foreach (var c in corners)
                if (!area.Contains(c)) return false;
            var room = box with { HalfExtents = box.HalfExtents + new Vector2(gap * 0.5f) };
            return !obstacles.Any(o => Geometry.Overlaps(room, o)) && !others.Any(m => Geometry.Overlaps(room, m.Footprint));
        }
    }

    /// <summary>A footprint center inside <paramref name="area"/>, clear of machines and obstacles, row by row.</summary>
    private Vector2? FreeSpot(Obb area, MachineDef def)
    {
        const float gap = 1f;
        var half = new Vector2(def.Size.Width * 0.5f, def.Size.Length * 0.5f);
        var room = area.HalfExtents - half;
        for (var y = -room.Y; y <= room.Y + 0.01f; y += def.Size.Length + gap)
        for (var x = -room.X; x <= room.X + 0.01f; x += def.Size.Width + gap)
        {
            var box = new Obb(area.Center + area.AxisX * x + area.AxisY * y, half, area.Heading);
            if (_sim.World.Obstacles.Any(o => Geometry.Overlaps(box, o))) continue;
            if (_sim.Machines.All.Any(m => Geometry.Overlaps(box, m.Footprint))) continue;
            return box.Center;
        }
        return null;
    }

    // ------------------------------------------------------------------ Filling, repairs, washing

    /// <summary>What refueling or buying at <paramref name="shop"/> gets <paramref name="m"/>: "Refuel", "Buy seeds".</summary>
    internal IEnumerable<string> Wants(Machine m, BuyingStation shop)
    {
        var tank = m.Get<Motor>()?.FuelTank;
        if (tank != null && shop.Def.FillTypes.Any(tank.Accepts)) yield return "Refuel";
        if (shop.Def.FillTypes.Where(ft => m.FillUnits.Any(u => u != tank && u.Accepts(ft))).ToList() is { Count: > 0 } supplies)
            yield return $"Buy {Names(supplies)}";
    }

    /// <summary>What an order of <paramref name="fillType"/> into <paramref name="station"/>'s storage brings now, as much as fits, and costs.</summary>
    public (float amount, float cost) OrderOf(BuyingStation station, string fillType)
    {
        var amount = MathF.Min(station.Def.OrderAmount, station.Storage.Free(fillType));
        return (amount, amount * Price(station, fillType));
    }

    /// <summary>Orders <paramref name="fillType"/> in bulk into <paramref name="station"/>'s storage for its owner, as much as fits and it can pay for.</summary>
    public void Order(BuyingStation station, string fillType)
    {
        var poi = station.Poi;
        var price = Price(station, fillType);
        var amount = Affordable(poi.FarmId, OrderOf(station, fillType).amount, price);
        if (amount < 1f)
        {
            Tell([station.Storage.Free(fillType) < 1f ? $"{poi.Name} is full" : "Not enough money"]);
            return;
        }
        amount = station.Storage.Add(fillType, amount);
        Spend(poi.FarmId, amount * price, IsFuel(fillType) ? MoneyCategory.Fuel : MoneyCategory.Purchases);
        _sim.Events.Publish(new FillOrdered(poi, fillType, amount, amount * price));
    }

    /// <summary>
    /// Fills <paramref name="machines"/> from what <paramref name="station"/>'s POI keeps, for nothing: its motors' tanks
    /// and whatever else takes it. Says why when nothing could be filled.
    /// </summary>
    public void FillFromStorage(IEnumerable<Machine> machines, BuyingStation station)
    {
        var poi = station.Poi;
        var why = new List<string>();
        var filled = false;
        foreach (var m in machines.Where(m => Wants(m, station).Any()))
        foreach (var unit in m.FillUnits)
        foreach (var ft in station.Def.FillTypes)
        {
            if (!unit.CanAccept(ft)) continue;
            if (m.ClosedOver(unit))
            {
                why.Add($"Open the cover of {m.Def.Name} first");
                continue;
            }
            var amount = unit.Add(ft, station.Storage.Remove(ft, unit.Free));
            if (amount < 0.01f) continue;
            filled = true;
            _sim.Events.Publish(new FillLoaded(m, poi, ft, amount));
        }
        if (!filled) Tell(why.Count > 0 ? why : [$"{poi.Name} is empty, or the machines are full"]);
    }

    /// <summary>Refuels <paramref name="machines"/> at <paramref name="shop"/> and buys what their other units take; says why when nothing could be bought.</summary>
    public void Buy(IEnumerable<Machine> machines, BuyingStation shop)
    {
        var why = new List<string>();
        var served = false;
        foreach (var m in machines.Where(m => Wants(m, shop).Any())) served |= FillUp(m, shop, why);
        if (!served) Tell(why);
    }

    /// <summary>
    /// Repairs every machine of <paramref name="chain"/> for <paramref name="workshop"/>, at <paramref name="factor"/>
    /// times its prices; says why when none could be.
    /// </summary>
    public void Repair(IEnumerable<Machine> chain, Workshop workshop, float factor = 1f) =>
        Serve(chain, m => RepairBlocker(m, workshop, factor), m => RepairPrice(workshop, m, factor), (m, cost) =>
        {
            m.Get<Wearable>()!.Condition = 1f;
            _sim.Events.Publish(new MachineRepaired(m, workshop.Poi, cost));
        });

    /// <summary>Repaints every machine of <paramref name="chain"/> for <paramref name="workshop"/>, as <see cref="Repair"/>.</summary>
    public void Repaint(IEnumerable<Machine> chain, Workshop workshop, float factor = 1f) =>
        Serve(chain, m => RepaintBlocker(m, workshop, factor), m => RepaintPrice(workshop, m, factor), (m, cost) =>
        {
            m.Get<Wearable>()!.Paint = 1f;
            _sim.Events.Publish(new MachineRepainted(m, workshop.Poi, cost));
        });

    /// <summary>Washes every machine of <paramref name="chain"/> at <paramref name="washer"/>; says why when none could be.</summary>
    public void Wash(IEnumerable<Machine> chain, WashingStation washer) =>
        Serve(chain, m => ServiceBlocker(m, washer.Poi, washer.Def, m.Dirt, WashPrice(washer, m), "Nothing to wash"), m => WashPrice(washer, m), (m, cost) =>
        {
            m.Dirt = 0f;
            _sim.Events.Publish(new MachineWashed(m, washer.Poi, cost));
        });

    /// <summary>Why <paramref name="workshop"/> can't repair <paramref name="m"/> at <paramref name="factor"/> times its price, or null when it can.</summary>
    public string? RepairBlocker(Machine m, Workshop workshop, float factor = 1f) =>
        ServiceBlocker(m, workshop.Poi, workshop.Def, m.Get<Wearable>()?.Wear ?? 0f, RepairPrice(workshop, m, factor), "Nothing to repair");

    /// <summary>Why <paramref name="workshop"/> can't repaint <paramref name="m"/> at <paramref name="factor"/> times its price, or null when it can.</summary>
    public string? RepaintBlocker(Machine m, Workshop workshop, float factor = 1f) =>
        ServiceBlocker(m, workshop.Poi, workshop.Def, m.Get<Wearable>()?.PaintWear ?? 0f, RepaintPrice(workshop, m, factor), "Nothing to repaint");

    private void Tell(List<string> why) =>
        _sim.Notifications.Post(why.Contains("Not enough money") ? "Not enough money" : why.FirstOrDefault() ?? "Nothing to do here");

    /// <summary>Buys what <paramref name="m"/>'s units take at <paramref name="shop"/>: into its fuel tank refueling, into the others purchases.</summary>
    private bool FillUp(Machine m, BuyingStation shop, List<string> why)
    {
        var poi = shop.Poi;
        if (Closed(poi, shop.Def) is { } closed)
        {
            why.Add(closed);
            return false;
        }
        var done = false;
        var tank = m.Get<Motor>()?.FuelTank;
        foreach (var unit in m.FillUnits)
        foreach (var ft in shop.Def.FillTypes)
        {
            if (!unit.CanAccept(ft)) continue;
            if (m.ClosedOver(unit))
            {
                why.Add($"Open the cover of {m.Def.Name} first");
                continue;
            }
            if (unit.Free < shop.Def.MinAmount)
            {
                why.Add($"{poi.Name} sells {shop.Def.MinAmount:N0} {Content.FillTypes[ft].Unit} or more");
                continue;
            }
            var price = Price(shop, ft);
            var amount = unit.Add(ft, Affordable(m.FarmId, unit.Free, price));
            if (amount <= 0f)
            {
                why.Add("Not enough money");
                continue;
            }
            Spend(m.FarmId, amount * price, unit == tank ? MoneyCategory.Fuel : MoneyCategory.Purchases);
            _sim.Events.Publish(new FillBought(m, poi, ft, amount, amount * price));
            done = true;
        }
        if (!done && why.Count == 0) why.Add($"{m.Def.Name}: full, or takes nothing sold here");
        return done;
    }

    /// <summary>A service (repairs, washing) is refused when it's closed, there's nothing to do or the farm can't pay.</summary>
    private string? ServiceBlocker(Machine m, Poi poi, IConditions station, float need, float cost, string nothing) =>
        Closed(poi, station) ?? (need < 0.005f ? nothing : IsPlayers(m.FarmId) && cost > Economy.Money ? "Not enough money" : null);

    /// <summary>
    /// Serves each machine of <paramref name="chain"/> that nothing blocks: pays what it costs, then
    /// <paramref name="serve"/> does it (given the cost); says why when none could be served.
    /// </summary>
    private void Serve(IEnumerable<Machine> chain, Func<Machine, string?> blocker, Func<Machine, float> cost, Action<Machine, float> serve)
    {
        var why = new List<string>();
        var served = false;
        foreach (var m in chain)
        {
            if (blocker(m) is { } no)
            {
                why.Add(no);
                continue;
            }
            var paid = cost(m);
            Spend(m.FarmId, paid, MoneyCategory.Maintenance);
            serve(m, paid);
            served = true;
        }
        if (!served) Tell(why);
    }

    // ------------------------------------------------------------------ Options

    /// <summary>The workshop fitting options whose bay the vehicle's chain stands in.</summary>
    public Workshop? Workshop(Machine vehicle)
    {
        foreach (var m in vehicle.Chain())
            if (TriggerAt(m.Footprint.Center, "repair")?.Station is Workshop { Def.Configure: not null } workshop)
                return workshop;
        return null;
    }

    /// <summary>
    /// What giving <paramref name="m"/> the options of <paramref name="def"/> costs: for each option changed, what it
    /// costs more than the one it replaces (a cheaper one gives nothing back) times the workshop's price factor, and
    /// its price for the work; times the price level and <paramref name="factor"/>.
    /// </summary>
    public float ConfigurePrice(Workshop workshop, Machine m, MachineDef def, float factor = 1f)
    {
        var configure = workshop.Def.Configure ?? new ConfigureDef();
        var cost = 0f;
        foreach (var c in def.Configurations)
        {
            var (from, to) = (m.Def.Chosen(c), def.Chosen(c));
            if (from == to || to == null) continue;
            cost += configure.PriceFactor * MathF.Max(0f, to.Price - (from?.Price ?? 0f)) + configure.Price;
        }
        return cost * Economy.PriceLevel * factor;
    }

    /// <summary>
    /// Why <paramref name="m"/> can't be given the options of <paramref name="def"/> by <paramref name="workshop"/> (at
    /// <paramref name="factor"/> times its prices), or null when it can.
    /// </summary>
    public string? ConfigureBlocker(Machine m, MachineDef def, Workshop workshop, float factor = 1f)
    {
        if (def == m.Def) return "Nothing to change";
        if (workshop.Def.Configure == null) return $"{workshop.Poi.Name} does not fit options";
        if (Closed(workshop.Poi, workshop.Def) is { } closed) return closed;
        if (m.LeaseContract != 0 || m.Lease != null) return "A leased machine goes back as it came";
        if (m.Root.Get<Drivable>()?.Controller is FieldWorkController) return "The helper is working: dismiss them first";
        foreach (var u in m.FillUnits)
            if (u.Level > (def.Get<FillUnitsDef>()?.Units.FirstOrDefault(x => x.Id == u.Def.Id)?.Capacity ?? 0f) + 0.5f)
                return $"Unload the {m.Def.Name} first: it holds more than it would take";
        if (IsPlayers(m.FarmId) && ConfigurePrice(workshop, m, def, factor) is var cost and > 0f && cost > Economy.Money) return "Not enough money";
        return null;
    }

    /// <summary>
    /// Gives <paramref name="m"/> the options <paramref name="choices"/> picks (configuration id → option id, over the
    /// ones it has) at the workshop its chain stands in, for their price. False, with a notification, when it can't.
    /// </summary>
    public bool Configure(Machine m, IReadOnlyDictionary<string, string> choices)
    {
        var def = m.Def.Configure(choices);
        if (Workshop(m.Root) is not { } workshop)
        {
            _sim.Notifications.Post("Park at a workshop first");
            return false;
        }
        return Configure(m, def, workshop, 1f);
    }

    /// <summary>
    /// Gives <paramref name="m"/> the options of <paramref name="def"/>, fitted by <paramref name="workshop"/> for
    /// <paramref name="factor"/> times their price. False, with a notification, when it can't.
    /// </summary>
    internal bool Configure(Machine m, MachineDef def, Workshop workshop, float factor)
    {
        if (ConfigureBlocker(m, def, workshop, factor) is { } why)
        {
            _sim.Notifications.Post(why, Severity.Warning);
            return false;
        }
        var cost = ConfigurePrice(workshop, m, def, factor);
        var from = m.Def;
        Spend(m.FarmId, cost, MoneyCategory.Machines);
        _sim.Machines.Reconfigure(m, def);
        _sim.Events.Publish(new MachineConfigured(m, workshop.Poi, from, cost));
        return true;
    }

    // ------------------------------------------------------------------ Production

    /// <summary>
    /// Runs an hour of every POI (called for each world hour, so sleeping runs them too): demand recovers, high
    /// demand comes and goes at midnight, and productions work. A closed production waits with its cycle half done; one
    /// short of inputs or room starts its cycle over.
    /// </summary>
    internal void TickHour(long hour)
    {
        var hourOfDay = hour % 24 + 0.5f;
        var day = (int)(hour / 24);
        var month = _sim.Calendar.DateOfDay(day).Month;
        foreach (var poi in All)
        {
            if (poi.Get<SellingStation>() is not { } sell) continue;
            RecoverDemand(sell);
            if (hour % 24 == 0) UpdateHighDemand(sell, day);
        }
        foreach (var poi in All)
        {
            if (poi.Get<ProductionPoint>() is not { } plant) continue;
            foreach (var p in plant.Def.Productions)
            {
                if (Closed(poi, p, hourOfDay, month) != null) continue;
                if (!CanCycle(plant, p))
                {
                    plant.Progress.Remove(p.Id);
                    continue;
                }
                var progress = plant.Progress.GetValueOrDefault(p.Id) + 1f / p.CycleHours;
                var cycles = 0;
                while (progress >= 1f && CanCycle(plant, p))
                {
                    foreach (var input in p.Inputs) plant.Storage.Remove(input.FillType, input.Amount);
                    foreach (var output in p.Outputs) plant.Storage.Add(output.FillType, output.Amount);
                    progress -= 1f;
                    cycles++;
                }
                plant.Progress[p.Id] = progress;
                Spend(poi.FarmId, p.RunningCost * Economy.PriceLevel, MoneyCategory.Production);
                if (cycles > 0)
                    foreach (var output in p.Outputs) _sim.Events.Publish(new PoiProduced(poi, output.FillType, output.Amount * cycles));
                SellOutputs(plant, p, month);
                PalletOutputs(plant, p);
            }
        }
    }

    /// <summary>
    /// Puts the stored outputs whose mode is "pallet" on pallets in the production point's pallet area (FS: a pallet
    /// spawner), for its owner: onto a pallet of it there that isn't full yet, else a new one in the first free place,
    /// row by row from the area's front. With no room left in the area, they stay stored (and the production stops once
    /// its storage is full).
    /// </summary>
    private void PalletOutputs(ProductionPoint plant, ProductionDef production)
    {
        if (plant.Trigger("pallets") is not { } spot) return;
        foreach (var output in production.Outputs.Where(o => o is { Mode: "pallet", Pallet: not null }))
        {
            var ft = output.FillType;
            var def = Content.Objects[output.Pallet!];
            while (plant.Storage.Level(ft) >= 1f)
            {
                var pallet = _sim.Objects.LooseIn(spot.Area).FirstOrDefault(o =>
                                 o.Def == def && o.FarmId == plant.Poi.FarmId && o.Content is { } c && c.CanAccept(ft) && !c.IsEmpty)
                             ?? (FreePlace(spot.Area, def) is { } at ? _sim.Objects.Spawn(def.Id, at, spot.Area.Heading, plant.Poi.FarmId) : null);
                if (pallet == null) break;
                pallet.Content!.Add(ft, plant.Storage.Remove(ft, pallet.Content.Free));
            }
        }
    }

    /// <summary>The middle of a free place for <paramref name="def"/> in <paramref name="area"/>, rows from its front, or null.</summary>
    private Vector2? FreePlace(Obb area, ObjectDef def)
    {
        const float gap = 0.4f;
        var half = new Vector2(def.Size.Width * 0.5f, def.Size.Length * 0.5f);
        var room = area.HalfExtents - half;
        for (var y = room.Y; y >= -room.Y - 0.01f; y -= def.Size.Length + gap)
        for (var x = -room.X; x <= room.X + 0.01f; x += def.Size.Width + gap)
        {
            var box = new Obb(area.Center + area.AxisX * x + area.AxisY * y, half + new Vector2(gap * 0.4f), area.Heading);
            if (!_sim.Objects.All.Any(o => o.Holder == null && Geometry.Overlaps(box, o.Footprint))) return box.Center;
        }
        return null;
    }

    /// <summary>Sells the stored outputs whose mode is "sell", at the market price times the production's factor.</summary>
    private void SellOutputs(ProductionPoint plant, ProductionDef production, int month)
    {
        foreach (var output in production.Outputs.Where(o => o.Mode == "sell"))
        {
            var ft = output.FillType;
            var amount = plant.Storage.Remove(ft, plant.Storage.Level(ft));
            if (amount < 0.001f) continue;
            var income = amount * Economy.Price(ft, month) * production.FactorOf(ft);
            Earn(plant.Poi.FarmId, income, MoneyCategory.Sales);
            _sim.Events.Publish(new ProductionSold(plant.Poi, ft, amount, income));
        }
    }

    /// <summary>True when the fill units hold a cycle's inputs and have room for its outputs.</summary>
    private static bool CanCycle(ProductionPoint plant, ProductionDef p) =>
        p.Inputs.All(x => plant.Storage.Level(x.FillType) >= x.Amount - 0.001f) &&
        p.Outputs.All(x => plant.Storage.Free(x.FillType) >= x.Amount - 0.001f);

    // ------------------------------------------------------------------ Demand

    private static void RecoverDemand(SellingStation sell)
    {
        foreach (var ft in sell.Demand.Keys.ToList())
        {
            var demand = sell.Demand[ft] + sell.Def.Demand.Recovery / 24f;
            if (demand >= 1f) sell.Demand.Remove(ft);
            else sell.Demand[ft] = demand;
        }
    }

    private void UpdateHighDemand(SellingStation sell, int day)
    {
        if (sell.HighDemand is { } high && day >= high.EndDay)
        {
            sell.HighDemand = null;
            _sim.Events.Publish(new HighDemandEnded(sell.Poi, high.FillType));
        }
        var d = sell.Def.Demand;
        if (sell.HighDemand != null || d.HighChance <= 0f || !Rng.Chance(d.HighChance)) return;
        var ft = sell.Def.FillTypes[Rng.Range(0, sell.Def.FillTypes.Length)];
        var factor = Rng.Range(d.HighFactor[0], d.HighFactor[1]);
        var end = day + Rng.Range(d.HighDays[0], d.HighDays[1] + 1);
        sell.HighDemand = new HighDemand(ft, factor, end);
        _sim.Events.Publish(new HighDemandStarted(sell.Poi, ft, factor, _sim.Calendar.DateOfDay(end - 1)));
    }

    // ------------------------------------------------------------------ Money

    // The economy is the player's farm's: other farms (NPCs) trade without it.
    private bool IsPlayers(int farmId) => farmId == _sim.Farms.Player.Id;

    private void Earn(int farmId, float amount, MoneyCategory category)
    {
        if (IsPlayers(farmId) && amount > 0f) Economy.Earn(amount, category);
    }

    private void Spend(int farmId, float amount, MoneyCategory category)
    {
        if (IsPlayers(farmId) && amount > 0f) Economy.Spend(amount, category);
    }

    private float Affordable(int farmId, float amount, float unitPrice) =>
        IsPlayers(farmId) ? Economy.Affordable(amount, unitPrice) : amount;
}
