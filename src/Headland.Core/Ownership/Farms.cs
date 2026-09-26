using System.Numerics;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.World;

namespace Headland.Core.Ownership;

/// <summary>Something a farm can own: machines, farmland, and later storage and buildings.</summary>
public interface IOwnable
{
    /// <summary>The owning farm, or <see cref="Farm.None"/> when an NPC (or nobody) owns it.</summary>
    int FarmId { get; }
}

public sealed class Farm(int id, string name)
{
    /// <summary>Farm id meaning "no farm": land and machines of NPCs.</summary>
    public const int None = 0;
    /// <summary>The player's farm (FS numbers farms from 1 too).</summary>
    public const int PlayerId = 1;

    public int Id { get; } = id;
    public string Name { get; set; } = name;
}

/// <summary>
/// Farms and who owns what. Farmland not owned by a farm belongs to its NPC, who sells it and (later) offers
/// contracts on it. The player's farm buys parcels from their NPC and sells them back, for the parcel's price.
/// </summary>
public sealed class Farms
{
    private readonly WorldMap _world;
    private readonly EventBus _events;
    private readonly Economy _economy;

    public Farms(WorldMap world, EventBus events, Economy economy, string playerFarmName)
    {
        _world = world;
        _events = events;
        _economy = economy;
        Player = new Farm(Farm.PlayerId, playerFarmName);
        All = [Player];
    }

    public Farm Player { get; }
    public IReadOnlyList<Farm> All { get; }

    public Farm? ById(int id) => All.FirstOrDefault(f => f.Id == id);

    /// <summary>Hands a parcel to <paramref name="farmId"/> (or back to its NPC with <see cref="Farm.None"/>).</summary>
    public void SetOwner(Farmland land, int farmId)
    {
        if (land.FarmId == farmId) return;
        if (farmId != Farm.None && ById(farmId) == null) throw new ArgumentException($"No farm {farmId}", nameof(farmId));
        var from = land.FarmId;
        land.FarmId = farmId;
        _events.Publish(new FarmlandOwnerChanged(land, from, farmId));
    }

    /// <summary>The farm's name, or the NPC's for land no farm owns.</summary>
    public string OwnerName(Farmland land) => ById(land.FarmId)?.Name ?? land.Npc.Name;

    /// <summary>True if the ground at <paramref name="p"/> is farmland owned by <paramref name="farmId"/>.</summary>
    public bool Owns(int farmId, Vector2 p) => farmId != Farm.None && _world.FarmlandAt(p)?.FarmId == farmId;

    public IEnumerable<Farmland> FarmlandOf(int farmId) => _world.Farmlands.Where(l => l.FarmId == farmId);

    /// <summary>What <paramref name="land"/> costs the player's farm at its price level, and pays when sold back.</summary>
    public float Price(Farmland land) => MathF.Round(land.Price * _economy.PriceLevel);

    /// <summary>Why the player's farm can't buy <paramref name="land"/>, or null if it can.</summary>
    public string? BuyBlocker(Farmland land)
    {
        if (land.FarmId == Player.Id) return $"{land.Label} is yours already";
        if (land.FarmId != Farm.None) return $"{land.Label} belongs to {OwnerName(land)}";
        return Price(land) > _economy.Money ? "Not enough money" : null;
    }

    /// <summary>Why the player's farm can't sell <paramref name="land"/>, or null if it can: its buildings stand on it.</summary>
    public string? SellBlocker(Farmland land)
    {
        if (land.FarmId != Player.Id) return $"{land.Label} is not yours";
        return _world.Pois.FirstOrDefault(p => p.FarmId == Player.Id && _world.FarmlandAt(p.Position) == land) is { } poi
            ? $"{poi.Name} stands on {land.Label}"
            : null;
    }

    public bool Buy(Farmland land)
    {
        if (BuyBlocker(land) != null) return false;
        var price = Price(land);
        _economy.Spend(price, MoneyCategory.Land);
        SetOwner(land, Player.Id);
        _events.Publish(new FarmlandBought(land, Player.Id, price));
        return true;
    }

    public bool Sell(Farmland land)
    {
        if (SellBlocker(land) != null) return false;
        var price = Price(land);
        _economy.Earn(price, MoneyCategory.Land);
        SetOwner(land, Farm.None);
        _events.Publish(new FarmlandSold(land, Player.Id, price));
        return true;
    }
}
