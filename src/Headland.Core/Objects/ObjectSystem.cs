using System.Numerics;
using Headland.Core.Machines;

namespace Headland.Core.Objects;

/// <summary>
/// The objects on the map (FS: bales, later pallets): made by machines (a baler drops a bale), carried by others (a bale
/// loader), and sold where a selling station takes them (its object trigger). Ids are never reused.
/// </summary>
public sealed class ObjectSystem(Simulation sim)
{
    public List<WorldObject> All { get; } = [];

    /// <summary>Id of the next object (ids are never reused).</summary>
    internal int NextId { get; set; } = 1;

    /// <summary>A new object of type <paramref name="defId"/> (objects/), lying on the ground.</summary>
    public WorldObject Spawn(string defId, Vector2 position, float heading, int farmId)
    {
        var o = new WorldObject(NextId++, sim.Content.Objects[defId], position, heading, farmId);
        All.Add(o);
        return o;
    }

    public WorldObject? ById(int id) => All.Find(o => o.Id == id);

    /// <summary>The objects lying loose with their middle in <paramref name="area"/>.</summary>
    public IEnumerable<WorldObject> LooseIn(Obb area) => All.Where(o => o.Holder == null && area.Contains(o.Position));

    /// <summary>Takes an object off the map (sold): what held it lets go.</summary>
    public void Remove(WorldObject o)
    {
        o.Holder?.Release(o);
        o.Holder = null;
        All.Remove(o);
    }

    /// <summary>Puts <paramref name="o"/> in <paramref name="holder"/>'s care: it's placed by it from now on.</summary>
    internal static void Hold(WorldObject o, IObjectHolder holder)
    {
        o.Holder = holder;
        Place(o);
    }

    /// <summary>Lets <paramref name="o"/> go where it is, <paramref name="elevation"/> over the ground.</summary>
    internal static void Drop(WorldObject o, float elevation = 0f)
    {
        o.Holder?.Release(o);
        o.Holder = null;
        o.Elevation = elevation;
    }

    /// <summary>Everything <paramref name="m"/>'s components hold is let go where it is: the machine leaves, or is rebuilt.</summary>
    internal void DropFrom(Machine m)
    {
        foreach (var o in All.Where(o => o.Holder?.Machine == m).ToList()) Drop(o);
    }

    /// <summary>Where a held object is, from its holder: on the map, and how high over the ground.</summary>
    internal static void Place(WorldObject o)
    {
        if (o.Holder is not { } h) return;
        var (at, yaw) = h.PoseOf(o);
        var (position, heading) = h.Machine.PartToWorld(at.X, at.Z);
        o.Position = position;
        o.Heading = MathUtil.WrapAngle(heading + yaw);
        o.Elevation = at.Y;
    }

    /// <summary>After the machines moved: the objects' components run.</summary>
    internal void Update(float dt)
    {
        foreach (var o in All)
        foreach (var c in o.Components)
            c.Update(sim, dt);
    }
}
