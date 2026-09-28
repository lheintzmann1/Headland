using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
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
/// turn stored goods into others every hour; new machines appear at delivery spots. Machines move the goods (tipping,
/// piping); the POI's components decide what happens to them.
/// </summary>
public sealed class PoiSystem
{
    private readonly Simulation _sim;
    private readonly Dictionary<Machine, Delivery> _deliveries = new();
    private readonly HashSet<Machine> _unloading = [];
    private readonly Dictionary<Machine, Loading> _loading = new();
    private HashSet<string>? _fuels;

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

    /// <summary>
    /// What repairing <paramref name="m"/> costs: 1% of its price for each 100% of wear, times the workshop's factor
    /// and the price level.
    /// </summary>
    public float RepairPrice(Workshop workshop, Machine m) =>
        workshop.Def.RepairPriceFactor * m.Def.Price / 100f * (m.Get<Wearable>()?.Wear ?? 0f) * Economy.PriceLevel;

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
        SellingStation s => [With(s.Poi, s.Def, $"Sell {Names(s.Def.FillTypes)}", s.Def.MinAmount, s.Def.FillTypes)],
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
        DeliverySpot d => [d.Def.Leases ? "Machines leased for contracts wait here" : "New machines are delivered here"],
        _ => [],
    };

    private string Names(IEnumerable<string> fillTypes) =>
        string.Join(", ", fillTypes.Select(f => Content.FillTypes[f].Name.ToLowerInvariant()));

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
                if (Cargo(m).Any(u => u.CanAccept(ft)))
                    choices.Add((ft, stock, Cargo(m).Any(u => u.FillType == ft)));
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
            if (Closed(silo.Poi, silo.Def) != null || !Cargo(m).Any(u => u.CanAccept(fillType))) continue;
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
    private string LoadBlocker(Machine vehicle)
    {
        var (m, silo) = AtSpouts(vehicle).OrderByDescending(x => Cargo(x.m).Any()).First();
        if (m.FarmId != silo.Poi.FarmId) return $"{silo.Poi.Name} belongs to another farm";
        if (Closed(silo.Poi, silo.Def) is { } closed) return closed;
        return Stock(silo).Any() ? $"Nothing stored here fits {m.Def.Name}" : $"{silo.Poi.Name} is empty";
    }

    private void UpdateLoading(float dt)
    {
        foreach (var (m, l) in _loading.ToList())
        {
            var storage = l.Silo.Storage;
            var unit = Cargo(m).FirstOrDefault(u => u.CanAccept(l.FillType));
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

    /// <summary>
    /// Puts a new machine of <paramref name="farmId"/> in a free spot of a delivery spot (<paramref name="at"/>'s, else
    /// the first POI's with room), facing the POI's front. Null when every spot is taken.
    /// </summary>
    public Machine? Deliver(string machineDefId, int farmId, Poi? at = null)
    {
        var def = Content.Machines[machineDefId];
        foreach (var t in (at != null ? [at] : All).SelectMany(p => p.Triggers).Where(t => t.Type == "delivery"))
        {
            if (FreeSpot(t.Area, def) is not { } center) continue;
            var m = _sim.Machines.Spawn(machineDefId, center - MathUtil.Forward(t.Area.Heading) * def.Size.CenterZ, t.Area.Heading, farmId);
            _sim.Events.Publish(new MachineDelivered(m, t.Poi));
            return m;
        }
        return null;
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

    /// <summary>
    /// The use key. Under a silo's spout it starts loading what the chain can take (the first of
    /// <see cref="LoadChoices"/>), or stops. Machines of the chain parked at a buying station buy supplies and fuel
    /// there, and the whole chain is repaired or washed once any of it stands in a workshop's bay or a washing station.
    /// </summary>
    public void Use(Machine vehicle)
    {
        if (IsLoading(vehicle))
        {
            StopLoading(vehicle);
            return;
        }
        var chain = vehicle.Chain().ToList();
        var parked = false;
        var served = false;
        var why = new List<string>();
        if (AtSpouts(vehicle).Any())
        {
            parked = true;
            if (LoadChoices(vehicle) is [var first, ..]) served = StartLoading(vehicle, first);
            else why.Add(LoadBlocker(vehicle));
        }
        foreach (var m in chain)
        {
            if (TriggerAt(m.Footprint.Center, "fill")?.Station is not BuyingStation shop) continue;
            parked = true;
            served |= FillUp(m, shop, why);
        }
        if (Bay(chain, "repair")?.Station is Workshop workshop)
        {
            parked = true;
            foreach (var m in chain) served |= Repair(m, workshop, why);
        }
        if (Bay(chain, "wash")?.Station is WashingStation washer)
        {
            parked = true;
            foreach (var m in chain) served |= Wash(m, washer, why);
        }
        if (!parked) _sim.Notifications.Post("Park in a marked area first: a shop, silo, gas station, workshop or wash bay");
        else if (!served) _sim.Notifications.Post(why.Contains("Not enough money") ? "Not enough money" : why.FirstOrDefault() ?? "Nothing to do here");
    }

    /// <summary>What the use key does for the vehicle's chain where it stands ("Buy seeds", "Repair ($1,250)").</summary>
    public List<string> UseOptions(Machine vehicle)
    {
        var options = new List<string>();
        var chain = vehicle.Chain().ToList();
        if (IsLoading(vehicle)) options.Add("Stop loading");
        else if (LoadChoices(vehicle) is { Count: > 0 } loads) options.Add(loads.Count == 1 ? $"Load {Names(loads)}" : "Load…");
        foreach (var m in chain)
        {
            if (TriggerAt(m.Footprint.Center, "fill")?.Station is not BuyingStation shop || Closed(shop.Poi, shop.Def) != null) continue;
            var tank = m.Get<Motor>()?.FuelTank;
            if (tank != null && shop.Def.FillTypes.Any(tank.Accepts)) options.Add("Refuel");
            if (shop.Def.FillTypes.Where(ft => m.FillUnits.Any(u => u != tank && u.Accepts(ft))).ToList() is { Count: > 0 } supplies)
                options.Add($"Buy {Names(supplies)}");
        }
        if (Bay(chain, "repair")?.Station is Workshop workshop && Closed(workshop.Poi, workshop.Def) == null
            && chain.Sum(m => RepairPrice(workshop, m)) is var repair and > 0.5f)
            options.Add($"Repair (${repair:N0})");
        if (Workshop(vehicle) is { } fitter && Closed(fitter.Poi, fitter.Def) == null && chain.Any(m => m.Def.Configurations.Count > 0))
            options.Add("Change options…");
        if (Bay(chain, "wash")?.Station is WashingStation washer && Closed(washer.Poi, washer.Def) == null && chain.Any(m => m.Dirt > 0.005f))
            options.Add($"Wash (${chain.Sum(m => WashPrice(washer, m)):N0})");
        return options.Distinct().ToList();
    }

    private PoiTrigger? Bay(IEnumerable<Machine> chain, string type) =>
        chain.Select(m => TriggerAt(m.Footprint.Center, type)).FirstOrDefault(t => t != null);

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

    private bool Repair(Machine m, Workshop workshop, List<string> why) =>
        Service(m, workshop.Poi, workshop.Def, m.Get<Wearable>()?.Wear ?? 0f, RepairPrice(workshop, m), "Nothing to repair", why, cost =>
        {
            m.Get<Wearable>()!.Condition = 1f;
            _sim.Events.Publish(new MachineRepaired(m, workshop.Poi, cost));
        });

    private bool Wash(Machine m, WashingStation washer, List<string> why) =>
        Service(m, washer.Poi, washer.Def, m.Dirt, WashPrice(washer, m), "Nothing to wash", why, cost =>
        {
            m.Dirt = 0f;
            _sim.Events.Publish(new MachineWashed(m, washer.Poi, cost));
        });

    /// <summary>Repairs or washes <paramref name="m"/> when it's open, there's something to do and the farm can pay.</summary>
    private bool Service(Machine m, Poi poi, IConditions station, float need, float cost, string nothing, List<string> why, Action<float> done)
    {
        if (Closed(poi, station) is { } closed)
        {
            why.Add(closed);
            return false;
        }
        if (need < 0.005f)
        {
            why.Add(nothing);
            return false;
        }
        if (IsPlayers(m.FarmId) && cost > Economy.Money)
        {
            why.Add("Not enough money");
            return false;
        }
        Spend(m.FarmId, cost, MoneyCategory.Maintenance);
        done(cost);
        return true;
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
    /// its price for the work; times the price level.
    /// </summary>
    public float ConfigurePrice(Workshop workshop, Machine m, MachineDef def)
    {
        var configure = workshop.Def.Configure ?? new ConfigureDef();
        var cost = 0f;
        foreach (var c in def.Configurations)
        {
            var (from, to) = (m.Def.Chosen(c), def.Chosen(c));
            if (from == to || to == null) continue;
            cost += configure.PriceFactor * MathF.Max(0f, to.Price - (from?.Price ?? 0f)) + configure.Price;
        }
        return cost * Economy.PriceLevel;
    }

    /// <summary>Why <paramref name="m"/> can't be given the options of <paramref name="def"/> at <paramref name="workshop"/>, or null when it can.</summary>
    public string? ConfigureBlocker(Machine m, MachineDef def, Workshop workshop)
    {
        if (def == m.Def) return "Nothing to change";
        if (workshop.Def.Configure == null) return $"{workshop.Poi.Name} does not fit options";
        if (Closed(workshop.Poi, workshop.Def) is { } closed) return closed;
        if (m.LeaseContract != 0) return "A leased machine goes back as it came";
        if (m.Root.Get<Drivable>()?.Controller is FieldWorkController) return "The helper is working: dismiss them first";
        foreach (var u in m.FillUnits)
            if (u.Level > (def.Get<FillUnitsDef>()?.Units.FirstOrDefault(x => x.Id == u.Def.Id)?.Capacity ?? 0f) + 0.5f)
                return $"Unload the {m.Def.Name} first: it holds more than it would take";
        if (IsPlayers(m.FarmId) && ConfigurePrice(workshop, m, def) is var cost and > 0f && cost > Economy.Money) return "Not enough money";
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
        if (ConfigureBlocker(m, def, workshop) is { } why)
        {
            _sim.Notifications.Post(why, Severity.Warning);
            return false;
        }
        var cost = ConfigurePrice(workshop, m, def);
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
            }
        }
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
