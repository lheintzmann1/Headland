using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;
using Headland.Core.World;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class WearTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static void Drive(Machine m, float throttle) =>
        m.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = throttle } };

    private static float WearOf(Machine m) => m.Get<Wearable>()!.Wear;

    /// <summary>How much a tractor (and its implement) wear in <paramref name="seconds"/> of driving from <paramref name="at"/>.</summary>
    private static (float tractor, float implement) Wear(Vector2 at, float heading, bool working, float seconds = 20f)
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", at, heading);
        var c = sim.Machines.Spawn("cultivator_3", at, heading);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        c.Get<Attachable>()!.Lowered = working;
        Drive(t, 0.3f);
        Run(sim, seconds);
        return (WearOf(t), WearOf(c));
    }

    /// <summary>How dirty a tractor (and its implement) get in <paramref name="seconds"/> of driving from <paramref name="at"/>.</summary>
    private static (float tractor, float implement) Dirt(Vector2 at, float heading, bool working, float seconds = 20f)
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", at, heading);
        var c = sim.Machines.Spawn("cultivator_3", at, heading);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        c.Get<Attachable>()!.Lowered = working;
        Drive(t, 0.3f);
        Run(sim, seconds);
        return (t.Dirt, c.Dirt);
    }

    [Fact]
    public void MachinesGetDirtyFasterOnFieldsAndWorking()
    {
        Assert.All(TestContent.Content.Machines.Values, m => Assert.NotNull(m.Get<WashableDef>()));
        var road = Dirt(new Vector2(60f, 248f), MathF.PI / 2f, working: false);
        var field = Dirt(new Vector2(85f, 66f), 0f, working: false);
        var work = Dirt(new Vector2(85f, 66f), 0f, working: true);
        // Some 90 minutes of driving on a road from clean to caked: 20 s is a little.
        Assert.InRange(road.tractor, 0.001f, 20f / (90f * 60f));
        Assert.True(field.tractor > 1.8f * road.tractor, $"road {road.tractor}, field {field.tractor}");
        // Working adds to what the field gives the implement (at the share of its work speed it goes), not to its tractor.
        Assert.True(work.implement > 1.3f * field.implement, $"field {field.implement}, working {work.implement}");
        Assert.Equal(field.tractor, work.tractor, 4);

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": { "washable": { "dirtMinutes": 0, "fieldFactor": 0.5 } } }]
            """)).Message;
        Assert.Contains("machine 'x' washable: dirtMinutes and rainMinutes must be > 0", bad);
        Assert.Contains("machine 'x' washable: fieldFactor must be >= 1, workFactor >= 0", bad);
    }

    [Fact]
    public void AStandingMachineStaysAsItIsAndRainRinsesItToHalf()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(60f, 248f), MathF.PI / 2f);
        var dry = Enumerable.Range(sim.Clock.DayIndex + 1, 120).First(d => sim.Weather.GetDay(d).PrecipMm == 0f);
        var drydate = sim.Calendar.DateOfDay(dry);
        TestContent.SkipTo(sim, drydate.Month, drydate.Day, 12.5f);
        t.Dirt = 0.9f;
        Run(sim, 5f);
        Assert.Equal(0.9f, t.Dirt, 4);

        // A rainy hour above freezing: a minute rinses it down to half dirty, and no further.
        var day = Enumerable.Range(sim.Clock.DayIndex + 1, 120).First(d => sim.Weather.GetDay(d) is { PrecipMm: > 1f, PrecipHours: > 1, TempMin: > 3f });
        var date = sim.Calendar.DateOfDay(day);
        TestContent.SkipTo(sim, date.Month, date.Day, sim.Weather.GetDay(day).PrecipStartHour + 0.5f);
        sim.Tick(Dt);
        Assert.Contains(sim.Weather.Condition, new[] { Weather.WeatherCondition.Rain, Weather.WeatherCondition.Storm });
        Run(sim, 12f);
        Assert.InRange(t.Dirt, 0.65f, 0.75f);
        Run(sim, 30f);
        Assert.Equal(0.5f, t.Dirt);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(t.Id)!;
        Assert.Equal(0.5f, loaded.Dirt);
    }

    [Fact]
    public void EveryMachineWears()
    {
        Assert.All(TestContent.Content.Machines.Values, m => Assert.NotNull(m.Get<WearableDef>()));
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": { "wearable": { "hours": 0, "fieldFactor": 0.5, "powerLoss": 1 } } }]
            """));
        Assert.Contains("machine 'x' wearable: hours must be > 0, fieldFactor and workFactor >= 1", bad.Message);
        Assert.Contains("machine 'x' wearable: powerLoss and speedLoss must be in [0, 1), usageIncrease >= 0", bad.Message);
    }

    [Fact]
    public void AMachineWearsOnlyWhileItMovesOrWorks()
    {
        var sim = TestContent.NewSim();
        // On the road, heading east.
        var t = sim.Machines.Spawn("tractor_125", new Vector2(60f, 248f), MathF.PI / 2f);
        Run(sim, 30f);
        Assert.Equal(0f, WearOf(t));

        Drive(t, 0.3f);
        var moving = 0f;
        for (var s = 0f; s < 60f; s += Dt)
        {
            sim.Tick(Dt);
            if (MathF.Abs(t.Speed) > 0.05f) moving += Dt;
        }
        // 100% in 16 hours of driving.
        Assert.Equal(moving / (16f * 3600f), WearOf(t), 6);
        t.Get<Drivable>()!.Controller = null;
        Run(sim, 5f);
        var stopped = WearOf(t);
        Run(sim, 30f);
        Assert.Equal(stopped, WearOf(t));
    }

    [Fact]
    public void AMachineWearsFasterOnAFieldAndMuchFasterWorking()
    {
        var road = Wear(new Vector2(60f, 248f), MathF.PI / 2f, working: false);
        // Field 1, the farm's: south into it.
        var field = Wear(new Vector2(85f, 66f), 0f, working: false);
        var work = Wear(new Vector2(85f, 66f), 0f, working: true);
        Assert.InRange(field.tractor / road.tractor, 1.8f, 2.2f);
        // Working, the tractor pulling the cultivator wears as fast as it: five times faster again.
        Assert.InRange(work.tractor / field.tractor, 4.5f, 5.5f);
        Assert.InRange(work.implement / field.implement, 4.5f, 5.5f);
    }

    [Fact]
    public void AWornMachineDoesItsWorkWorse()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(85f, 66f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(85f, 64f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        var motor = t.Get<Motor>()!;
        t.Get<Wearable>()!.Condition = 0f;
        c.Get<Wearable>()!.Condition = 0f;
        Assert.Equal(125f * 0.7f, motor.PowerHp, 3);

        // A worn cultivator works at 70% of its 14 km/h at most (the worn engine is short of power for it, too).
        c.Get<Attachable>()!.Lowered = true;
        Drive(t, 1f);
        var top = 0f;
        for (var s = 0f; s < 15f; s += Dt)
        {
            sim.Tick(Dt);
            top = MathF.Max(top, t.Speed);
        }
        Assert.InRange(top * 3.6f, 8.5f, 14f * 0.7f + 0.01f);

        // The worn engine burns 30% more for the same power: idling, for one.
        Drive(t, 0f);
        Run(sim, 5f);
        var worn = motor.FuelPerHour;
        t.Get<Wearable>()!.Condition = 1f;
        sim.Tick(Dt);
        Assert.Equal(1.3f, worn / motor.FuelPerHour, 3);
    }

    [Fact]
    public void AWornSeederUsesMoreSeed()
    {
        float SeedPerCell(float condition)
        {
            var sim = TestContent.NewSim();
            var t = sim.Machines.Spawn("tractor_125", new Vector2(90f, 262f), 0f);
            var s = sim.Machines.Spawn("seeder_3", new Vector2(90f, 257f), 0f);
            Assert.True(sim.Machines.Attach(t, "drawbar", s));
            s.Get<Wearable>()!.Condition = condition;
            // Little in the hopper: a few grams of canola a cell would be lost in a full one's precision.
            s.Unit("seed")!.Remove(s.Unit("seed")!.Level - 20f);
            s.Get<Attachable>()!.Lowered = true;
            s.Get<WorkAreas>()!.On = true;
            var before = s.Unit("seed")!.Level;
            Drive(t, 1f);
            Run(sim, 8f);
            var sown = Enumerable.Range(0, sim.World.Layers.Crop.Length).Count(i => sim.World.Layers.Crop[i] != 0 && sim.World.Layers.FieldId[i] == 3);
            return (before - s.Unit("seed")!.Level) / sown;
        }
        Assert.Equal(1.3f, SeedPerCell(0f) / SeedPerCell(1f), 2);
    }

    [Fact]
    public void AWornMachineSaysSoUntilItIsRepaired()
    {
        var sim = TestContent.NewSim();
        var worn = Record<MachineWorn>(sim);
        var bay = sim.World.PoiById("workshop")!.Trigger("repair")!;
        var t = sim.Machines.Spawn("tractor_95", bay.Area.Center, 0f);
        var wear = t.Get<Wearable>()!;
        wear.Condition = Wearable.WornBelow + 1e-6f;
        Assert.Empty(t.Conditions);
        Drive(t, 0.3f);
        Run(sim, 2f);
        t.Get<Drivable>()!.Controller = null;
        Run(sim, 3f);

        Assert.Equal([new MachineWorn(t)], worn);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Fieldmaster 95 is worn: repair it at a workshop");
        var condition = Assert.IsType<Worn>(Assert.Single(t.Conditions));
        Assert.Equal($"Worn ({wear.Condition * 100f:0}%): repair it at a workshop", condition.Text);

        // 1% of its price for all of its wear.
        var price = sim.Pois.RepairPrice(sim.World.PoiById("workshop")!.Get<Pois.Components.Workshop>()!, t);
        Assert.Equal(72_000f / 100f * wear.Wear, price, 1);
        var money = sim.Economy.Money;
        sim.Activate(t);
        Assert.Equal((1f, money - price), (wear.Condition, sim.Economy.Money));
        Assert.Empty(t.Conditions);
    }

    [Fact]
    public void AFormat3SaveHandsTheConditionToTheWearable()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.All.First(m => m.Def.Id == "tractor_95");
        t.Get<Wearable>()!.Condition = 0.6f;
        var file = SaveGame.Capture(sim, "test");
        // Format 3 kept it beside the components.
        var state = JsonNode.Parse(file.State)!.AsObject();
        foreach (var m in state["machines"]!.AsArray())
        {
            var components = m!["components"]!.AsObject();
            m["condition"] = components["wearable"]!["condition"]!.GetValue<double>();
            components.Remove("wearable");
        }
        file.Meta.Format = 3;
        var loaded = SaveGame.Load(sim.Content, file with { State = Encoding.UTF8.GetBytes(state.ToJsonString()) });
        Assert.Empty(loaded.Warnings);
        Assert.Equal(sim.Machines.All.Select(m => m.Get<Wearable>()!.Condition), loaded.Sim.Machines.All.Select(m => m.Get<Wearable>()!.Condition));
        Assert.Equal(0.6f, loaded.Sim.Machines.ById(t.Id)!.Get<Wearable>()!.Condition);
    }
}
