using System.Numerics;
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
/// contracts on it.
/// </summary>
public sealed class Farms
{
    private readonly WorldMap _world;
    private readonly EventBus _events;

    public Farms(WorldMap world, EventBus events, string playerFarmName)
    {
        _world = world;
        _events = events;
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
}
