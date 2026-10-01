using System.Numerics;
using Headland.Core.Machines;

namespace Headland.Core.Objects;

/// <summary>
/// A machine's component carrying objects (FS: a bale loader's bed, later a fork): it holds each in a slot, and places
/// it there as the machine moves (<see cref="ObjectSystem.Place"/>).
/// </summary>
public interface IObjectHolder
{
    Machine Machine { get; }

    /// <summary>
    /// Where it holds <paramref name="o"/>: the middle of its bottom in the machine's space (x left, y up, z forward),
    /// and its turn from the machine's heading (radians).
    /// </summary>
    (Vector3 at, float yaw) PoseOf(WorldObject o);

    /// <summary>The slot <paramref name="o"/> is in: saves keep it.</summary>
    int SlotOf(WorldObject o);

    /// <summary>Takes <paramref name="o"/> back into <paramref name="slot"/>, as a save had it; false when it can't.</summary>
    bool Restore(WorldObject o, int slot);

    /// <summary>Lets go of <paramref name="o"/>: it was taken away (sold) or dropped.</summary>
    void Release(WorldObject o);
}
