using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;

namespace Headland.Core.Pois;

/// <summary>A load being unloaded at a POI, totalled until the machine stops: sold, or stored by the POI's owner.</summary>
internal sealed record Delivery(Poi Poi, string FillType, float Amount, float Income, bool Stored);

/// <summary>A machine filling up from a POI's storage at a load trigger, with what it took so far.</summary>
internal sealed record Loading(PoiTrigger Trigger, string FillType, float Amount);

/// <summary>
/// What POIs do. Loads tipped or piped into an unload trigger are sold or stored; the owner's trailers fill up from
/// storage at a load trigger; machines parked in fill, repair and wash triggers buy supplies and fuel, get repaired
/// and washed; processing turns stored goods into others every hour; new machines appear at delivery triggers.
/// Machines move the goods (tipping, piping); the POI's actions decide what happens to them.
/// </summary>
public sealed class PoiSystem
{
    private readonly Simulation _sim;
    private readonly Dictionary<Machine, Delivery> _deliveries = new();
    private readonly HashSet<Machine> _unloading = [];
    private readonly Dictionary<Machine, Loading> _loading = new();

    public PoiSystem(Simulation sim) => _sim = sim;

    public IReadOnlyList<Poi> All => _sim.World.Pois;

    /// <summary>Loads being unloaded, by machine: published as one sale or delivery when the machine stops.</summary>
    internal Dictionary<Machine, Delivery> Deliveries => _deliveries;
    /// <summary>Machines being loaded at a load trigger.</summary>
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

    /// <summary>What a unit of <paramref name="fillType"/> sells or costs at <paramref name="trigger"/> right now.</summary>
    public float Price(PoiTrigger trigger, string fillType) => Economy.Price(fillType, _sim.Clock.Month);

    /// <summary>What repairing <paramref name="m"/> costs: 1% of its price for each 100% of wear.</summary>
    public static float RepairPrice(Machine m) => m.Def.Price / 100f * (1f - m.Condition);

    public static float WashPrice(PoiActionDef wash, Machine m) => wash.Price * m.Dirt;

    /// <summary>What the farmer can do at a trigger, one line per action ("Sell wheat, barley").</summary>
    public IEnumerable<string> Describe(PoiTrigger trigger) => trigger.Type switch
    {
        "load" => [$"Load {Names(trigger.Poi.Storage!.Def.FillTypes)}"],
        "delivery" => ["New machines are delivered here"],
        _ => trigger.Actions.Select(a => Describe(trigger.Poi, a)),
    };

    private string Describe(Poi poi, PoiActionDef a) => a.Type switch
    {
        "sell" => $"Sell {Names(poi.FillTypesOf(a))}",
        "store" => $"Store {Names(poi.FillTypesOf(a))}",
        "buy" => $"Buy {Names(a.FillTypes)}",
        "refuel" => $"Refuel with {Names(a.FillTypes)}",
        "repair" => "Repair machines",
        "wash" => "Wash machines",
        _ => a.Type,
    };

    private string Names(IEnumerable<string> fillTypes) =>
        string.Join(", ", fillTypes.Select(f => Content.FillTypes[f].Name.ToLowerInvariant()));

    // ------------------------------------------------------------------ Unloading

    /// <summary>The action taking <paramref name="fillType"/> from <paramref name="m"/>: its farm's storage first, else a sale.</summary>
    private static PoiActionDef? UnloadAction(Machine m, PoiTrigger trigger, string fillType)
    {
        PoiActionDef? sell = null;
        foreach (var a in trigger.Actions)
        {
            if (!trigger.Poi.FillTypesOf(a).Contains(fillType)) continue;
            if (a.Type == "store" && m.FarmId == trigger.Poi.FarmId) return a;
            if (a.Type == "sell") sell ??= a;
        }
        return sell;
    }

    /// <summary>Where an accepted load goes: the POI's storage, or away to the market (null).</summary>
    private static PoiStorage? Destination(Poi poi, PoiActionDef action, string fillType) =>
        action.Type == "store" || poi.Storage?.Keeps(fillType) == true ? poi.Storage : null;

