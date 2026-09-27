using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Pois.Components;

namespace Headland.Core.Pois;

/// <summary>
/// A point of interest placed on the map: a building or site of some farm or NPC. What it does comes from its
/// components (a selling station, a silo, a workshop…), each with the triggers where machines use it.
/// </summary>
public sealed class Poi : Entity
{
    public Poi(string id, PoiDef def, Vector2 position, float heading, int farmId, string? name = null)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        Name = name ?? def.Name;
        CreateComponents();
        Triggers = Components
            .SelectMany(c => c.Definition is PoiComponentDef d ? d.Triggers.Select(t => new PoiTrigger(this, c, t.type, t.area)) : [])
            .ToArray();
    }

    /// <summary>The placement id, unique on the map.</summary>
    public string Id { get; }
    public override PoiDef Def { get; }
    /// <summary>The type's name, or the one the map gives it.</summary>
    public override string Name { get; }
    /// <summary>Its components' triggers, in their order.</summary>
    public IReadOnlyList<PoiTrigger> Triggers { get; }

    public Obb Footprint => new(Position, new Vector2(Def.W * 0.5f, Def.D * 0.5f), Heading);

    /// <summary>Its first trigger of <paramref name="type"/> (unload, load, fill, repair, wash, delivery).</summary>
    public PoiTrigger? Trigger(string type) => Triggers.FirstOrDefault(t => t.Type == type);

    /// <summary>The ground a collider covers (a round one: the box around it).</summary>
    public Obb ColliderBox(PoiColliderDef collider) =>
        new(LocalToWorld(collider.X, collider.Z), new Vector2(collider.W * 0.5f, collider.D * 0.5f), Heading + collider.RotDeg * MathUtil.Deg2Rad);

    public override string ToString() => $"{Name} ({Id})";
}
