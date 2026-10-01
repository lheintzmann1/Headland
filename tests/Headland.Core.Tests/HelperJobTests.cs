using System.Numerics;
using Headland.Core.Events;
using Headland.Core.Helpers;
using Headland.Core.Helpers.Tasks;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Objects;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class HelperJobTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds, Func<bool>? until = null)
    {
        for (var t = 0f; t < seconds && until?.Invoke() != true; t += Dt) sim.Tick(Dt);
    }

    private static List<T> Record<T>(Simulation sim) where T : IGameEvent
    {
        var list = new List<T>();
        sim.Events.Subscribe<T>(list.Add);
        return list;
    }

    /// <summary>A combine with its grain header at the north edge of field 2 (ripe wheat, the farm's), heading into it.</summary>
    private static (Machine combine, FieldInfo strip) Combine(Simulation sim)
    {
        var f2 = sim.World.FieldById(2)!.Shape;
        var combine = sim.Machines.Spawn("combine_7", f2.Min + new Vector2(3f, -8f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", sim.Machines.Spawn("header_grain_6", combine.Position, 0f)));
        return (combine, FieldInfo.Rect(2, f2.Min.X, f2.Min.Y, 18f, f2.Size.Y));
    }

    /// <summary>A trailer standing under the combine's pipe, as if driven alongside.</summary>
    private static void Alongside(Simulation sim, Machine trailer, Machine combine) =>
        sim.Machines.Teleport(trailer, combine.Get<Pipe>()!.Outlet, combine.Heading);

    [Fact]
    public void AHarvestHelperUnloadsIntoATrailerDrivenAlongsideAsItGoes()
    {
        var sim = TestContent.NewSim();
        var (combine, strip) = Combine(sim);
        var (pipe, tank) = (combine.Get<Pipe>()!, combine.Get<Thresher>()!.Tank);
        tank.Add("wheat", 3000f);
        var helper = sim.HireHelper(combine, strip);
        Assert.Equal("harvest", helper.Type.Id);
        var work = helper.FieldWork!;
        Run(sim, 60f, () => work.Path.Segments[work.Driver.Index] == PathSegment.Work && combine.Speed > 0.5f && strip.Contains(combine.Position));
        Assert.False(pipe.Out);

        // A trailer comes alongside: the pipe swings out by itself, and the grain goes into it while the combine works on.
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(150f, 60f), 0f);
        var moving = false;
        for (var t = 0f; t < 10f; t += Dt)
        {
            Alongside(sim, trailer, combine);
            sim.Tick(Dt);
            moving |= combine.Speed > 0.5f && pipe.Flowing;
        }
        Assert.True(pipe.Out);
        Assert.True(moving, "unloads as it harvests");
        Assert.True(trailer.Unit("main")!.Level > 500f, $"trailer {trailer.Unit("main")!.Level}");
        Assert.IsType<FieldWorkTask>(helper.Current);

        // The trailer gone, the pipe comes back in.
        sim.Machines.Teleport(trailer, new Vector2(150f, 60f), 0f);
        Run(sim, 5f);
        Assert.False(pipe.Out);
    }

    [Fact]
    public void FullItWaitsWithItsPipeOutForATrailerAndGoesOnOnceUnloaded()
    {
        var sim = TestContent.NewSim();
        var (combine, strip) = Combine(sim);
        var (pipe, tank) = (combine.Get<Pipe>()!, combine.Get<Thresher>()!.Tank);
        tank.Add("wheat", tank.Capacity - 300f);
        var waiting = Record<HelperWaiting>(sim);
        var helper = sim.HireHelper(combine, strip, maxLanes: 2);
        Run(sim, 60f, () => helper.Current is UnloadTask);

        Assert.IsType<UnloadTask>(helper.Current);
        Assert.False(helper.Finished);
        Run(sim, 4f);
        Assert.Equal(0f, combine.Speed);
        Assert.True(pipe.Out);
        Assert.Equal("waiting for a trailer", helper.Describe());
        Assert.IsType<TankFull>(Assert.Single(waiting).Reason);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Helper 1 waits on Field 2 (Grain tank full: unload into a trailer)");

        // A trailer comes: the tank empties into it, and the helper goes on harvesting.
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(150f, 60f), 0f);
        for (var t = 0f; t < 90f && helper.Current is UnloadTask; t += Dt)
        {
            Alongside(sim, trailer, combine);
            sim.Tick(Dt);
        }
        Assert.IsType<FieldWorkTask>(helper.Current);
        Assert.True(trailer.Unit("main")!.Level > tank.Capacity - 1000f);
        Run(sim, 10f);
        Assert.True(combine.Speed > 0.5f);
        Assert.False(helper.Stopped, helper.StopReason?.Text);
    }

    [Fact]
    public void ABalerHelperDropsWhatsLeftInTheChamberAsABaleOnceTheFieldIsBaled()
    {
        var sim = TestContent.NewSim();
        // 6,000 L of grass lying on a strip of the farm's meadow: one full bale and some.
        var strip = FieldInfo.Rect(7, 150f, 280f, 6f, 40f);
        var fill = Windrows.FillOf(sim.Content, "grass");
        var cells = Enumerable.Range(0, sim.World.Layers.FieldId.Length)
            .Where(i => strip.Contains(sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX))).ToList();
        foreach (var i in cells) Windrows.Add(sim.World, i, fill, 6000f / cells.Count);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(151f, 270f), 0f);
        var baler = sim.Machines.Spawn("baler_125", new Vector2(151f, 266f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", baler));

        var helper = sim.HireHelper(t, strip);
        Assert.Equal("bale", helper.Type.Id);
        Run(sim, 400f, () => helper.Finished);
        Assert.True(helper.Finished && !helper.Stopped, helper.StopReason?.Text);
        Assert.True(baler.Get<Baler>()!.Chamber.IsEmpty);
        var bales = sim.Objects.All.Where(o => o.Content?.FillType == "grass").ToList();
        Assert.Equal(2, bales.Count);
        Assert.Equal(6000f, bales.Sum(b => b.Content!.Level), 60f);
    }

    [Fact]
    public void ACollectorHelperTakesTheBalesBesideTheFieldInLoads()
    {
        var sim = TestContent.NewSim();
        var meadow = sim.World.FieldById(7)!;
        var rng = new Random(4);
        var bales = Enumerable.Range(0, 11).Select(k =>
        {
            var at = new Vector2(152f + rng.NextSingle() * 30f, 280f + k * 7f + rng.NextSingle() * 3f);
            var bale = sim.Objects.Spawn("round_bale", at, rng.NextSingle() * MathF.PI, Farm.PlayerId);
            bale.Content!.Add("grass", 4000f);
            return bale;
        }).ToList();
        // Hired beside the field, where the bales go.
        var t = sim.Machines.Spawn("tractor_95", new Vector2(170f, 258f), MathF.PI * 0.5f);
        var loader = sim.Machines.Spawn("baleloader_8", new Vector2(164f, 258f), MathF.PI * 0.5f);
        Assert.True(sim.Machines.Attach(t, "drawbar", loader));

        var helper = sim.HireHelper(t, meadow);
        Assert.Equal("collectBales", helper.Type.Id);
        Assert.StartsWith("collecting bales, 11 left", helper.Describe());
        Run(sim, 900f, () => helper.Finished);
        Assert.True(helper.Finished && !helper.Stopped, helper.StopReason?.Text);

        // All of them beside the field, in two loads, none left on it.
        Assert.All(bales, b => Assert.Null(b.Holder));
        Assert.All(bales, b => Assert.True(meadow.Shape.Distance(b.Position) > 1f, $"bale at {b.Position}"));
        Assert.Equal(2, helper.Tasks.OfType<SetDownBalesTask>().Single().Loads);
        Assert.False(loader.Get<BaleLoader>()!.On);
    }

    [Fact]
    public void AHelperIsRefusedWhenItsJobCantBeDone()
    {
        var sim = TestContent.NewSim();
        var meadow = sim.World.FieldById(7)!;
        string? Refusal(Machine v)
        {
            sim.Player.Enter(v);
            sim.Perform(InputActions.Helper);
            var said = sim.Notifications.Items.LastOrDefault()?.Text;
            sim.Player.Exit(sim);
            return said;
        }

        // A collector with no bale on the field.
        var t = sim.Machines.Spawn("tractor_95", new Vector2(170f, 258f), MathF.PI * 0.5f);
        Assert.True(sim.Machines.Attach(t, "drawbar", sim.Machines.Spawn("baleloader_8", new Vector2(164f, 258f), MathF.PI * 0.5f)));
        Assert.Equal("No bales of yours lie on Field 7", Refusal(t));
        Assert.Empty(sim.Helpers);

        // A combine without its header.
        var f2 = sim.World.FieldById(2)!.Shape;
        var combine = sim.Machines.Spawn("combine_7", f2.Min + new Vector2(3f, -8f), 0f);
        Assert.Equal("Attach a header first", Refusal(combine));

        // A tractor with nothing to do a job with.
        var bare = sim.Machines.Spawn("tractor_125", new Vector2(160f, 250f), 0f);
        Assert.Equal("Attach an implement first", Refusal(bare));
        Assert.Empty(sim.Helpers);
    }

    [Fact]
    public void HelpersAreSavedAtTheStepOfTheirJobTheyAreAt()
    {
        var sim = TestContent.NewSim();
        var (combine, strip) = Combine(sim);
        var tank = combine.Get<Thresher>()!.Tank;
        tank.Add("wheat", tank.Capacity - 300f);
        var harvest = sim.HireHelper(combine, strip, maxLanes: 2);
        Run(sim, 60f, () => harvest.Current is UnloadTask);

        var t = sim.Machines.Spawn("tractor_95", new Vector2(170f, 258f), MathF.PI * 0.5f);
        var loader = sim.Machines.Spawn("baleloader_8", new Vector2(164f, 258f), MathF.PI * 0.5f);
        Assert.True(sim.Machines.Attach(t, "drawbar", loader));
        foreach (var k in Enumerable.Range(0, 3))
            sim.Objects.Spawn("round_bale", new Vector2(160f + k * 9f, 290f), 0f, Farm.PlayerId).Content!.Add("hay", 4000f);
        var collecting = sim.HireHelper(t, sim.World.FieldById(7)!);
        Run(sim, 20f);
        var setDown = collecting.Tasks.OfType<SetDownBalesTask>().Single();

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var h = Assert.IsType<HelperJob>(loaded.Machines.ById(combine.Id)!.Get<Drivable>()!.Controller);
        Assert.Equal(("harvest", typeof(UnloadTask)), (h.Type.Id, h.Current.GetType()));
        Assert.Equal(harvest.FieldWork!.Driver.Index, h.FieldWork!.Driver.Index);
        var c = Assert.IsType<HelperJob>(loaded.Machines.ById(t.Id)!.Get<Drivable>()!.Controller);
        Assert.Equal(("collectBales", collecting.TaskIndex), (c.Type.Id, c.TaskIndex));
        var saved = c.Tasks.OfType<SetDownBalesTask>().Single();
        Assert.Equal((setDown.Spot, setDown.Heading, setDown.Loads), (saved.Spot, saved.Heading, saved.Loads));
        Assert.Equal(collecting.Number, c.Number);

        // Both go on: the collector finishes its job.
        Run(loaded, 600f, () => c.Finished);
        Assert.True(c.Finished && !c.Stopped, c.StopReason?.Text);
        Assert.IsType<UnloadTask>(h.Current);
    }
}
