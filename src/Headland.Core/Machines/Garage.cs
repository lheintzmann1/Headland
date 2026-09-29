using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.Pois;
using Headland.Core.Pois.Components;

namespace Headland.Core.Machines;

/// <summary>
/// The farm's machines (FS: the garage): where each is, what it's worth, selling it back to the dealer (a leased one is
/// given back), and a workshop's services wherever it stands: repairs, a repaint and new options. A workshop does them
/// at its prices for machines in its bay; elsewhere the nearest sends a mechanic, for economy.json's remoteService times
/// as much, while it's open. Machines grow older by a month as each month starts.
/// </summary>
public sealed class Garage
{
    private readonly Simulation _sim;

    public Garage(Simulation sim)
    {
        _sim = sim;
        sim.Events.Subscribe<MonthStarted>(_ =>
        {
            foreach (var m in sim.Machines.All) m.AgeMonths++;
        });
    }

    private ContentDatabase Content => _sim.Content;
    private int Player => _sim.Farms.Player.Id;

    /// <summary>The player's farm's machines, leased ones too, in the shop's order: by category, cheapest first.</summary>
    public IEnumerable<Machine> Machines
    {
        get
        {
            var order = Content.ShopCategories.Keys.ToList();
            return _sim.Machines.All
                .Where(m => m.FarmId == Player)
                .OrderBy(m => order.IndexOf(m.Def.Category) is var i and >= 0 ? i : int.MaxValue)
                .ThenBy(m => m.Def.Price)
                .ThenBy(m => m.Def.Name, StringComparer.Ordinal)
                .ThenBy(m => m.Id);
        }
    }

    /// <summary>Where <paramref name="m"/> is: the field it's on, the POI it's at, else how far it is from the nearest.</summary>
    public string Location(Machine m)
    {
        var world = _sim.World;
        var p = m.Footprint.Center;
        var (cx, cz) = world.WorldToCell(p);
        if (world.InBounds(cx, cz) && world.Layers.FieldId[world.CellIndex(cx, cz)] is var id and > 0) return world.FieldById(id)!.Label;
        if (world.Pois.MinBy(poi => poi.Footprint.Distance(p)) is not { } near) return "Out in the country";
        var distance = near.Footprint.Distance(p);
        return distance < 5f ? $"At {near.Name}" : $"{MathF.Max(10f, MathF.Round(distance / 10f) * 10f):N0} m from {near.Name}";
    }

    /// <summary>
    /// What <paramref name="m"/> sells for (FS): its price times the share of its life its hours leave and a share for
    /// its age, less what its repairs and repaint would cost, and never under a floor (economy.json's resale).
    /// </summary>
    public float Value(Machine m)
    {
        var price = _sim.Shop.Price(m.Def);
        var resale = Content.Economy.Resale;
        // An implement's hours count for more: it's hitched to one engine after another.
        var hours = m.Has<Motor>() ? m.OperatingHours : Math.Pow(m.OperatingHours, 1.3);
        var life = Math.Max(0.0, 1.0 - hours / resale.LifetimeHours);
        var age = Math.Min(resale.NewShare, 0.75 - 0.1 * Math.Log(m.AgeMonths / 12.0));
        var wear = m.Get<Wearable>();
        var repairs = price / 100f * (wear?.Wear ?? 0f) + price * PoiSystem.RepaintShare * (wear?.PaintWear ?? 0f);
        return MathF.Round(MathF.Max((float)(price * life * age) - repairs, price * resale.MinShare));
    }

    // ------------------------------------------------------------------ Selling

    /// <summary>Why <paramref name="m"/> can't be sold (or given back) now, or null when it can.</summary>
    public string? SellBlocker(Machine m)
    {
        if (m.FarmId != Player) return $"The {m.Def.Name} is not the farm's";
        if (m.LeaseContract != 0) return "It goes back when its contract ends";
        return m.Root.Get<Drivable>()?.Controller is FieldWorkController ? "The helper is working: dismiss them first" : null;
    }

    /// <summary>
    /// Sells <paramref name="m"/> back to the dealer for its <see cref="Value"/>, or gives it back when it's leased:
    /// it leaves the map, and what hangs on it stays, unhitched. False, with a notification, when it can't.
    /// </summary>
    public bool Sell(Machine m)
    {
        if (SellBlocker(m) is { } why)
        {
            _sim.Notifications.Post(why, Severity.Warning);
            return false;
        }
        var price = Value(m);
        _sim.RemoveMachines([m]);
        if (m.Lease is { } lease)
        {
            _sim.Events.Publish(new MachineReturned(m, lease));
            return true;
        }
        _sim.Economy.Earn(price, MoneyCategory.Machines);
        _sim.Events.Publish(new MachineSold(m, price));
        return true;
    }

