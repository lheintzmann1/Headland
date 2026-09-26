using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Ownership;

namespace Headland.Core.Pois;

/// <summary>A point of interest placed on the map: a building or site of some farm or NPC.</summary>
public sealed class Poi : IOwnable
{
    public Poi(string id, PoiDef def, Vector2 position, float heading, int farmId, string? name = null)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        Name = name ?? def.Name;
        Triggers = def.Triggers.Select(t => new PoiTrigger(this, t)).ToArray();
    }

    /// <summary>The placement id, unique on the map.</summary>
    public string Id { get; }
    public PoiDef Def { get; }
    public string Name { get; }
    /// <summary>Center of the footprint (the POI's origin).</summary>
    public Vector2 Position { get; }
    /// <summary>Heading of the POI's front (+Z), as for machines.</summary>
    public float Heading { get; }
    /// <summary>Owning farm (<see cref="Farm.None"/> = an NPC's).</summary>
    public int FarmId { get; internal set; }
    public IReadOnlyList<PoiTrigger> Triggers { get; }

    public Obb Footprint => new(Position, new Vector2(Def.W * 0.5f, Def.D * 0.5f), Heading);

    public PoiTrigger? Trigger(string id) => Triggers.FirstOrDefault(t => t.Id == id);

    public Vector2 LocalToWorld(float x, float z) => MathUtil.LocalToWorld(Position, Heading, new Vector2(x, z));

    /// <summary>The ground a part covers (round parts: the box around them).</summary>
    public Obb PartBox(PoiPartDef part) =>
        new(LocalToWorld(part.X, part.Z), new Vector2(part.W * 0.5f, part.D * 0.5f), Heading + part.RotDeg * MathUtil.Deg2Rad);

    public override string ToString() => $"{Name} ({Id})";
}
