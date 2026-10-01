using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.World;

namespace Headland.Core.Machines.Components;

/// <summary>A bucket's cutting edge, in its space: the middle of its lip and how wide it is.</summary>
public sealed class ShovelEdgeDef
{
    public float Z { get; set; } = 1f;
    public float Y { get; set; }
    public float Width { get; set; } = 2f;
}

/// <summary>
/// A bucket (FS: shovel), on a loader arm: driven into a heap with its edge low and the bucket level, it takes up what
/// lies above its edge; tilted forward, it pours out over its edge, the farther the faster (FS: the shovel's tip angle):
/// into a machine under it (a trailer), an unloading area, or on the ground on its farm's land.
/// </summary>
public sealed class ShovelDef : MachineComponentDef, ISpecSource
{
    public string FillUnit { get; set; } = "bucket";
    public ShovelEdgeDef Edge { get; set; } = new();
    /// <summary>How far behind its edge it takes up what lies above it.</summary>
    public float Depth { get; set; } = 0.5f;
    /// <summary>How fast it fills, driven into a heap.</summary>
    public float FillPerSecond { get; set; } = 1500f;
    /// <summary>How far from level it may be tilted to take up (degrees).</summary>
    public float MaxPickupAngleDeg { get; set; } = 25f;
    /// <summary>Tilted forward past the first angle it starts to pour, and at the second it pours at its full rate (degrees).</summary>
    public float[] DumpAngleDeg { get; set; } = [20f, 50f];
    /// <summary>How fast it pours, tilted all the way.</summary>
    public float RatePerSecond { get; set; } = 800f;

    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content) =>
        [new("Width", $"{Edge.Width:0.0#} m")];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
        if (Edge.Width <= 0f || Depth <= 0f) yield return "edge.width and depth must be > 0";
        if (FillPerSecond <= 0f || RatePerSecond <= 0f) yield return "fillPerSecond and ratePerSecond must be > 0";
        if (MaxPickupAngleDeg is < 0f or >= 90f) yield return "maxPickupAngleDeg must be in [0, 90)";
        if (DumpAngleDeg is not [var from, var to] || from <= 0f || to <= from || to > 90f) yield return "dumpAngleDeg is [from, to], 0 < from < to <= 90";
    }

    internal override Component Create(Machine machine) => new Shovel(machine, this);
}

public sealed class Shovel(Machine machine, ShovelDef def) : MachineComponent<ShovelDef>(machine, def), IReadoutSource
{
    /// <summary>It moves into what it takes up at least this fast (m/s).</summary>
    private const float MinSpeed = 0.15f;
    private readonly List<int> _cells = [];

    public FillUnit Bucket => Machine.Unit(Def.FillUnit)!;

    /// <summary>It took some up on the last tick.</summary>
    public bool Loading { get; private set; }
    /// <summary>It poured some out on the last tick.</summary>
    public bool Dumping { get; private set; }
    /// <summary>Why it can't pour out where it is (tilted to, with something in it), on the last tick.</summary>
    public string? Blocked { get; private set; }

    /// <summary>How far it's tilted from level, in degrees: back (up) positive, forward negative.</summary>
    public float PitchDeg
    {
        get
        {
            var edge = new Vector3(0f, Def.Edge.Y, Def.Edge.Z);
            if (Machine.OnCrane(edge) is not { } tip || Machine.OnCrane(edge - Vector3.UnitZ) is not { } back) return 0f;
            var d = tip - back;
            return MathF.Atan2(d.Y, MathF.Sqrt(d.X * d.X + d.Z * d.Z)) / MathUtil.Deg2Rad;
        }
    }

