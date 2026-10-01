using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines.Work;
using Headland.Core.World;

namespace Headland.Core.Machines.Components;

/// <summary>A ridge marker's arm, and where its disc runs.</summary>
public sealed class RidgeMarkerArmDef
{
    /// <summary>What the key hint calls it: "Ridge marker left".</summary>
    public string Name { get; set; } = "";
    /// <summary>The parts of the machine's <c>animatedParts</c> it moves, down in their moved pose.</summary>
    public string[] Parts { get; set; } = [];
    /// <summary>Where its disc draws its line, in the machine's space: the middle of the next pass, for the driver to follow.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Its pose (0 up … 1 down) while it's down but the machine raised, at the headland (FS: liftedAnimTime).</summary>
    public float Lifted { get; set; } = 1f;
}

/// <summary>
/// Ridge markers (FS: RidgeMarker): arms at the sides of a seeder or a cultivator whose disc draws a line where the
/// next pass goes. The key steps through them, one down at a time, then all up: "Ridge marker left", "Ridge marker
/// right", "Ridge markers up". The one down draws its line only with the machine lowered, on the fields its farm may
/// work: a furrow on tilled ground, a tilled line on plowed ground. Folding puts them up, and they can't come down
/// while folded; a helper leaves them up.
/// </summary>
public sealed class RidgeMarkerDef : MachineComponentDef
{
    public RidgeMarkerArmDef[] Markers { get; set; } = [];
    /// <summary>The key they step on (FS: inputButton): the parts key, or the turn-on key on a machine that doesn't turn on.</summary>
    public string Key { get; set; } = InputActions.MoveParts;

    public override IEnumerable<string> HeldParts => Markers.SelectMany(m => m.Parts);

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Markers.Length == 0) yield return "needs markers";
        if (Key is not (InputActions.MoveParts or InputActions.TurnOn)) yield return $"key must be {InputActions.MoveParts} or {InputActions.TurnOn}";
        else if (Key == InputActions.TurnOn && machine.Components.Any(c => c != this && c.Toggles.Contains(InputActions.TurnOn)))
            yield return "the turn-on key turns the machine on; put the markers on another key";
        if (machine.Get<AttachableDef>() is not { Lowerable: true }) yield return "needs an attachable that lowers: markers draw only lowered";
        if (machine.Get<WorkAreasDef>() == null) yield return "needs workAreas: markers draw where their machine may work";
        var parts = machine.Get<AnimatedPartsDef>();
        foreach (var m in Markers)
        {
            var what = $"marker '{m.Name}'";
            if (string.IsNullOrWhiteSpace(m.Name)) yield return "a marker has no name";
            if (m.Parts.Length == 0) yield return $"{what}: needs parts";
            if (m.Lifted is < 0f or > 1f) yield return $"{what}: lifted must be in [0, 1]";
            foreach (var id in m.Parts)
            {
                if (parts?.Parts.FirstOrDefault(p => p.Id == id) is not { } part) yield return $"{what}: part '{id}' missing in animatedParts";
                else if (part.HeldBy != Kind) yield return $"{what}: part '{id}' is moved by the {part.HeldBy}";
            }
        }
    }

    internal override Component Create(Machine machine) => new RidgeMarker(machine, this);
}

public sealed class RidgeMarkerSave
{
    /// <summary>0: all up; else the marker down, from 1.</summary>
    public int State { get; set; }
}

public sealed class RidgeMarker(Machine machine, RidgeMarkerDef def) : MachineComponent<RidgeMarkerDef, RidgeMarkerSave>(machine, def), IActionSource, IReadoutSource
{
    /// <summary>Its disc down to draw from (FS: minWorkLimit).</summary>
    private const float DownFrom = 0.99f;
    /// <summary>Where the marker down drew last tick, if it did, and the cell its disc was in: each is drawn once, as the disc comes to it.</summary>
    private Vector2? _last;
    private int _cell = -1;

    /// <summary>0: all up; else the marker down, from 1.</summary>
    public int State { get; set; }

    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (Down is { } marker) yield return new Status($"Marker {marker.Name}", Tone.Info);
    }

    /// <summary>The marker down, if one is.</summary>
    public RidgeMarkerArmDef? Down => State > 0 ? Def.Markers[State - 1] : null;

    /// <summary>Folded or folding, they stay up.</summary>
    private bool Folded => Machine.Get<AnimatedParts>() is { Unfolded: false };

    /// <summary>The key steps to the next marker, then all up; not while it's folded.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Folded) return;
        var next = (State + 1) % (Def.Markers.Length + 1);
        actions.Add(Def.Key, next == 0 ? "Ridge markers up" : $"Ridge marker {Def.Markers[next - 1].Name}", () => State = next);
    }

    internal override void Update(Simulation sim, float dt)
    {
        if (Folded) State = 0;
        var lowered = Machine.Get<Attachable>() is { Lowered: true, LowerAnim: >= Attachable.WorkingDepth } && Machine.Get<AnimatedParts>() is not { InWorkingPose: false };
        var parts = Machine.Get<AnimatedParts>();
        for (var i = 0; i < Def.Markers.Length; i++)
        {
            var m = Def.Markers[i];
            foreach (var id in m.Parts) parts?.Hold(id, State == i + 1 ? lowered ? 1f : m.Lifted : 0f);
        }

        // The marker down draws its line, lowered, once its disc is on the ground.
        var down = Down;
        if (down == null || !lowered || down.Parts.Any(id => parts?.Part(id) is { Position: < DownFrom }))
        {
            Lift();
            return;
        }
        var disc = Machine.LocalToWorld(down.X, down.Z);
        if (_last is { } from && Vector2.Distance(from, disc) < 5f) Draw(sim, from, disc);
        _last = disc;
    }

    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/>, on the field cells its farm may work.</summary>
    private void Draw(Simulation sim, Vector2 from, Vector2 to)
    {
        var world = sim.World;
        var areas = Machine.Get<WorkAreas>()!;
        var area = areas.Def.Areas[0];
        var crop = area.Work.Crop(areas, sim.Content);
        var angle = WorldGen.AngleToByte(Machine.Heading);
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(from, to) / (WorldMap.CellSize * 0.5f)));
        for (var s = 1; s <= steps; s++)
        {
            var (cx, cz) = world.WorldToCell(Vector2.Lerp(from, to, (float)s / steps));
            if (!world.InBounds(cx, cz)) continue;
            var i = world.CellIndex(cx, cz);
            if (i == _cell) continue;
            _cell = i;
            if (world.Layers.FieldId[i] == 0 || !sim.Farms.MayWork(Machine.Root.FarmId, i, area.Type, crop)) continue;
            // FS: plowed ground is tilled, the rest furrowed.
            if ((GroundType)world.Layers.Ground[i] == GroundType.Plowed) CultivatorWork.Till(world, i, angle);
            else PlowWork.Plow(world, i, angle);
        }
    }

    /// <summary>Its disc left the ground: the next line starts anew.</summary>
    private void Lift()
    {
        _last = null;
        _cell = -1;
    }

    internal override void OnDetached() => Lift();

    protected override RidgeMarkerSave Capture(ContentDatabase content) => new() { State = State };

    protected override void Restore(RidgeMarkerSave save, SaveContext context) => State = Math.Clamp(save.State, 0, Def.Markers.Length);
}