    // ------------------------------------------------------------------ Services

    /// <summary>
    /// The workshop serving <paramref name="m"/> (one fitting options, for <paramref name="options"/>) and the factor on
    /// its prices: the one whose bay it or its chain stands in, at its prices; else the nearest, coming out for more.
    /// Null on a map without one.
    /// </summary>
    public (Workshop workshop, float factor)? Service(Machine m, bool options = false)
    {
        var workshops = _sim.World.Pois
            .Select(p => p.Get<Workshop>())
            .OfType<Workshop>()
            .Where(w => !options || w.Def.Configure != null)
            .ToList();
        if (workshops.Find(w => m.Root.Chain().Any(x => w.Bay.Contains(x.Footprint.Center))) is { } bay) return (bay, 1f);
        return workshops.MinBy(w => Vector2.Distance(w.Poi.Position, m.Position)) is { } nearest ? (nearest, Content.Economy.RemoteService) : null;
    }

    public float RepairPrice(Machine m) => Service(m) is var (w, factor) ? _sim.Pois.RepairPrice(w, m, factor) : 0f;

    public float RepaintPrice(Machine m) => Service(m) is var (w, factor) ? _sim.Pois.RepaintPrice(w, m, factor) : 0f;

    public string? RepairBlocker(Machine m) =>
        Blocker(m) ?? (Service(m) is var (w, factor) ? _sim.Pois.RepairBlocker(m, w, factor) : NoWorkshop);

    public string? RepaintBlocker(Machine m) =>
        Blocker(m) ?? (Service(m) is var (w, factor) ? _sim.Pois.RepaintBlocker(m, w, factor) : NoWorkshop);

    /// <summary>Repairs <paramref name="m"/> where it stands (see <see cref="Service"/>); false, with a notification, when it can't be.</summary>
    public bool Repair(Machine m) => Serve(m, RepairBlocker(m), (w, factor) => _sim.Pois.Repair([m], w, factor));

    /// <summary>Repaints <paramref name="m"/> where it stands (see <see cref="Service"/>); false, with a notification, when it can't be.</summary>
    public bool Repaint(Machine m) => Serve(m, RepaintBlocker(m), (w, factor) => _sim.Pois.Repaint([m], w, factor));

    /// <summary>What giving <paramref name="m"/> the options of <paramref name="def"/> costs where it stands.</summary>
    public float ConfigurePrice(Machine m, MachineDef def) =>
        Service(m, options: true) is var (w, factor) ? _sim.Pois.ConfigurePrice(w, m, def, factor) : 0f;

    /// <summary>Why <paramref name="m"/> can't be given the options of <paramref name="def"/> where it stands, or null when it can.</summary>
    public string? ConfigureBlocker(Machine m, MachineDef def) =>
        Blocker(m) ?? (Service(m, options: true) is var (w, factor) ? _sim.Pois.ConfigureBlocker(m, def, w, factor) : "No workshop fits options on this map");

    /// <summary>
    /// Gives <paramref name="m"/> the options <paramref name="choices"/> picks (over the ones it has) where it stands, for
    /// their price (see <see cref="Service"/>). False, with a notification, when it can't.
    /// </summary>
    public bool Configure(Machine m, IReadOnlyDictionary<string, string> choices)
    {
        var def = m.Def.Configure(choices);
        if (ConfigureBlocker(m, def) is { } why)
        {
            _sim.Notifications.Post(why, Severity.Warning);
            return false;
        }
        var (workshop, factor) = Service(m, options: true)!.Value;
        return _sim.Pois.Configure(m, def, workshop, factor);
    }

    private const string NoWorkshop = "No workshop on this map";

    /// <summary>Only the farm's machines are serviced.</summary>
    private string? Blocker(Machine m) => m.FarmId != Player ? $"The {m.Def.Name} is not the farm's" : null;

    private bool Serve(Machine m, string? blocker, Action<Workshop, float> serve)
    {
        if (blocker != null)
        {
            _sim.Notifications.Post(blocker, Severity.Warning);
            return false;
        }
        var (workshop, factor) = Service(m)!.Value;
        serve(workshop, factor);
        return true;
    }
}
