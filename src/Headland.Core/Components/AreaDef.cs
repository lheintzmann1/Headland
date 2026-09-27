using System.Numerics;
using Headland.Core.Machines;

namespace Headland.Core.Components;

/// <summary>An area on the ground, in its entity's space: centered on <see cref="X"/>, <see cref="Z"/>, w along x and d along z.</summary>
public class AreaDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float W { get; set; } = 10f;
    public float D { get; set; } = 10f;

    /// <summary>Where it lies on the map, with its entity where it is now.</summary>
    public Obb On(Entity owner) => new(owner.LocalToWorld(X, Z), new Vector2(W * 0.5f, D * 0.5f), owner.Heading);

    internal virtual string? Error() => W > 0f && D > 0f ? null : "needs w and d > 0";
}

/// <summary>
/// An area that senses who is in it, as FS triggers react to the player, vehicles or both: a door opens, a lamp comes
/// on. What counts is a machine's footprint center, or where the farmer stands (in a vehicle: the vehicle's).
/// </summary>
public sealed class TriggerDef : AreaDef
{
    /// <summary>Who it reacts to.</summary>
    public static readonly string[] Senses = ["anyone", "farmer", "machines"];

    /// <summary>One of <see cref="Senses"/>: the farmer on foot or in a vehicle, machines (driven or not), or both.</summary>
    public string By { get; set; } = "anyone";

    /// <summary>Whether someone it reacts to is in it now.</summary>
    public bool Occupied(Entity owner, Simulation sim)
    {
        var area = On(owner);
        return By != "machines" && area.Contains(sim.Player.Position)
               || By != "farmer" && sim.Machines.All.Any(m => area.Contains(m.Footprint.Center));
    }

    internal override string? Error() => base.Error() ?? (Senses.Contains(By) ? null : $"by must be {string.Join(", ", Senses)}");
}
