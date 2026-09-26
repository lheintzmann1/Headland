using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;

namespace Headland.Core.Pois;

/// <summary>A load being unloaded at a POI, totalled until the machine stops.</summary>
internal sealed record Delivery(Poi Poi, string FillType, float Amount, float Income);

/// <summary>
/// What machines do at POIs: loads tipped into an unload trigger are sold, and supplies are bought at a fill trigger.
/// Machines move the goods (tipping, filling); the POI's actions decide what happens to them.
/// </summary>
public sealed class PoiSystem
{
    private readonly Simulation _sim;
    private readonly Dictionary<Machine, Delivery> _deliveries = new();
    private readonly HashSet<Machine> _unloading = [];

    public PoiSystem(Simulation sim) => _sim = sim;

    public IReadOnlyList<Poi> All => _sim.World.Pois;

    /// <summary>Loads being unloaded, by machine: published as one sale when the machine stops.</summary>
    internal Dictionary<Machine, Delivery> Deliveries => _deliveries;

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

    /// <summary>What the farmer can do at a trigger, one line per action ("Sell wheat, barley").</summary>
    public IEnumerable<string> Describe(PoiTrigger trigger) => trigger.Actions.Select(a => a.Type switch
    {
        "sell" => $"Sell {Names(a.FillTypes)}",
        "buy" => $"Buy {Names(a.FillTypes)}",
        _ => a.Type,
    });

    private string Names(IEnumerable<string> fillTypes) =>
        string.Join(", ", fillTypes.Select(f => Content.FillTypes[f].Name.ToLowerInvariant()));

    // ------------------------------------------------------------------ Unloading

    /// <summary>Why <paramref name="m"/> can't unload <paramref name="fillType"/> at <paramref name="trigger"/>, or null if it can.</summary>
    public string? UnloadBlocker(Machine m, PoiTrigger trigger, string fillType) =>
        trigger.Actions.Any(a => a.Type == "sell" && a.FillTypes.Contains(fillType))
            ? null
            : $"{trigger.Poi.Name} does not buy {Content.FillTypes[fillType].Name}";

    /// <summary>Takes up to <paramref name="amount"/> from <paramref name="m"/> unloading at <paramref name="trigger"/>; returns what it took.</summary>
    public float Unload(Machine m, PoiTrigger trigger, string fillType, float amount)
    {
        if (amount <= 0f || UnloadBlocker(m, trigger, fillType) != null) return 0f;
        var income = amount * Price(trigger, fillType);
        Earn(m.FarmId, income);
        if (_deliveries.TryGetValue(m, out var d) && (d.Poi != trigger.Poi || d.FillType != fillType)) Flush(m);
        d = _deliveries.GetValueOrDefault(m) ?? new Delivery(trigger.Poi, fillType, 0f, 0f);
        _deliveries[m] = d with { Amount = d.Amount + amount, Income = d.Income + income };
        _unloading.Add(m);
        return amount;
    }

    /// <summary>After the machines moved: machines that stopped unloading this tick publish their sale.</summary>
    internal void Update()
    {
        foreach (var m in _deliveries.Keys.Where(m => !_unloading.Contains(m)).ToList()) Flush(m);
        _unloading.Clear();
    }

    private void Flush(Machine m)
    {
        if (!_deliveries.Remove(m, out var d) || d.Amount < 1f) return;
        _sim.Events.Publish(new FillSold(m, d.Poi, d.FillType, d.Amount, d.Income));
    }

    // ------------------------------------------------------------------ Filling

    /// <summary>Fills every machine of the chain parked in a fill trigger with what the POI sells there.</summary>
    public void Fill(Machine vehicle)
    {
        var parked = false;
        var bought = false;
        foreach (var m in vehicle.Chain())
        {
            if (TriggerAt(m.Footprint.Center, "fill") is not { } trigger) continue;
            parked = true;
            foreach (var action in trigger.Actions.Where(a => a.Type == "buy"))
            foreach (var unit in m.FillUnits)
            foreach (var ft in action.FillTypes)
            {
                if (!unit.CanAccept(ft)) continue;
                var price = Price(trigger, ft);
                var amount = unit.Add(ft, Affordable(m.FarmId, unit.Free, price));
                if (amount <= 0f) continue;
                Spend(m.FarmId, amount * price);
                _sim.Events.Publish(new FillBought(m, trigger.Poi, ft, amount, amount * price));
                bought = true;
            }
        }
        if (!parked) _sim.Notifications.Post("Park the machine in a shop's fill area to buy supplies");
        else if (!bought) _sim.Notifications.Post(Economy.Money <= 0f ? "Not enough money" : "Nothing to buy here: full, or nothing sold here fits");
    }

    // ------------------------------------------------------------------ Money

    // The economy is the player's farm's: other farms (NPCs) trade without it.
    private bool IsPlayers(int farmId) => farmId == _sim.Farms.Player.Id;

    private void Earn(int farmId, float amount)
    {
        if (IsPlayers(farmId)) Economy.Earn(amount);
    }

    private void Spend(int farmId, float amount)
    {
        if (IsPlayers(farmId)) Economy.Spend(amount);
    }

    private float Affordable(int farmId, float amount, float unitPrice) =>
        IsPlayers(farmId) ? Economy.Affordable(amount, unitPrice) : amount;
}
