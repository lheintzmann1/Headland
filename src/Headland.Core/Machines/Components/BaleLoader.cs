using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Objects;
using Headland.Core.Objects.Components;

namespace Headland.Core.Machines.Components;

/// <summary>
/// A bale collector (FS: BaleLoader): turned on, it grabs the bales of its farm lying in its pickup area, one at a time,
/// onto the slots of its bed; the unload key sets them all down behind it, standing still, in the same rows (in a
/// selling station's object trigger, they're sold).
/// </summary>
public sealed class BaleLoaderDef : MachineComponentDef, ISpecSource
{
    /// <summary>Where the bales sit on it, [x, y, z] in its space (y: their bottom): one for each bale it carries.</summary>
    public float[][] Slots { get; set; } = [];
    /// <summary>Where it grabs bales lying on the ground: their middle in it.</summary>
    public AreaDef Pickup { get; set; } = new() { W = 2f, D = 2f };
    /// <summary>Seconds a grab takes: it takes one bale at a time.</summary>
    public float GrabSeconds { get; set; } = 1.5f;
    /// <summary>The bale shapes it takes (round, square); none: any.</summary>
    public string[] Shapes { get; set; } = [];
    /// <summary>Where the front row lands when it unloads, along z (behind it).</summary>
    public float UnloadZ { get; set; } = -4f;

    public override IEnumerable<string> Toggles => [InputActions.TurnOn];

    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content) =>
        [new("Carries", Shapes.Length > 0 ? $"{Slots.Length} {string.Join(" or ", Shapes)} bales" : $"{Slots.Length} bales")];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Slots.Length == 0) yield return "needs slots";
        if (Slots.Any(s => s.Length != 3)) yield return "slots are [x, y, z]";
        if (Pickup.Error() is { } error) yield return $"pickup: {error}";
        if (GrabSeconds <= 0f) yield return "grabSeconds must be > 0";
        foreach (var s in Shapes.Where(s => !BaleDef.Shapes.Contains(s))) yield return $"unknown shape '{s}' ({string.Join(", ", BaleDef.Shapes)})";
    }

    internal override Component Create(Machine machine) => new BaleLoader(machine, this);
}

public sealed class BaleLoaderSave
{
    public bool On { get; set; }
}

public sealed class BaleLoader : MachineComponent<BaleLoaderDef, BaleLoaderSave>, ISwitchable, IActionSource, IObjectHolder
{
    /// <summary>Faster than this, it won't set its bales down.</summary>
    private const float UnloadSpeed = 0.5f;

    private readonly WorldObject?[] _slots;
    private float _grab;

    public BaleLoader(Machine machine, BaleLoaderDef def) : base(machine, def) => _slots = new WorldObject?[def.Slots.Length];

    public bool CanTurnOn => true;
    public bool On { get; set; }

    /// <summary>What sits in each slot of its bed.</summary>
    public IReadOnlyList<WorldObject?> Slots => _slots;
    public int Count => _slots.Count(s => s != null);
    public int Capacity => _slots.Length;

    /// <summary>The turn-on key starts and stops picking bales up; the unload key sets them down.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        actions.AddSwitch(this);
        if (Count > 0) actions.Add(InputActions.Unload, $"Unload bales ({Count})", () => Unload(sim));
    }

    /// <summary>Whether it takes <paramref name="o"/>: a bale of a shape it takes, of its farm.</summary>
    public bool Takes(WorldObject o) =>
        o.Get<Bale>() is { } b && (Def.Shapes.Length == 0 || Def.Shapes.Contains(b.Def.Shape)) && o.FarmId == Machine.FarmId;

    internal override void Update(Simulation sim, float dt)
    {
        foreach (var o in _slots)
            if (o != null) ObjectSystem.Place(o);
        _grab = MathF.Max(0f, _grab - dt);
        if (!On || _grab > 0f || Array.IndexOf(_slots, null) is not (>= 0 and var free)) return;
        if (sim.Objects.LooseIn(Def.Pickup.On(Machine)).FirstOrDefault(Takes) is not { } bale) return;
        _slots[free] = bale;
        ObjectSystem.Hold(bale, this);
        _grab = Def.GrabSeconds;
    }

    /// <summary>Sets every bale down behind it, the front row at its unload z; it must stand (nearly) still.</summary>
    public void Unload(Simulation sim)
    {
        if (MathF.Abs(Machine.Root.Speed) > UnloadSpeed)
        {
            sim.Notifications.Post("Stop to unload the bales");
            return;
        }
        var front = Def.Slots.Max(s => s[2]);
        var bottom = Def.Slots.Min(s => s[1]);
        for (var k = 0; k < _slots.Length; k++)
        {
            if (_slots[k] is not { } o) continue;
            var s = Def.Slots[k];
            var (position, heading) = Machine.PartToWorld(s[0], Def.UnloadZ - (front - s[2]));
            ObjectSystem.Drop(o, s[1] - bottom);
            o.Position = position;
            o.Heading = heading;
        }
    }

    public (Vector3 at, float yaw) PoseOf(WorldObject o)
    {
        var s = Def.Slots[Math.Max(0, Array.IndexOf(_slots, o))];
        return (new Vector3(s[0], s[1], s[2]), 0f);
    }

    public int SlotOf(WorldObject o) => Array.IndexOf(_slots, o);

    public bool Restore(WorldObject o, int slot)
    {
        if (slot < 0 || slot >= _slots.Length || _slots[slot] != null) return false;
        _slots[slot] = o;
        ObjectSystem.Hold(o, this);
        return true;
    }

    public void Release(WorldObject o)
    {
        if (Array.IndexOf(_slots, o) is var k and >= 0) _slots[k] = null;
    }

    internal override void OnDetached() => On = false;

    protected override BaleLoaderSave Capture(ContentDatabase content) => new() { On = On };

    protected override void Restore(BaleLoaderSave save, SaveContext context) => On = save.On;
}