    /// <summary>How fast it pours, from 0 (level enough to hold its load) to 1 (tilted all the way).</summary>
    public float PourFactor => Math.Clamp((-PitchDeg - Def.DumpAngleDeg[0]) / (Def.DumpAngleDeg[1] - Def.DumpAngleDeg[0]), 0f, 1f);

    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        var ft = Bucket.FillType is { } id ? sim.Content.FillTypes[id].Name.ToLowerInvariant() : null;
        if (Loading) yield return new Status($"Loading {ft}", Tone.Busy);
        else if (Dumping) yield return new Status($"Dumping {ft}", Tone.Busy);
        else if (Blocked != null) yield return new Status(Blocked, Tone.Warning);
    }

    /// <summary>
    /// A point of the bucket (in its space) on the map, and how high it is, from where its vehicle stands on the ground
    /// (or a heap) and leans with it.
    /// </summary>
    private (Vector2 at, float y) Point(Simulation sim, float x, float y, float z)
    {
        var local = new Vector3(x, y, z);
        var at = Machine.OnCrane(local) is { } p ? Machine.Parent!.PartToWorld(p.X, p.Z).position : Machine.LocalToWorld(x, z);
        var root = Machine.Root;
        var (ground, slope, _) = root.GroundPose(sim.World);
        var ahead = MathUtil.WorldToLocal(root.Position, root.Heading, at).Y;
        return (at, ground + slope * ahead + Machine.HeightOf(local));
    }

    internal override void Update(Simulation sim, float dt)
    {
        Loading = Dumping = false;
        Blocked = null;
        var bucket = Bucket;
        var half = Def.Edge.Width * 0.5f;
        if (!bucket.IsEmpty && PourFactor > 0f)
        {
            var (a, ay) = Point(sim, half, Def.Edge.Y, Def.Edge.Z);
            var (b, by) = Point(sim, -half, Def.Edge.Y, Def.Edge.Z);
            Pour(sim, bucket, a, b, MathF.Min(ay, by), MathF.Min(bucket.Level, Def.RatePerSecond * PourFactor * dt));
            return;
        }
        // Takes up only driven forward into what it takes, held about level.
        if (bucket.Free <= 0f || MathF.Abs(PitchDeg) > Def.MaxPickupAngleDeg || Machine.Root.Speed < MinSpeed) return;
        var (l, ly) = Point(sim, half, Def.Edge.Y, Def.Edge.Z);
        var (r, ry) = Point(sim, -half, Def.Edge.Y, Def.Edge.Z);
        var (lb, _) = Point(sim, half, Def.Edge.Y, Def.Edge.Z - Def.Depth);
        var (rb, _) = Point(sim, -half, Def.Edge.Y, Def.Edge.Z - Def.Depth);
        _cells.Clear();
        Geometry.RasterizeConvex([l, r, rb, lb], WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ, _cells);
        var (ft, taken) = sim.Heaps.TakeAbove(Machine, _cells, bucket.FillType, (ly + ry) * 0.5f,
            MathF.Min(bucket.Free, Def.FillPerSecond * dt), bucket.Accepts);
        if (ft != null && taken > 0f) Loading = bucket.Add(ft, taken) > 0f;
    }

    /// <summary>
    /// Pours out <paramref name="amount"/> over its edge, from <paramref name="a"/> to <paramref name="b"/>, as high as
    /// <paramref name="top"/>: into a machine under it, else an unloading area, else on the ground.
    /// </summary>
    private void Pour(Simulation sim, FillUnit bucket, Vector2 a, Vector2 b, float top, float amount)
    {
        var ft = bucket.FillType!;
        var middle = (a + b) * 0.5f;
        var moved = 0f;
        if (sim.Machines.FindReceiver(middle, ft, Machine) is { } target) moved = bucket.Remove(target.Add(ft, amount));
        else if (sim.Machines.CoveredAt(middle, ft, Machine) is { } covered) Blocked = new CoverClosed(covered.Def.Name).Text;
        else if (sim.Pois.TriggerAt(middle, "unload") is { } pit)
        {
            if (sim.Pois.UnloadBlocker(Machine, pit, ft, bucket.Level) is { } why) Blocked = why;
            else moved = bucket.Remove(sim.Pois.Unload(Machine, pit, ft, amount));
        }
        else if (sim.Heaps.DropBlocker(Machine.FarmId, a, b, ft) is { } why) Blocked = why;
        else moved = bucket.Remove(sim.Heaps.Drop(Machine, a, b, ft, amount, top));
        Dumping = moved > 0f;
    }
}