    /// <summary>Why <paramref name="m"/> can't unload <paramref name="fillType"/> at <paramref name="trigger"/>, or null if it can.</summary>
    public string? UnloadBlocker(Machine m, PoiTrigger trigger, string fillType)
    {
        var poi = trigger.Poi;
        var name = Content.FillTypes[fillType].Name;
        if (UnloadAction(m, trigger, fillType) is not { } action)
        {
            if (trigger.Actions.Any(a => a.Type == "store" && poi.FillTypesOf(a).Contains(fillType)))
                return $"{poi.Name} belongs to another farm";
            return trigger.Actions.All(a => a.Type == "store") ? $"{poi.Name} does not store {name}" : $"{poi.Name} does not buy {name}";
        }
        if (Destination(poi, action, fillType) is { } storage && storage.Free(fillType) < 1f) return $"{poi.Name} has no room for {name}";
        return null;
    }

    /// <summary>Takes up to <paramref name="amount"/> from <paramref name="m"/> unloading at <paramref name="trigger"/>; returns what it took.</summary>
    public float Unload(Machine m, PoiTrigger trigger, string fillType, float amount)
    {
        if (amount <= 0f || UnloadBlocker(m, trigger, fillType) != null) return 0f;
        var action = UnloadAction(m, trigger, fillType)!;
        if (Destination(trigger.Poi, action, fillType) is { } storage) amount = storage.Add(fillType, amount);
        if (amount <= 0f) return 0f;
        var stored = action.Type == "store";
        var income = stored ? 0f : amount * Price(trigger, fillType);
        Earn(m.FarmId, income);
        if (_deliveries.TryGetValue(m, out var d) && (d.Poi != trigger.Poi || d.FillType != fillType || d.Stored != stored)) Flush(m);
        d = _deliveries.GetValueOrDefault(m) ?? new Delivery(trigger.Poi, fillType, 0f, 0f, stored);
        _deliveries[m] = d with { Amount = d.Amount + amount, Income = d.Income + income };
        _unloading.Add(m);
        return amount;
    }

    /// <summary>
    /// After the machines moved: machines that stopped unloading this tick publish their sale or delivery, and
    /// loading trailers fill up.
    /// </summary>
    internal void Update(float dt)
    {
        foreach (var m in _deliveries.Keys.Where(m => !_unloading.Contains(m)).ToList()) Flush(m);
        _unloading.Clear();
        UpdateLoading(dt);
    }

    private void Flush(Machine m)
    {
        if (!_deliveries.Remove(m, out var d) || d.Amount < 1f) return;
        if (d.Stored) _sim.Events.Publish(new FillStored(m, d.Poi, d.FillType, d.Amount));
        else _sim.Events.Publish(new FillSold(m, d.Poi, d.FillType, d.Amount, d.Income));
    }

    // ------------------------------------------------------------------ Loading

    /// <summary>Machines of the chain standing in a load trigger, with that trigger.</summary>
    private IEnumerable<(Machine m, PoiTrigger spout)> AtLoadTriggers(Machine vehicle)
    {
        foreach (var m in vehicle.Chain())
            if (TriggerAt(m.Footprint.Center, "load") is { } spout) yield return (m, spout);
    }

    /// <summary>Fill units that take loads (not fuel tanks).</summary>
    private static IEnumerable<FillUnit> Cargo(Machine m) => m.FillUnits.Where(u => u.Def.Id != m.Def.Motorized?.FuelTank);

    /// <summary>What the vehicle's chain can load where it stands: what it already carries first, then by stock.</summary>
    public IReadOnlyList<string> LoadChoices(Machine vehicle)
    {
        var choices = new List<(string fillType, float stock, bool carried)>();
        foreach (var (m, spout) in AtLoadTriggers(vehicle))
        {
            if (m.FarmId != spout.Poi.FarmId) continue;
            foreach (var (ft, stock) in spout.Poi.Storage!.Levels)
                if (stock >= 1f && Cargo(m).Any(u => u.CanAccept(ft)))
                    choices.Add((ft, stock, Cargo(m).Any(u => u.FillType == ft)));
        }
        return choices.OrderByDescending(c => c.carried).ThenByDescending(c => c.stock).Select(c => c.fillType).Distinct().ToList();
    }

    /// <summary>True while some machine of the chain is being loaded.</summary>
    public bool IsLoading(Machine vehicle) => vehicle.Chain().Any(_loading.ContainsKey);

    /// <summary>What <paramref name="m"/> itself is being loaded with, or null.</summary>
    public string? LoadingFillType(Machine m) => _loading.GetValueOrDefault(m)?.FillType;

