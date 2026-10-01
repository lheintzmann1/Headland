using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Objects;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Tines or spikes that carry objects (FS: a bale spike's or pallet fork's dynamic mount): an object of a kind it takes,
/// lying loose with its middle in its <see cref="Area"/>, is picked up once the tines are at its height, how far up it
/// they go in (<see cref="Into"/>: a bale spike's at mid-height, a pallet fork's in the pallet's openings), and rides on
/// them as the loader arm lifts and tilts. The unload key (and the mouse's tool action) sets it down: on the ground, or
/// on what lies under it (a stack).
/// </summary>
public sealed class ForkDef : MachineComponentDef
{
    /// <summary>Kinds of objects there are (the components they have): what it can take.</summary>
    public static readonly string[] Kinds = ["bale", "pallet"];

    /// <summary>Where its tines reach, in its space: an object's middle in it.</summary>
    public AreaDef Area { get; set; } = new() { Z = 0.8f, W = 1.2f, D = 1.2f };
    /// <summary>How high its tines are over its origin.</summary>
    public float Y { get; set; }
    /// <summary>How far up an object its tines go in, from its bottom.</summary>
    public float Into { get; set; } = 0.1f;
    /// <summary>The kinds of objects it takes (<see cref="Kinds"/>).</summary>
    public string[] Takes { get; set; } = ["bale"];
    /// <summary>How many it carries at once.</summary>
    public int Capacity { get; set; } = 1;

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Area.Error() is { } error) yield return $"area: {error}";
        if (Takes.Length == 0) yield return "needs takes: the kinds of objects it carries";
        foreach (var k in Takes.Where(k => !Kinds.Contains(k))) yield return $"unknown kind '{k}' ({string.Join(", ", Kinds)})";
        if (Capacity < 1) yield return "capacity must be >= 1";
        if (Into < 0f) yield return "into must be >= 0";
    }

    internal override Component Create(Machine machine) => new Fork(machine, this);
}

public sealed class Fork(Machine machine, ForkDef def) : MachineComponent<ForkDef>(machine, def), IActionSource, IObjectHolder
{
    /// <summary>How near its tines must be to where they go into an object to take it.</summary>
    public const float Reach = 0.25f;

    private readonly List<(WorldObject o, Vector3 at, float yaw)> _held = [];
    /// <summary>What it just set down: left alone until the tines are away from it.</summary>
    private readonly HashSet<WorldObject> _left = [];

    public IReadOnlyList<WorldObject> Held => _held.Select(h => h.o).ToList();

    /// <summary>How high its tines are over the ground now, at the middle of its area.</summary>
    public float TineHeight => Machine.HeightOf(new Vector3(Def.Area.X, Def.Y, Def.Area.Z));

    public bool Takes(WorldObject o) => o.FarmId == Machine.FarmId && o.Components.Any(c => Def.Takes.Contains(c.Definition.Kind));

    /// <summary>Whether its tines go into <paramref name="o"/> where they are: at its height, its middle among them.</summary>
    private bool AtTines(WorldObject o, float tines) =>
        Def.Area.On(Machine).Contains(o.Position) && MathF.Abs(o.Elevation + Def.Into - tines) <= Reach;

    /// <summary>The unload key, and the mouse's action on the tool, set down what it carries.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (_held.Count == 0) return;
        var what = _held[0].o.Def.Name.ToLowerInvariant();
        var label = _held.Count == 1 ? $"Set the {what} down" : $"Set the {what}s down";
        actions.Add(InputActions.Unload, label, () => SetDown(sim));
        actions.Add(InputActions.ToolAction, label, () => SetDown(sim), hinted: false);
    }

    internal override void Update(Simulation sim, float dt)
    {
        foreach (var (o, _, _) in _held) ObjectSystem.Place(o);
        var tines = TineHeight;
        _left.RemoveWhere(o => o.Holder != null || !sim.Objects.All.Contains(o) || !AtTines(o, tines));
        if (_held.Count >= Def.Capacity) return;
        foreach (var o in sim.Objects.LooseIn(Def.Area.On(Machine)).Where(o => !_left.Contains(o) && Takes(o) && AtTines(o, tines)).ToList())
        {
            var local = MathUtil.WorldToLocal(Machine.Position, Machine.Heading, o.Position);
            _held.Add((o, new Vector3(local.X, Def.Y - Def.Into, local.Y), MathUtil.WrapAngle(o.Heading - Machine.Heading)));
            ObjectSystem.Hold(o, this);
            if (_held.Count >= Def.Capacity) break;
        }
    }

    /// <summary>Sets what it carries down where it is: on the ground, or on what lies under it.</summary>
    public void SetDown(Simulation sim)
    {
        foreach (var (o, _, _) in _held.ToList())
        {
            ObjectSystem.Place(o);
            ObjectSystem.Drop(o, sim.Objects.TopAt(o.Position, o));
            _left.Add(o);
        }
    }

    public (Vector3 at, float yaw) PoseOf(WorldObject o)
    {
        foreach (var h in _held)
            if (h.o == o) return (h.at, h.yaw);
        return (new Vector3(0f, Def.Y - Def.Into, Def.Area.Z), 0f);
    }

    public int SlotOf(WorldObject o) => _held.FindIndex(h => h.o == o);

    /// <summary>Takes back what it carried when saved, where it lies from it now.</summary>
    public bool Restore(WorldObject o, int slot)
    {
        if (_held.Count >= Def.Capacity) return false;
        var local = MathUtil.WorldToLocal(Machine.Position, Machine.Heading, o.Position);
        _held.Add((o, new Vector3(local.X, Def.Y - Def.Into, local.Y), MathUtil.WrapAngle(o.Heading - Machine.Heading)));
        ObjectSystem.Hold(o, this);
        return true;
    }

    public void Release(WorldObject o) => _held.RemoveAll(h => h.o == o);
}
