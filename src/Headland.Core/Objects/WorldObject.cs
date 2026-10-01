using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;

namespace Headland.Core.Objects;

/// <summary>
/// A thing lying about that machines pick up and carry (FS: objects): a bale. Its type (<see cref="Def"/>) lists its
/// components, and a bale keeps what it's made of in its fill unit. Lying loose it rests on the ground (or on others, by
/// its <see cref="Elevation"/>); carried, its <see cref="Holder"/> places it as its machine moves.
/// </summary>
public sealed class WorldObject : Entity
{
    public WorldObject(int id, ObjectDef def, Vector2 position, float heading, int farmId)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        CreateComponents();
    }

    public int Id { get; }
    public override ObjectDef Def { get; }

    /// <summary>How high its bottom is over the ground: on a stack, or carried (in its holder's machine's space).</summary>
    public float Elevation { get; set; }

    /// <summary>What carries it (a bale loader's bed), or null lying loose.</summary>
    public IObjectHolder? Holder { get; internal set; }

    /// <summary>The ground it covers, its origin at the middle.</summary>
    public Obb Footprint => new(Position, new Vector2(Def.Size.Width * 0.5f, Def.Size.Length * 0.5f), Heading);

    /// <summary>What it holds (a bale's grass): its first fill unit, if it has one.</summary>
    public FillUnit? Content => FillUnits.Count > 0 ? FillUnits[0] : null;

    public override string ToString() => $"{Def.Name} #{Id}";
}