    /// <summary>Starts loading <paramref name="fillType"/> into every machine of the chain in a load trigger that takes it.</summary>
    public bool StartLoading(Machine vehicle, string fillType)
    {
        var started = false;
        foreach (var (m, spout) in AtLoadTriggers(vehicle))
        {
            if (_loading.ContainsKey(m) || m.FarmId != spout.Poi.FarmId || spout.Poi.Storage!.Level(fillType) < 1f) continue;
            if (!Cargo(m).Any(u => u.CanAccept(fillType))) continue;
            _loading[m] = new Loading(spout, fillType, 0f);
            started = true;
        }
        return started;
    }

    public void StopLoading(Machine vehicle)
    {
        foreach (var m in vehicle.Chain().Where(_loading.ContainsKey).ToList()) FinishLoading(m);
    }

    /// <summary>Why nothing can be loaded at the chain's load trigger.</summary>
    private string LoadBlocker(Machine vehicle)
    {
        var (m, spout) = AtLoadTriggers(vehicle).OrderByDescending(x => Cargo(x.m).Any()).First();
        if (m.FarmId != spout.Poi.FarmId) return $"{spout.Poi.Name} belongs to another farm";
        return spout.Poi.Storage!.Levels.Values.All(v => v < 1f) ? $"{spout.Poi.Name} is empty" : $"Nothing stored here fits {m.Def.Name}";
    }

    private void UpdateLoading(float dt)
    {
        foreach (var (m, l) in _loading.ToList())
        {
            var storage = l.Trigger.Poi.Storage!;
            var unit = Cargo(m).FirstOrDefault(u => u.CanAccept(l.FillType));
            if (unit == null || !l.Trigger.Contains(m.Footprint.Center) || storage.Level(l.FillType) < 0.001f)
            {
                FinishLoading(m);
                continue;
            }
            var moved = unit.Add(l.FillType, storage.Remove(l.FillType, MathF.Min(l.Trigger.Def.Rate * dt, unit.Free)));
            _loading[m] = l with { Amount = l.Amount + moved };
        }
    }

    private void FinishLoading(Machine m)
    {
        if (!_loading.Remove(m, out var l) || l.Amount < 1f) return;
        _sim.Events.Publish(new FillLoaded(m, l.Trigger.Poi, l.FillType, l.Amount));
    }

    // ------------------------------------------------------------------ Deliveries

