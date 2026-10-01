using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;

namespace Headland.Core.Tests;

/// <summary>What machines show on the HUD's vehicle panel comes from their components.</summary>
public class ReadoutTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static List<Gauge> Gauges(Simulation sim, Machine m) => m.Readouts(sim).OfType<Gauge>().ToList();

    private static List<string> States(Simulation sim, Machine m) => m.Readouts(sim).OfType<Status>().Select(s => s.Text).ToList();

    [Fact]
    public void AVehicleShowsItsSpeedEngineFuelAndCondition()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        Assert.Equal(["Engine off"], States(sim, t));
        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 1f } };
        Run(sim, 3f);

        var gauges = Gauges(sim, t);
        Assert.Equal(["speed", "load", "fuel", "condition"], gauges.Select(g => g.Kind));
        var speed = gauges[0];
        Assert.Equal(($"{MathF.Abs(t.Speed) * 3.6f:0} km/h", MathF.Abs(t.Speed) / t.Get<Motor>()!.MaxSpeed), (speed.Value, speed.Fraction));
        Assert.Matches(@"^2\d\d L, \d+\.\d L/h$", gauges[2].Value);
        Assert.Equal((Tone.Normal, "100%"), (gauges[3].Tone, gauges[3].Value));
        Assert.Empty(States(sim, t));

        // Low on fuel and worn, the gauges say so.
        t.Unit("fuel")!.Remove(240f);
        t.Get<Wearable>()!.Condition = 0.1f;
        gauges = Gauges(sim, t);
        Assert.Equal((Tone.Warning, Tone.Warning), (gauges[2].Tone, gauges[3].Tone));
    }

    [Fact]
    public void ACombineShowsItsThresherHeaderPipeAndTank()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        Assert.Contains("Thresher off", States(sim, combine));
        Assert.Equal(["Raised"], States(sim, header));
        var tank = Gauges(sim, combine).Single(g => g.Kind == "fill");
        Assert.Equal(("Empty", "0 / 9,000 L", Tone.Dim), (tank.Label, tank.Value, tank.Tone));

        combine.Get<Thresher>()!.On = true;
        header.Get<Attachable>()!.Lowered = true;
        combine.Get<Pipe>()!.Out = true;
        combine.Unit("tank")!.Add("wheat", 4500f);
        Assert.Contains("Threshing", States(sim, combine));
        Assert.Contains("Pipe out", States(sim, combine));
        Assert.Equal(["Lowered"], States(sim, header));
        tank = Gauges(sim, combine).Single(g => g.Kind == "fill");
        Assert.Equal(("Wheat", "4,500 / 9,000 L", 0.5f), (tank.Label, tank.Value, tank.Fraction));
        // Its fuel is the fuel gauge's, not a fill level.
        Assert.Single(Gauges(sim, combine), g => g.Kind == "fuel");
    }

    [Fact]
    public void ImplementsShowWhatTheyDoAndTheMachineItsLeaseAndWork()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_95", new Vector2(269f, 300f), 0f);
        var seeder = sim.Machines.Spawn("seeder_3", new Vector2(269f, 296f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", seeder));
        var states = States(sim, seeder);
        Assert.Contains("Off", states);
        Assert.Contains($"Sows {sim.Content.Crops[seeder.Get<WorkAreas>()!.Crop].Name.ToLowerInvariant()}", states);
        seeder.WorkedHa = 1.234f;
        seeder.LeaseContract = 3;
        Assert.Contains("1.23 ha worked", States(sim, seeder));
        Assert.Contains("Leased for a contract", States(sim, seeder));

        // A bale collector shows its bales; a helper shows on the vehicle.
        var loader = sim.Machines.Spawn("baleloader_8", new Vector2(250f, 300f), 0f);
        var bales = Gauges(sim, loader).Single(g => g.Kind == "bales");
        Assert.Equal(("0 / 8", Tone.Dim), (bales.Value, bales.Tone));
        var meadow = sim.World.FieldById(7)!;
        var mower = sim.Machines.Spawn("mower_3", new Vector2(167f, 265f), 0f);
        var t125 = sim.Machines.Spawn("tractor_125", new Vector2(167f, 268f), 0f);
        Assert.True(sim.Machines.Attach(t125, "rear", mower));
        var helper = sim.HireHelper(t125, meadow);
        Assert.Contains(States(sim, t125), s => s.StartsWith($"Helper {helper.Number} on Field 7: lane 1/"));
        Assert.Equal(Farm.PlayerId, t125.FarmId);
    }
}
