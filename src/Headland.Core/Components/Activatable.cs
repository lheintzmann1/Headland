using System.Numerics;
using Headland.Core.Machines;

namespace Headland.Core.Components;

/// <summary>
/// Who presses the use key: the farmer on foot where they stand, or the chain of the vehicle they drive, for their
/// farm.
/// </summary>
public sealed record ActivationUser(int FarmId, Vector2 Position, Machine? Vehicle)
{
    /// <summary>The machines of the vehicle's chain (none on foot).</summary>
    public IEnumerable<Machine> Chain => Vehicle?.Chain() ?? [];

    /// <summary>The machines of the chain standing in <paramref name="area"/> (their footprint's center in it).</summary>
    public List<Machine> In(Obb area) => Chain.Where(m => area.Contains(m.Footprint.Center)).ToList();

    /// <summary>How far the user is from <paramref name="area"/>'s center: the farmer, or the nearest of <paramref name="machines"/>.</summary>
    public float Distance(Obb area, IEnumerable<Machine> machines) =>
        Vehicle == null ? Vector2.Distance(Position, area.Center) : machines.Min(m => Vector2.Distance(m.Footprint.Center, area.Center));

    /// <summary>
    /// The machines a bay serves this user (a workshop's, a wash bay): the vehicle's chain once some of it stands in
    /// the bay; on foot in the bay, the chain of the farm's machine standing there nearest to the farmer. Null when none.
    /// </summary>
    public (Machine root, float distance)? InBay(Obb bay, IEnumerable<Machine> machines)
    {
        if (Vehicle != null) return In(bay) is { Count: > 0 } parked ? (Vehicle, Distance(bay, parked)) : null;
        if (!bay.Contains(Position)) return null;
        var root = machines.Where(m => m.FarmId == FarmId && bay.Contains(m.Footprint.Center)).Select(m => m.Root).Distinct()
            .MinBy(m => Vector2.Distance(m.Footprint.Center, Position));
        return root != null ? (root, Vector2.Distance(Position, bay.Center)) : null;
    }
}

/// <summary>A screen the game opens for an activation, to ask the player first (what to load, which options).</summary>
public abstract record ActivationMenu;

/// <summary>
/// What the use key does where the user is (FS: an activatable): its label, the component offering it, how far the
/// user is from its trigger, and what it does, or why it can't now (closed, another farm's, nothing to do). A
/// <see cref="Menu"/> asks the player first where there's a screen to; without one, <see cref="Run"/> does the usual.
/// </summary>
public sealed record Activation(string Label, Component Source, float Distance)
{
    public Action? Run { get; init; }
    public string? Blocked { get; init; }
    public ActivationMenu? Menu { get; init; }

    public bool Usable => Blocked == null && Run != null;

    /// <summary>What several activations do, as one label: "Refuel, buy seeds".</summary>
    public static string Join(IReadOnlyList<string> parts) =>
        string.Join(", ", parts.Select((p, i) => i == 0 || p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}

/// <summary>
/// A component offering the use key something while the user is in its trigger, such as a gas station's pump or a
/// workshop's bay. It's asked every time, so what it offers is what applies now.
/// </summary>
public interface IActivatable
{
    IEnumerable<Activation> Activations(ActivationUser user, Simulation sim);
}
