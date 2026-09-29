using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;

namespace Headland.Core.Machines;

/// <summary>
/// A machine leased at the shop (FS: leasing): the farm paid a fee for it, and pays for each hour it runs (its
/// <see cref="Machine.OperatingHours"/>, from new) until it gives it back.
/// </summary>
public sealed class MachineLease
{
    /// <summary>Paid when it was leased.</summary>
    public float Fee { get; init; }
    /// <summary>What an hour of running costs, agreed when it was leased.</summary>
    public float PerHour { get; init; }
    /// <summary>What its hours cost so far: paid in whole dollars as they add up.</summary>
    public float Paid { get; set; }
}

/// <summary>
/// The machine shop (FS: the store): the machines for sale in the shop's categories (shopcategories.json) and by brand
/// (brands.json), what they are (<see cref="Spec"/>s from their components) and what they cost with the options picked,
/// at the difficulty's price level. Machines bought or leased wait on the lot of a dealer's delivery spot. A leased one
/// costs a share of its price up front, then a share for each hour it runs (economy.json's leasing).
/// </summary>
public sealed class Shop(Simulation sim)
{
    private ContentDatabase Content => sim.Content;
    private Economy Economy => sim.Economy;

    /// <summary>The categories with machines for sale (of <paramref name="brand"/>, when given), in the shop's order.</summary>
    public IEnumerable<ShopCategoryDef> Categories(BrandDef? brand = null) =>
        Content.ShopCategories.Values.Where(c => Machines(c, brand).Any());

    /// <summary>The machines for sale in <paramref name="category"/> (of <paramref name="brand"/>, when given), cheapest first.</summary>
    public IEnumerable<MachineDef> Machines(ShopCategoryDef category, BrandDef? brand = null) => Content.Machines.Values
        .Where(m => m.Category == category.Id && (brand == null || m.Brand == brand.Id))
        .OrderBy(m => m.Price)
        .ThenBy(m => m.Name, StringComparer.Ordinal);

    /// <summary>The brands of the machines for sale, by name.</summary>
    public IEnumerable<BrandDef> Brands => Content.Brands.Values
        .Where(b => Content.Machines.Values.Any(m => m.Brand == b.Id && Content.ShopCategories.ContainsKey(m.Category)))
        .OrderBy(b => b.Name, StringComparer.Ordinal);

    public BrandDef? BrandOf(MachineDef def) => Content.Brands.GetValueOrDefault(def.Brand);

    /// <summary>What <paramref name="def"/> costs the farm with its options, at the difficulty's price level.</summary>
    public float Price(MachineDef def) => MathF.Round(def.Price * Economy.PriceLevel);

    /// <summary>What <paramref name="def"/> is with its options: what its components bring, then its mass.</summary>
    public IReadOnlyList<Spec> Specs(MachineDef def) => Spec.Of(def, Content).Append(new Spec("Mass", $"{def.Mass:N0} kg")).ToList();

    /// <summary>What leasing <paramref name="def"/> costs: a share of its price up front, and one for each hour it runs.</summary>
    public (float fee, float perHour) LeaseTerms(MachineDef def)
    {
        var (price, terms) = (Price(def), Content.Economy.Leasing);
        return (MathF.Round(price * terms.Upfront), MathF.Round(price * terms.PerHour));
    }

    /// <summary>Why the farm can't buy <paramref name="def"/> now, or null when it can.</summary>
    public string? BuyBlocker(MachineDef def) => Blocker(def, Price(def));

    /// <summary>Why the farm can't lease <paramref name="def"/> now, or null when it can.</summary>
    public string? LeaseBlocker(MachineDef def) => Blocker(def, LeaseTerms(def).fee);

    /// <summary>It isn't sold, the farm can't pay <paramref name="cost"/>, or no dealer can deliver it.</summary>
    private string? Blocker(MachineDef def, float cost)
    {
        if (!Content.ShopCategories.ContainsKey(def.Category)) return $"The {def.Name} is not for sale";
        return cost > Economy.Money ? "Not enough money" : sim.Pois.DeliveryBlocker(def);
    }

    /// <summary>
    /// Buys <paramref name="def"/> (with its options) for the player's farm: paid for, it waits on a dealer's lot. Null,
    /// with a notification, when it can't be bought.
    /// </summary>
    public Machine? Buy(MachineDef def)
    {
        if (BuyBlocker(def) is { } why)
        {
            sim.Notifications.Post(why, Severity.Warning);
            return null;
        }
        var price = Price(def);
        var (machine, poi) = sim.Pois.Deliver(def, sim.Farms.Player.Id)!.Value;
        Economy.Spend(price, MoneyCategory.Machines);
        sim.Events.Publish(new MachineBought(machine, poi, price));
        return machine;
    }

    /// <summary>
    /// Leases <paramref name="def"/> (with its options) for the player's farm: its fee paid, it waits on a dealer's lot,
    /// and costs its hours as it runs. Null, with a notification, when it can't be leased.
    /// </summary>
    public Machine? Lease(MachineDef def)
    {
        if (LeaseBlocker(def) is { } why)
        {
            sim.Notifications.Post(why, Severity.Warning);
            return null;
        }
        var (fee, perHour) = LeaseTerms(def);
        var (machine, poi) = sim.Pois.Deliver(def, sim.Farms.Player.Id)!.Value;
        machine.Lease = new MachineLease { Fee = fee, PerHour = perHour };
        Economy.Spend(fee, MoneyCategory.Leasing);
        sim.Events.Publish(new MachineLeased(machine, poi, machine.Lease));
        return machine;
    }

    /// <summary>After the machines moved: the hours leased machines ran cost the farm, in whole dollars as they add up.</summary>
    internal void Update()
    {
        foreach (var m in sim.Machines.All)
        {
            if (m.Lease is not { } lease || Math.Floor(m.OperatingHours * lease.PerHour - lease.Paid) is not (var due and >= 1.0)) continue;
            lease.Paid += (float)due;
            if (m.FarmId == sim.Farms.Player.Id) Economy.Spend((float)due, MoneyCategory.Leasing);
        }
    }
}