    /// <summary>
    /// Puts a new machine of <paramref name="farmId"/> in a free spot of a delivery trigger (<paramref name="at"/>'s,
    /// else the first POI's with room), facing the POI's front. Null when every spot is taken.
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
    /// The use key. At a load trigger it starts loading what the chain can take (the first of
    /// <see cref="LoadChoices"/>), or stops. Machines of the chain parked in a fill trigger buy supplies and fuel there,
    /// and the whole chain is repaired or washed once any of it stands in a repair or wash trigger.
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
        if (AtLoadTriggers(vehicle).Any())
        {
            parked = true;
            if (LoadChoices(vehicle) is [var first, ..]) served = StartLoading(vehicle, first);
            else why.Add(LoadBlocker(vehicle));
        }
        foreach (var m in chain)
        {
            if (TriggerAt(m.Footprint.Center, "fill") is not { } fill) continue;
            parked = true;
            served |= FillUp(m, fill, why);
        }
        foreach (var type in (string[])["repair", "wash"])
        {
            if (Bay(chain, type) is not { } bay) continue;
            parked = true;
            foreach (var action in bay.Actions.Where(a => a.Type == type))
            foreach (var m in chain)
                served |= Service(m, bay, action, why);
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
            if (TriggerAt(m.Footprint.Center, "fill") is not { } fill) continue;
            foreach (var a in fill.Actions)
            {
                if (a.Type == "buy" && m.FillUnits.Any(u => a.FillTypes.Any(u.Accepts))) options.Add($"Buy {Names(a.FillTypes)}");
                if (a.Type == "refuel" && m.Unit(m.Def.Motorized?.FuelTank) is { } tank && a.FillTypes.Any(tank.Accepts)) options.Add("Refuel");
            }
        }
        if (Bay(chain, "repair") is { } workshop && chain.Sum(RepairPrice) is var repair and > 0.5f)
            options.Add($"Repair (${repair:N0})");
        if (Bay(chain, "wash") is { } wash && chain.Any(m => m.Dirt > 0.005f))
            options.Add($"Wash (${chain.Sum(m => wash.Actions.Where(a => a.Type == "wash").Sum(a => WashPrice(a, m))):N0})");
        return options.Distinct().ToList();
    }

    private PoiTrigger? Bay(IEnumerable<Machine> chain, string type) =>
        chain.Select(m => TriggerAt(m.Footprint.Center, type)).FirstOrDefault(t => t != null);

    private bool FillUp(Machine m, PoiTrigger trigger, List<string> why)
    {
        var done = false;
        foreach (var action in trigger.Actions)
        {
            FillUnit[] units = action.Type switch
            {
                "buy" => m.FillUnits,
                "refuel" => m.Unit(m.Def.Motorized?.FuelTank) is { } tank ? [tank] : [],
                _ => [],
            };
            foreach (var unit in units)
            foreach (var ft in action.FillTypes)
            {
                if (!unit.CanAccept(ft)) continue;
                var price = Price(trigger, ft);
                var amount = unit.Add(ft, Affordable(m.FarmId, unit.Free, price));
                if (amount <= 0f)
                {
                    why.Add("Not enough money");
                    continue;
                }
                Spend(m.FarmId, amount * price);
                _sim.Events.Publish(new FillBought(m, trigger.Poi, ft, amount, amount * price));
                done = true;
            }
        }
        if (!done) why.Add($"{m.Def.Name}: full, or takes nothing sold here");
        return done;
    }

    private bool Service(Machine m, PoiTrigger bay, PoiActionDef action, List<string> why)
    {
        var repair = action.Type == "repair";
        if ((repair ? 1f - m.Condition : m.Dirt) < 0.005f)
        {
            why.Add(repair ? "Nothing to repair" : "Nothing to wash");
            return false;
        }
        var cost = repair ? RepairPrice(m) : WashPrice(action, m);
        if (IsPlayers(m.FarmId) && cost > Economy.Money)
        {
            why.Add("Not enough money");
            return false;
        }
        Spend(m.FarmId, cost);
        if (repair)
        {
            m.Condition = 1f;
            _sim.Events.Publish(new MachineRepaired(m, bay.Poi, cost));
        }
        else
        {
            m.Dirt = 0f;
            _sim.Events.Publish(new MachineWashed(m, bay.Poi, cost));
        }
        return true;
    }

    // ------------------------------------------------------------------ Processing

    /// <summary>Runs an hour of every POI's processing (called for each world hour, so sleeping runs them too).</summary>
    internal void TickHour()
    {
        foreach (var poi in All)
        for (var i = 0; i < poi.Def.Actions.Length; i++)
        {
            var a = poi.Def.Actions[i];
            if (a.Type != "process") continue;
            if (!CanCycle(poi, a))
            {
                poi.Progress[i] = 0f;
                continue;
            }
            poi.Progress[i] += 1f / a.CycleHours;
            var cycles = 0;
            while (poi.Progress[i] >= 1f && CanCycle(poi, a))
            {
                foreach (var input in a.Inputs) poi.Storage!.Remove(input.FillType, input.Amount);
                foreach (var output in a.Outputs) poi.Storage!.Add(output.FillType, output.Amount);
                poi.Progress[i] -= 1f;
                cycles++;
            }
            Spend(poi.FarmId, a.RunningCost);
            if (cycles == 0) continue;
            foreach (var output in a.Outputs) _sim.Events.Publish(new PoiProduced(poi, output.FillType, output.Amount * cycles));
        }
    }

    /// <summary>True when the storage holds a cycle's inputs and has room for its outputs.</summary>
    private static bool CanCycle(Poi poi, PoiActionDef process) =>
        process.Inputs.All(x => poi.Storage!.Level(x.FillType) >= x.Amount - 0.001f) &&
        process.Outputs.All(x => poi.Storage!.Free(x.FillType) >= x.Amount - 0.001f);

    // ------------------------------------------------------------------ Money

    // The economy is the player's farm's: other farms (NPCs) trade without it.
    private bool IsPlayers(int farmId) => farmId == _sim.Farms.Player.Id;

    private void Earn(int farmId, float amount)
    {
        if (IsPlayers(farmId)) Economy.Earn(amount);
    }

    private void Spend(int farmId, float amount)
    {
        if (IsPlayers(farmId) && amount > 0f) Economy.Spend(amount);
    }

    private float Affordable(int farmId, float amount, float unitPrice) =>
        IsPlayers(farmId) ? Economy.Affordable(amount, unitPrice) : amount;
}
