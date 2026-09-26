using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class PoiActionTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    /// <summary>A grain bin keeping up to 10,000 L of wheat, tipped into through its pit.</summary>
    private static readonly PoiDef Bin = new()
    {
        Id = "test_bin", Name = "Bin", W = 24, D = 16,
        Triggers = [new PoiTriggerDef { Id = "pit", Type = "unload", W = 20, D = 12 }],
        Storage = new PoiStorageDef { FillTypes = ["wheat"], Capacity = 10_000 },
        Actions = [new PoiActionDef { Type = "store", Trigger = "pit" }],
    };

    /// <summary>A press turning 100 L of canola into 40 L of diesel every half hour, for $10 an hour.</summary>
    private static PoiDef Press(float cycleHours = 0.5f) => new()
    {
        Id = "test_press", Name = "Press",
        Storage = new PoiStorageDef { FillTypes = ["canola", "diesel"], Capacity = 1000 },
        Actions =
        [
            new PoiActionDef
            {
                Type = "process", CycleHours = cycleHours, RunningCost = 10,
                Inputs = [new FillAmountDef { FillType = "canola", Amount = 100 }],
                Outputs = [new FillAmountDef { FillType = "diesel", Amount = 40 }],
            },
        ],
    };

    [Fact]
    public void TheOwnerStoresLoadsUntilTheStorageIsFull()
    {
        var sim = SimWith([Bin],
            new PoiPlacementDef { Id = "ours", Type = "test_bin", X = 20, Z = 20, Farm = Farm.PlayerId },
            new PoiPlacementDef { Id = "theirs", Type = "test_bin", X = 20, Z = 48 });
        var stored = Record<FillStored>(sim);
        var (t, trailer) = TrailerAt(sim, new Vector2(20f, 20f), "wheat", 12_000f);
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        sim.CommandUnload();
        Run(sim, 40f);

        var bin = sim.World.PoiById("ours")!;
        Assert.Equal(10_000f, bin.Storage!.Level("wheat"), 1);
        Assert.Equal(2_000f, trailer.Unit("main")!.Level, 1);
        Assert.False(trailer.Tipping);
        Assert.Equal(money, sim.Economy.Money);
        Assert.Equal(("ours", 10_000f), (Assert.Single(stored).Poi.Id, MathF.Round(stored[0].Amount)));

        sim.CommandUnload();
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Bin has no room for Wheat");
        sim.Machines.Teleport(t, new Vector2(26f, 48f), MathF.PI / 2f);
        sim.CommandUnload();
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Bin belongs to another farm");
    }

    [Fact]
    public void ProcessingTurnsStoredInputsIntoOutputsEveryHour()
    {
        var sim = SimWith([Press()], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        var produced = Record<PoiProduced>(sim);
        var press = sim.World.PoiById("press")!;
        press.Storage!.Add("canola", 1000f);
        var money = sim.Economy.Money;

        sim.SkipHours(3);
        Assert.Equal((400f, 240f), (press.Storage.Level("canola"), press.Storage.Level("diesel")));
        Assert.Equal(money - 30f, sim.Economy.Money);
        Assert.All(produced, e => Assert.Equal(("diesel", 80f), (e.FillType, e.Amount)));

        // Out of canola after two more hours: it stops, and so do the costs.
        sim.SkipHours(4);
        Assert.Equal((0f, 400f), (press.Storage.Level("canola"), press.Storage.Level("diesel")));
        Assert.Equal(money - 50f, sim.Economy.Money);
    }

    [Fact]
    public void SlowCyclesKeepTheirProgressAcrossASave()
    {
        var sim = SimWith([Press(cycleHours: 3f)], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        sim.World.PoiById("press")!.Storage!.Add("canola", 150f);
        sim.SkipHours(2);
        Assert.Equal(0f, sim.World.PoiById("press")!.Storage!.Level("diesel"));

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var press = loaded.World.PoiById("press")!;
        Assert.Equal(150f, press.Storage!.Level("canola"));
        loaded.SkipHours(1);
        Assert.Equal((50f, 40f), (press.Storage.Level("canola"), press.Storage.Level("diesel")));
        // Not enough canola for another cycle: it idles.
        loaded.SkipHours(6);
        Assert.Equal(40f, press.Storage.Level("diesel"));
    }

    [Fact]
    public void TheGasStationRefuelsAndWashes()
    {
        var sim = TestContent.NewSim();
        var gas = sim.World.PoiById("gas")!;
        var t = sim.Machines.Spawn("tractor_95", gas.Trigger("pumps")!.Area.Center, MathF.PI / 2f);
        var tank = t.Unit("fuel")!;
        tank.Remove(100f);
        t.Dirt = 0.5f;
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        Assert.Equal(["Refuel"], sim.Pois.UseOptions(t));

        sim.CommandUse();
        Assert.Equal(180f, tank.Level);
        Assert.Equal(money - 100f * sim.Economy.Price("diesel", sim.Clock.Month), sim.Economy.Money, 1);
        Assert.Equal(0.5f, t.Dirt);

        sim.Machines.Teleport(t, gas.Trigger("wash")!.Area.Center, 0f);
        money = sim.Economy.Money;
        sim.CommandUse();
        Assert.Equal(0f, t.Dirt);
        Assert.Equal(money - 20f, sim.Economy.Money, 1);
        sim.CommandUse();
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing to wash");
    }

    [Fact]
    public void TheWorkshopRepairsTheWholeChainForWhatItCosts()
    {
        var sim = TestContent.NewSim();
        var repaired = Record<MachineRepaired>(sim);
        var bay = sim.World.PoiById("workshop")!.Trigger("bay")!;
        var (t, trailer) = TrailerAt(sim, bay.Area.Center - new Vector2(4f, 0f), "wheat", 0f);
        t.Condition = trailer.Condition = 0.5f;
        sim.Player.Enter(t);
        var cost = (72_000f + 21_000f) / 100f * 0.5f;
        Assert.Equal([$"Repair (${cost:N0})"], sim.Pois.UseOptions(t));

        sim.Economy.Spend(sim.Economy.Money - 100f);
        sim.CommandUse();
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Not enough money");
        Assert.Empty(repaired);

        sim.Economy.Earn(1000f);
        sim.CommandUse();
        Assert.Equal((1f, 1f), (t.Condition, trailer.Condition));
        Assert.Equal(1100f - cost, sim.Economy.Money, 1);
        Assert.Equal([t, trailer], repaired.Select(e => e.Machine));
    }

    [Fact]
    public void ConditionAndDirtAreSaved()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.All.First(m => m.Def.Id == "tractor_95");
        (t.Condition, t.Dirt) = (0.7f, 0.3f);
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var same = loaded.Machines.ById(t.Id)!;
        Assert.Equal((0.7f, 0.3f), (same.Condition, same.Dirt));
    }
}
