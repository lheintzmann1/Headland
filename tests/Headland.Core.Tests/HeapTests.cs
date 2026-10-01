using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class HeapTests
{
    private const float Dt = 1f / 60f;
    /// <summary>The farm's own land, open ground off its fields.</summary>
    private static readonly Vector2 Yard = new(160f, 100f);
    /// <summary>A neighbor's land.</summary>
    private static readonly Vector2 Neighbors = new(269f, 300f);

    private static void Run(Simulation sim, float seconds, Func<bool>? until = null)
    {
        for (var t = 0f; t < seconds && until?.Invoke() != true; t += Dt) sim.Tick(Dt);
    }

    /// <summary>What lies in heaps of <paramref name="fillType"/> within <paramref name="radius"/> of <paramref name="p"/>, and how high it stands.</summary>
    private static (float amount, float top) Heap(Simulation sim, Vector2 p, string fillType, float radius = 12f)
    {
        var w = sim.World;
        var (amount, top) = (0f, 0f);
        var (c0x, c0z) = w.WorldToCell(p - new Vector2(radius));
        var (c1x, c1z) = w.WorldToCell(p + new Vector2(radius));
        for (var cz = c0z; cz <= c1z; cz++)
        for (var cx = c0x; cx <= c1x; cx++)
        {
            var i = w.CellIndex(cx, cz);
            if (sim.Heaps.FillTypeAt(i)?.Id != fillType) continue;
            amount += sim.Heaps.AmountAt(i);
            top = MathF.Max(top, w.Layers.Heap[i]);
        }
        return (amount, top);
    }

    /// <summary>The steepest step between neighboring cells of a heap (rise per meter).</summary>
    private static float Steepest(Simulation sim, Vector2 p, float radius = 12f)
    {
        var w = sim.World;
        var steepest = 0f;
        var (c0x, c0z) = w.WorldToCell(p - new Vector2(radius));
        var (c1x, c1z) = w.WorldToCell(p + new Vector2(radius));
        for (var cz = c0z; cz <= c1z; cz++)
        for (var cx = c0x; cx <= c1x; cx++)
        {
            var i = w.CellIndex(cx, cz);
            if (!sim.Heaps.Has(i)) continue;
            foreach (var n in new[] { w.CellIndex(cx + 1, cz), w.CellIndex(cx, cz + 1) })
            {
                if (sim.Heaps.FillTypeAt(n) is { } f && f.Id != sim.Heaps.FillTypeAt(i)!.Id) continue;
                var rise = MathF.Abs(w.CellHeight(i) + w.Layers.Heap[i] - w.CellHeight(n) - w.Layers.Heap[n]) / WorldMap.CellSize;
                steepest = MathF.Max(steepest, rise);
            }
        }
        return steepest;
    }

    /// <summary>A tractor heading east (+x) with a trailer holding <paramref name="amount"/> of wheat, its middle at <paramref name="at"/>.</summary>
    private static (Machine tractor, Machine trailer) Trailer(Simulation sim, Vector2 at, float amount)
    {
        var heading = MathF.PI / 2f;
        var t = sim.Machines.Spawn("tractor_95", at + MathUtil.Forward(heading) * 6f, heading);
        var trailer = sim.Machines.Spawn("trailer_16", at, heading);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", amount);
        return (t, trailer);
    }

    /// <summary>A tractor heading east with its loader and a bucket on it, the arm down and the bucket level.</summary>
    private static (Machine tractor, Machine arm, Machine bucket) Loader(Simulation sim, Vector2 at)
    {
        var heading = MathF.PI / 2f;
        var t = sim.Machines.Spawn("tractor_125", at, heading, configuration: new Dictionary<string, string> { ["frontLoader"] = "bracket" });
        var arm = sim.Machines.Spawn("frontloader_arm", at, heading);
        Assert.True(sim.Machines.Attach(t, "frontLoader", arm));
        var bucket = sim.Machines.Spawn("bucket", arm.Position, heading);
        Assert.True(sim.Machines.Attach(arm, "tool", bucket));
        return (t, arm, bucket);
    }

    /// <summary>Where the bucket's edge is on the map.</summary>
    private static Vector2 Edge(Machine bucket)
    {
        var edge = bucket.Get<Shovel>()!.Def.Edge;
        var p = bucket.OnCrane(new Vector3(0f, edge.Y, edge.Z))!.Value;
        return bucket.Parent!.PartToWorld(p.X, p.Z).position;
    }

    private static void Drive(Machine tractor, float throttle) =>
        tractor.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = throttle, Brake = throttle == 0f } };

    [Fact]
    public void ATippedLoadSlidesIntoAHeapAtItsAngleAndNothingIsLost()
    {
        var sim = TestContent.NewSim();
        var (_, trailer) = Trailer(sim, Yard, 0f);
        for (var k = 0; k < 20; k++)
        {
            Assert.Equal(400f, sim.Heaps.Drop(trailer, Yard, Yard + new Vector2(0.5f, 0f), "wheat", 400f), 0.01f);
            sim.Tick(Dt);
        }
        sim.Heaps.SettleAll();

        var (amount, top) = Heap(sim, Yard, "wheat");
        Assert.Equal(8000f, amount, 1f);
        // 8 m³ standing at 27°: a cone about 1.3 m high, its sides no steeper than that.
        Assert.InRange(top, 0.9f, 1.6f);
        Assert.True(Steepest(sim, Yard) <= MathF.Tan(27f * MathUtil.Deg2Rad) + 0.02f, $"steepest {Steepest(sim, Yard)}");
        var i = sim.World.CellIndex(sim.World.WorldToCell(Yard).cx, sim.World.WorldToCell(Yard).cz);
        Assert.Equal("Wheat", sim.InspectCell(Yard).HeapFill?.Name);
        Assert.Equal(sim.World.Layers.Heap[i], sim.InspectCell(Yard).HeapHeight);
        // Machines roll on it.
        Assert.Equal(sim.World.HeightAt(Yard) + top, sim.World.SurfaceAt(sim.World.CellCenter(sim.World.WorldToCell(Yard).cx, sim.World.WorldToCell(Yard).cz)), 0.3f);
    }

    [Fact]
    public void AHeapOfAnotherFillTypeStopsOneAsAWall()
    {
        var sim = TestContent.NewSim();
        var (_, trailer) = Trailer(sim, Yard, 0f);
        Assert.Equal(4000f, sim.Heaps.Drop(trailer, Yard, Yard, "wheat", 4000f), 0.01f);
        sim.Heaps.SettleAll();
        // Nothing of the barley goes on the wheat, and what lies beside it stays apart.
        Assert.Equal(0f, sim.Heaps.Drop(trailer, Yard, Yard, "barley", 1000f));
        var beside = Yard + new Vector2(3f, 0f);
        Assert.Equal(4000f, sim.Heaps.Drop(trailer, beside, beside, "barley", 4000f), 0.01f);
        sim.Heaps.SettleAll();
        Assert.Equal(4000f, Heap(sim, Yard, "wheat").amount, 1f);
        Assert.Equal(4000f, Heap(sim, Yard, "barley").amount, 1f);
    }

    [Fact]
    public void ATrailerTipsOnTheGroundOnItsFarmsLandOnly()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = Trailer(sim, Neighbors, 2000f);
        sim.Player.Enter(t);
        var bed = trailer.Get<Tipper>()!;

        // Not on a neighbor's land.
        Assert.False(sim.Offers().Of(InputActions.TipGround)!.Hinted);
        sim.Perform(InputActions.TipGround);
        Assert.False(bed.Tipping);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "You can only tip on your own land");

        // On its own: the hint shows, and the load goes on the ground, behind it.
        sim.Machines.Teleport(t, Yard + MathUtil.Forward(t.Heading) * 6f, t.Heading);
        Assert.Equal(("Tip on the ground", true), (sim.Offers().Of(InputActions.TipGround)!.Label, sim.Offers().Of(InputActions.TipGround)!.Hinted));
        var tipped = new List<FillTippedOnGround>();
        sim.Events.Subscribe<FillTippedOnGround>(tipped.Add);
        sim.Perform(InputActions.TipGround);
        Assert.True(bed.Tipping);
        Assert.Equal("Stop tipping", sim.Offers().Of(InputActions.Unload)!.Label);
        Run(sim, 20f, () => bed.Load.IsEmpty);
        Run(sim, 0.5f);
        Assert.True(bed.Load.IsEmpty);
        Assert.False(bed.Tipping);
        Assert.Equal(2000f, Heap(sim, bed.GroundOutlet(sim).a, "wheat").amount, 1f);
        Assert.Equal(("wheat", 2000f), (tipped.Single().FillType, MathF.Round(tipped.Single().Amount)));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Tipped 2,000 L Wheat on the ground");

        // What doesn't lie in heaps can't be tipped there.
        var (a, b, _) = bed.GroundOutlet(sim);
        Assert.Equal("Seeds can't be tipped on the ground", sim.Heaps.DropBlocker(t.FarmId, a, b, "seeds"));
    }

    [Fact]
    public void TippedStandingStillTheHeapComesUpToTheTailgateAndDrivingOnTipsTheRest()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = Trailer(sim, Yard, 16000f);
        var bed = trailer.Get<Tipper>()!;
        Assert.Null(bed.StartOnGround(sim));
        Run(sim, 60f, () => bed.HeapUp);
        Assert.True(bed.HeapUp);
        Assert.True(bed.Tipping);
        Assert.InRange(bed.Load.Level, 1f, 15000f);
        Assert.Contains(trailer.Readouts(sim), r => r is Status { Text: "The heap is up to the tailgate: drive on" });

        for (var k = 0; k < 8 && !bed.Load.IsEmpty; k++)
        {
            sim.Machines.Teleport(t, t.Position + MathUtil.Forward(t.Heading) * 7f, t.Heading);
            sim.Tick(Dt);
            Run(sim, 60f, () => bed.Load.IsEmpty || bed.HeapUp);
        }
        Assert.True(bed.Load.IsEmpty);
        sim.Heaps.SettleAll();
        Assert.Equal(16000f, Heap(sim, Yard + new Vector2(25f, 0f), "wheat", 40f).amount, 2f);
    }

    [Fact]
    public void ABucketTakesUpAHeapDrivenIntoItLowAndLevel()
    {
        var sim = TestContent.NewSim();
        var (t, arm, bucket) = Loader(sim, Yard);
        var shovel = bucket.Get<Shovel>()!;
        var heapAt = Edge(bucket) + MathUtil.Forward(t.Heading) * 4f;
        Assert.Equal(3000f, sim.Heaps.Drop(bucket, heapAt, heapAt, "wheat", 3000f), 0.01f);
        sim.Heaps.SettleAll();
        var load = bucket.Unit("bucket")!;

        // Standing, it takes nothing; curled back, driven into it, neither.
        Run(sim, 1f);
        Assert.True(load.IsEmpty);
        arm.Get<CraneArm>()!.MoveTo("tilt", 40f);
        Run(sim, 2f);
        Drive(t, 0.15f);
        Run(sim, 4f);
        Assert.True(load.IsEmpty);

        // Level, its edge on the ground, it fills up.
        Drive(t, 0f);
        sim.Machines.Teleport(t, Yard, t.Heading);
        arm.Get<CraneArm>()!.MoveTo("tilt", 0f);
        Run(sim, 2f);
        Drive(t, 0.15f);
        var scooped = new List<HeapScooped>();
        sim.Events.Subscribe<HeapScooped>(scooped.Add);
        Run(sim, 15f, () => load.Free < 1f);
        Assert.Equal(load.Capacity, load.Level, 1f);
        Assert.Equal("wheat", load.FillType);
        Drive(t, 0f);
        Run(sim, 1f);
        sim.Heaps.SettleAll();
        Assert.Equal(3000f - load.Level, Heap(sim, heapAt, "wheat").amount, 1f);
        Assert.Equal(load.Level, scooped.Sum(e => e.Amount), 1f);
        Assert.InRange(MathF.Abs(shovel.PitchDeg), 0f, 1f);
    }

    [Fact]
    public void ABucketTippedForwardPoursIntoATrailerAPitOrOnTheGround()
    {
        var sim = TestContent.NewSim();
        var (t, arm, bucket) = Loader(sim, Yard);
        var shovel = bucket.Get<Shovel>()!;
        var load = bucket.Unit("bucket")!;
        var crane = arm.Get<CraneArm>()!;

        // Held up and level, nothing pours; tilted forward, it does, faster the farther.
        crane.MoveTo("lift", 50f);
        crane.MoveTo("tilt", -50f);
        Run(sim, 4f);
        Assert.Equal(0f, shovel.PourFactor);
        crane.MoveTo("tilt", -100f);
        Run(sim, 2f);
        Assert.Equal(-50f, shovel.PitchDeg, 0.5f);
        Assert.Equal(1f, shovel.PourFactor, 0.02f);

        // Over a trailer: into it.
        var trailer = sim.Machines.Spawn("trailer_16", Edge(bucket), t.Heading);
        load.Add("wheat", 1500f);
        Run(sim, 5f, () => load.IsEmpty);
        Assert.Equal(1500f, trailer.Unit("main")!.Level, 0.5f);
        sim.Machines.All.Remove(trailer);

        // Over the farm silo's pit: into the silo.
        var silo = sim.World.PoiById("silo")!;
        var pit = silo.Trigger("unload")!.Area;
        var stored = silo.Get<Pois.Components.Silo>()!.Storage.Level("wheat");
        sim.Machines.Teleport(t, pit.Center - (Edge(bucket) - t.Position), t.Heading);
        load.Add("wheat", 1500f);
        Run(sim, 5f, () => load.IsEmpty);
        Assert.True(load.IsEmpty);
        Assert.Equal(stored + 1500f, silo.Get<Pois.Components.Silo>()!.Storage.Level("wheat"), 0.5f);

        // On its farm's land: on the ground, in a heap.
        sim.Machines.Teleport(t, Yard, t.Heading);
        load.Add("wheat", 1500f);
        Run(sim, 5f, () => load.IsEmpty);
        sim.Heaps.SettleAll();
        Assert.Equal(1500f, Heap(sim, Edge(bucket), "wheat").amount, 1f);

        // On a neighbor's, it holds it and says why.
        sim.Machines.Teleport(t, Neighbors, t.Heading);
        load.Add("wheat", 1500f);
        Run(sim, 2f);
        Assert.Equal(1500f, load.Level);
        Assert.Contains(bucket.Readouts(sim), r => r is Status { Text: "You can only tip on your own land" });
    }

    [Fact]
    public void HeapsAreSaved()
    {
        var sim = TestContent.NewSim();
        var (_, trailer) = Trailer(sim, Yard, 0f);
        sim.Heaps.Drop(trailer, Yard, Yard, "corn", 5000f);
        sim.Heaps.SettleAll();
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(5000f, Heap(loaded, Yard, "corn").amount, 1f);
        Assert.Equal(Heap(sim, Yard, "corn").top, Heap(loaded, Yard, "corn").top);
    }

    [Fact]
    public void ShovelsAndHeapsAreChecked()
    {
        var errors = Assert.Throws<ContentException>(() => TestContent.Modded(new()
        {
            ["machines/test.json"] = """
                [{ "id": "x", "components": {
                     "fillUnits": { "units": [ { "id": "main" } ] },
                     "shovel": { "edge": { "width": 0 }, "dumpAngleDeg": [50, 20], "maxPickupAngleDeg": 95 } } }]
                """,
            ["filltypes.json"] = TestContent.WithEntry("filltypes.json", """{ "id": "sand", "name": "Sand", "heap": { "angleDeg": 75 } }"""),
        })).Message;
        Assert.Contains("machine 'x' shovel: fill unit 'bucket' missing", errors);
        Assert.Contains("machine 'x' shovel: edge.width and depth must be > 0", errors);
        Assert.Contains("machine 'x' shovel: maxPickupAngleDeg must be in [0, 90)", errors);
        Assert.Contains("machine 'x' shovel: dumpAngleDeg is [from, to], 0 < from < to <= 90", errors);
        Assert.Contains("fill type 'sand': heap needs angleDeg in (0, 60) and perCubicMeter > 0", errors);
    }
}
