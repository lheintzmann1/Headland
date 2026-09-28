using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Pois;
using Headland.Core.Pois.Components;
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

    /// <summary>What a POI keeps: its fill units.</summary>
    private static FillUnits Stock(Poi poi) => poi.Get<FillUnits>()!;

    /// <summary>A unit keeping one fill type, named after it (as POIs name their storage).</summary>
    private static FillUnitDef Bin(string fillType, float capacity = 100_000f) => new() { Id = fillType, Capacity = capacity, FillTypes = [fillType] };

    /// <summary>A grain bin keeping up to 10,000 L of wheat, tipped into through its pit.</summary>
    private static readonly PoiDef WheatBin = new()
    {
        Id = "test_bin", Name = "Bin", W = 24, D = 16,
        Components =
        [
            new FillUnitsDef { Units = [Bin("wheat", 10_000)] },
            new SiloDef { UnloadTrigger = new AreaDef { W = 20, D = 12 } },
        ],
    };

    /// <summary>A press turning 100 L of canola into 40 L of <paramref name="output"/> every half hour, for $10 an hour.</summary>
    private static PoiDef Press(float cycleHours = 0.5f, string output = "diesel", string mode = "store") => new()
    {
        Id = "test_press", Name = "Press",
        Components =
        [
            new FillUnitsDef { Units = [Bin("canola", 1000), Bin(output, 1000)] },
            new SiloDef { LoadTrigger = new AreaDef { Z = 10, W = 20, D = 12 } },
            new ProductionPointDef
            {
                Productions =
                [
                    new ProductionDef
                    {
                        Id = "press", CycleHours = cycleHours, RunningCost = 10, PriceFactor = 0.5f,
                        Inputs = [new FillAmountDef { FillType = "canola", Amount = 100 }],
                        Outputs = [new ProductionOutputDef { FillType = output, Amount = 40, Mode = mode }],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void TheOwnerStoresLoadsUntilTheStorageIsFull()
    {
        var sim = SimWith([WheatBin],
            new PoiPlacementDef { Id = "ours", Type = "test_bin", X = 20, Z = 20, Farm = Farm.PlayerId },
            new PoiPlacementDef { Id = "theirs", Type = "test_bin", X = 20, Z = 48 });
        var stored = Record<FillStored>(sim);
        var (t, trailer) = TrailerAt(sim, new Vector2(20f, 20f), "wheat", 12_000f);
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        sim.Perform(InputActions.Unload);
        Run(sim, 40f);

        var bin = sim.World.PoiById("ours")!;
        Assert.Equal(10_000f, Stock(bin).Level("wheat"), 1);
        Assert.Equal(2_000f, trailer.Unit("main")!.Level, 1);
        Assert.False(trailer.Get<Tipper>()!.Tipping);
        Assert.Equal(money, sim.Economy.Money);
        Assert.Equal(("ours", 10_000f), (Assert.Single(stored).Poi.Id, MathF.Round(stored[0].Amount)));

        sim.Perform(InputActions.Unload);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Bin has no room for Wheat");
        sim.Machines.Teleport(t, new Vector2(26f, 48f), MathF.PI / 2f);
        sim.Perform(InputActions.Unload);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Bin belongs to another farm");
    }

    [Fact]
    public void ProcessingTurnsStoredInputsIntoOutputsEveryHour()
    {
        var sim = SimWith([Press()], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        var produced = Record<PoiProduced>(sim);
        var press = sim.World.PoiById("press")!;
        Stock(press).Add("canola", 1000f);
        var money = sim.Economy.Money;

        sim.SkipHours(3);
        Assert.Equal((400f, 240f), (Stock(press).Level("canola"), Stock(press).Level("diesel")));
        Assert.Equal(money - 30f, sim.Economy.Money);
        Assert.All(produced, e => Assert.Equal(("diesel", 80f), (e.FillType, e.Amount)));

        // Out of canola after two more hours: it stops, and so do the costs.
        sim.SkipHours(4);
        Assert.Equal((0f, 400f), (Stock(press).Level("canola"), Stock(press).Level("diesel")));
        Assert.Equal(money - 50f, sim.Economy.Money);
    }

    [Fact]
    public void SoldOutputsPayTheOwnerEveryHour()
    {
        var sim = SimWith([Press(mode: "sell")], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        var sold = Record<ProductionSold>(sim);
        var press = sim.World.PoiById("press")!;
        Stock(press).Add("canola", 1000f);
        var money = sim.Economy.Money;

        sim.SkipHours(2);
        var income = 80f * sim.Economy.Price("diesel", sim.Clock.Month) * 0.5f;
        Assert.Equal(0f, Stock(press).Level("diesel"));
        Assert.Equal([("diesel", 80f, income), ("diesel", 80f, income)], sold.Select(e => (e.FillType, e.Amount, e.Income)));
        Assert.Equal(money - 20f + 2f * income, sim.Economy.Money, 1);
        Assert.Equal(160f, sim.Statistics.Sold["diesel"]);
    }

    [Fact]
    public void StoredOutputsWaitForTheOwnersTrailer()
    {
        var sim = SimWith([Press(output: "wheat")], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        var press = sim.World.PoiById("press")!;
        Stock(press).Add("canola", 1000f);
        sim.SkipHours(2);
        Assert.Equal(160f, Stock(press).Level("wheat"));

        var (t, trailer) = TrailerAt(sim, press.Trigger("load")!.Area.Center, "wheat", 0f);
        sim.Player.Enter(t);
        sim.Perform(InputActions.Use);
        Run(sim, 2f);
        Assert.Equal(160f, trailer.Unit("main")!.Level, 1);
        Assert.Equal(0f, Stock(press).Level("wheat"), 1);
    }

    [Fact]
    public void TheMillBuysWheatAndMillsItIntoFlour()
    {
        var sim = TestContent.NewSim();
        var mill = sim.World.PoiById("mill")!;
        var produced = Record<PoiProduced>(sim);
        var (t, trailer) = TrailerAt(sim, mill.Trigger("unload")!.Area.Center, "wheat", 16_000f);
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        sim.Perform(InputActions.Unload);
        Run(sim, 45f);
        Assert.True(trailer.Unit("main")!.IsEmpty);
        Assert.Equal(16_000f, Stock(mill).Level("wheat"), 1);
        Assert.Equal(1.1f * SaleIncome(sim, "wheat", 16_000f), sim.Economy.Money - money, 0);

        // It mills a tonne an hour and ships the flour; the farmer earns nothing from that.
        money = sim.Economy.Money;
        sim.SkipHours(3);
        Assert.Equal(13_000f, Stock(mill).Level("wheat"), 1);
        Assert.Equal((0f, 3f * 580f), (Stock(mill).Level("flour"), produced.Sum(e => e.Amount)));
        Assert.Equal(money, sim.Economy.Money);

        Stock(mill).Add("wheat", 60_000f);
        trailer.Unit("main")!.Add("wheat", 5000f);
        sim.Perform(InputActions.Unload);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Flour Mill has no room for Wheat");
    }

    [Fact]
    public void SlowCyclesKeepTheirProgressAcrossASave()
    {
        var sim = SimWith([Press(cycleHours: 3f)], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        Stock(sim.World.PoiById("press")!).Add("canola", 150f);
        sim.SkipHours(2);
        Assert.Equal(0f, Stock(sim.World.PoiById("press")!).Level("diesel"));

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var press = loaded.World.PoiById("press")!;
        Assert.Equal(150f, Stock(press).Level("canola"));
        loaded.SkipHours(1);
        Assert.Equal((50f, 40f), (Stock(press).Level("canola"), Stock(press).Level("diesel")));
        // Not enough canola for another cycle: it idles.
        loaded.SkipHours(6);
        Assert.Equal(40f, Stock(press).Level("diesel"));
    }

    [Fact]
    public void TheGasStationRefuelsAndWashes()
    {
        var sim = TestContent.NewSim();
        var gas = sim.World.PoiById("gas")!;
        var t = sim.Machines.Spawn("tractor_95", gas.Trigger("fill")!.Area.Center, MathF.PI / 2f);
        var tank = t.Unit("fuel")!;
        tank.Remove(100f);
        t.Dirt = 0.5f;
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        Assert.Equal(["Refuel with diesel"], sim.Pois.Describe(gas.Trigger("fill")!));
        Assert.Equal(["Refuel"], sim.Pois.UseOptions(t));

        sim.Perform(InputActions.Use);
        Assert.Equal(180f, tank.Level);
        Assert.Equal(money - 100f * sim.Economy.Price("diesel", sim.Clock.Month), sim.Economy.Money, 1);
        Assert.Equal(0.5f, t.Dirt);

        sim.Machines.Teleport(t, gas.Trigger("wash")!.Area.Center, 0f);
        money = sim.Economy.Money;
        sim.Perform(InputActions.Use);
        Assert.Equal(0f, t.Dirt);
        Assert.Equal(money - 20f, sim.Economy.Money, 1);
        sim.Perform(InputActions.Use);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing to wash");
    }

    [Fact]
    public void TheWorkshopRepairsTheWholeChainForWhatItCosts()
    {
        var sim = TestContent.NewSim();
        var repaired = Record<MachineRepaired>(sim);
        var bay = sim.World.PoiById("workshop")!.Trigger("repair")!;
        var (t, trailer) = TrailerAt(sim, bay.Area.Center - new Vector2(4f, 0f), "wheat", 0f);
        t.Get<Wearable>()!.Condition = trailer.Get<Wearable>()!.Condition = 0.5f;
        sim.Player.Enter(t);
        var cost = (72_000f + 21_000f) / 100f * 0.5f;
        Assert.Equal([$"Repair (${cost:N0})", "Change options…"], sim.Pois.UseOptions(t));

        sim.Economy.Spend(sim.Economy.Money - 100f, MoneyCategory.Other);
        sim.Perform(InputActions.Use);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Not enough money");
        Assert.Empty(repaired);

        sim.Economy.Earn(1000f, MoneyCategory.Other);
        sim.Perform(InputActions.Use);
        Assert.Equal((1f, 1f), (t.Get<Wearable>()!.Condition, trailer.Get<Wearable>()!.Condition));
        Assert.Equal(1100f - cost, sim.Economy.Money, 1);
        Assert.Equal([t, trailer], repaired.Select(e => e.Machine));
    }

    /// <summary>A silo keeping wheat and barley: tip into its pit, load from its spout.</summary>
    private static readonly PoiDef GrainSilo = new()
    {
        Id = "test_silo", Name = "Silo", W = 24, D = 30,
        Components =
        [
            new FillUnitsDef { Units = [Bin("wheat"), Bin("barley")] },
            new SiloDef { UnloadTrigger = new AreaDef { Z = -8, W = 20, D = 12 }, LoadTrigger = new AreaDef { Z = 8, W = 20, D = 12 }, LoadRate = 500 },
        ],
    };

    [Fact]
    public void ThePipeUnloadsTheCombineIntoAnUnloadingArea()
    {
        var sim = TestContent.NewSim();
        var sold = Record<FillSold>(sim);
        var pit = sim.World.PoiById("elevator")!.Trigger("unload")!;
        var pipe = sim.Content.Machines["combine_7"].Get<PipeDef>()!;
        var combine = sim.Machines.Spawn("combine_7", pit.Area.Center - new Vector2(pipe.X, pipe.Z), 0f);
        combine.Unit("tank")!.Add("wheat", 3000f);
        var money = sim.Economy.Money;
        combine.Get<Pipe>()!.Out = true;
        Run(sim, 40f);

        Assert.True(combine.Unit("tank")!.IsEmpty);
        Assert.Equal((combine, 3000f), (Assert.Single(sold).Machine, MathF.Round(sold[0].Amount)));
        Assert.Equal(SaleIncome(sim, "wheat", 3000f), sold[0].Income, 1);
        Assert.InRange(sim.Economy.Money - money, sold[0].Income - 5f, sold[0].Income + 5f);
    }

    [Fact]
    public void TheOwnersTrailerLoadsFromStorageAcrossASave()
    {
        var sim = SimWith([GrainSilo],
            new PoiPlacementDef { Id = "ours", Type = "test_silo", X = 20, Z = 30, Farm = Farm.PlayerId },
            new PoiPlacementDef { Id = "theirs", Type = "test_silo", X = 46, Z = 30 });
        var silo = sim.World.PoiById("ours")!;
        Stock(silo).Add("wheat", 5000f);
        Stock(silo).Add("barley", 3000f);
        var (t, trailer) = TrailerAt(sim, silo.Trigger("load")!.Area.Center, "wheat", 0f);
        sim.Player.Enter(t);
        Assert.Equal(["wheat", "barley"], sim.Pois.LoadChoices(t));
        Assert.Equal(["Load…"], sim.Pois.UseOptions(t));

        sim.Perform(InputActions.Use);
        Assert.True(sim.Pois.IsLoading(t));
        Run(sim, 5f);
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var events = Record<FillLoaded>(loaded);
        var same = loaded.Machines.ById(trailer.Id)!;
        Run(loaded, 10f);

        Assert.Equal(("wheat", 5000f), (same.Unit("main")!.FillType, MathF.Round(same.Unit("main")!.Level)));
        Assert.Equal(0f, Stock(loaded.World.PoiById("ours")!).Level("wheat"));
        Assert.False(loaded.Pois.IsLoading(same.Parent!));
        Assert.Equal(("wheat", 5000f), (Assert.Single(events).FillType, MathF.Round(events[0].Amount)));

        // Barley doesn't mix with the wheat on board, and the neighbor's silo is not ours.
        loaded.Perform(InputActions.Use);
        Assert.Contains(loaded.Notifications.Items, n => n.Text == "Nothing stored here fits Tipper 16");
        loaded.Machines.Teleport(same.Parent!, loaded.World.PoiById("theirs")!.Trigger("load")!.Area.Center + new Vector2(6f, 0f), MathF.PI / 2f);
        loaded.Perform(InputActions.Use);
        Assert.Contains(loaded.Notifications.Items, n => n.Text == "Silo belongs to another farm");
    }

    [Fact]
    public void TheUseKeyStopsLoading()
    {
        var sim = SimWith([GrainSilo], new PoiPlacementDef { Id = "ours", Type = "test_silo", X = 20, Z = 30, Farm = Farm.PlayerId });
        Stock(sim.World.PoiById("ours")!).Add("barley", 8000f);
        var (t, trailer) = TrailerAt(sim, sim.World.PoiById("ours")!.Trigger("load")!.Area.Center, "wheat", 0f);
        var loaded = Record<FillLoaded>(sim);
        sim.Player.Enter(t);
        sim.Perform(InputActions.Use);
        Run(sim, 2f);
        sim.Perform(InputActions.Use);
        Assert.False(sim.Pois.IsLoading(t));
        Assert.InRange(Assert.Single(loaded).Amount, 900f, 1100f);
        Assert.Equal(loaded[0].Amount, trailer.Unit("main")!.Level);
    }

    [Fact]
    public void NewMachinesAreDeliveredToFreeSpots()
    {
        var sim = TestContent.NewSim();
        var shed = sim.World.PoiById("shed")!;
        var spot = shed.Trigger("delivery")!;
        var delivered = new List<Machines.Machine>();
        while (sim.Pois.Deliver("tractor_125", Farm.PlayerId, shed) is { } m) delivered.Add(m);

        Assert.Equal(3, delivered.Count);
        Assert.All(delivered, m => Assert.True(spot.Contains(m.Footprint.Center) && m.Heading == shed.Heading));
        for (var i = 0; i < delivered.Count; i++)
        for (var j = i + 1; j < delivered.Count; j++)
            Assert.False(Machines.Geometry.Overlaps(delivered[i].Footprint, delivered[j].Footprint));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Fieldmaster 125 delivered at Machine shed");
    }

    [Fact]
    public void TheFarmSiloStoresTheFarmsGrainAndLoadsItBack()
    {
        var sim = TestContent.NewSim();
        var silo = sim.World.PoiById("silo")!;
        Assert.Equal(Farm.PlayerId, silo.FarmId);
        var (t, trailer) = TrailerAt(sim, silo.Trigger("unload")!.Area.Center, "barley", 9000f);
        var stored = Record<FillStored>(sim);
        var money = sim.Economy.Money;
        sim.Player.Enter(t);
        sim.Perform(InputActions.Unload);
        Run(sim, 40f);
        Assert.Equal(9000f, Stock(silo).Level("barley"), 1);
        Assert.True(trailer.Unit("main")!.IsEmpty);
        Assert.Equal(money, sim.Economy.Money);
        Assert.Equal((silo, "barley", 9000f), (Assert.Single(stored).Poi, stored[0].FillType, MathF.Round(stored[0].Amount)));

        sim.Machines.Teleport(t, silo.Trigger("load")!.Area.Center + new Vector2(6f, 0f), MathF.PI / 2f);
        Assert.Equal(["Load barley"], sim.Pois.UseOptions(t));
        sim.Perform(InputActions.Use);
        Run(sim, 30f);
        Assert.Equal(9000f, trailer.Unit("main")!.Level, 1);
        Assert.Equal(0f, Stock(silo).Level("barley"), 1);
    }

    [Fact]
    public void ClosedPoisSayWhenTheyOpen()
    {
        var sim = TestContent.NewSim();
        var yard = sim.World.PoiById("supplies")!.Trigger("fill")!;
        var seeder = sim.Machines.Spawn("seeder_3", yard.Area.Center, 0f);
        seeder.Unit("seed")!.Remove(500f);
        Assert.Equal(["Buy seeds, fertilizer, herbicide (7:00–19:00)"], sim.Pois.Describe(yard));
        Assert.Equal(["Buy seeds"], sim.Pois.UseOptions(seeder));

        sim.SkipHours(13);
        Assert.Empty(sim.Pois.UseOptions(seeder));
        sim.Pois.Use(seeder);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Farm Supplies is closed: open 7:00–19:00");
        Assert.Equal(400f, seeder.Unit("seed")!.Level);
    }

    [Fact]
    public void LoadsUnderTheMinimumAreRefusedButStartedLoadsFinish()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = TrailerAt(sim, new Vector2(441f, 230f), "wheat", 400f);
        sim.Player.Enter(t);
        sim.Perform(InputActions.Unload);
        Assert.False(trailer.Get<Tipper>()!.Tipping);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Grain Elevator takes loads of 500 L or more");

        trailer.Unit("main")!.Add("wheat", 600f);
        sim.Perform(InputActions.Unload);
        Run(sim, 20f);
        Assert.True(trailer.Unit("main")!.IsEmpty);
    }

    [Fact]
    public void ProcessingRunsOnlyInItsHoursAndMonths()
    {
        var hours = Press(cycleHours: 1f);
        hours.Get<ProductionPointDef>()!.Productions[0].OpenHours = [8, 12];
        var season = Press(cycleHours: 1f);
        season.Id = "test_press_september";
        season.Get<ProductionPointDef>()!.Productions[0].Months = [9];
        var sim = SimWith([hours, season],
            new PoiPlacementDef { Id = "hours", Type = "test_press", X = 20, Z = 30, Farm = Farm.PlayerId },
            new PoiPlacementDef { Id = "season", Type = "test_press_september", X = 44, Z = 30, Farm = Farm.PlayerId });
        foreach (var poi in sim.World.Pois) Stock(poi).Add("canola", 1000f);
        var money = sim.Economy.Money;

        // August 1st, 7:00 to the next morning: open from 8:00 to 12:00 only.
        sim.SkipHours(24);
        Assert.Equal(600f, Stock(sim.World.PoiById("hours")!).Level("canola"));
        Assert.Equal(1000f, Stock(sim.World.PoiById("season")!).Level("canola"));
        Assert.Equal(money - 40f, sim.Economy.Money);
    }

    /// <summary>A market paying 20% over the market price for wheat, where wheat is always in high demand.</summary>
    private static PoiDef Market(float highChance) => new()
    {
        Id = "test_market", Name = "Market",
        Components =
        [
            new SellingStationDef
            {
                FillTypes = ["wheat", "barley"], PriceFactors = new() { ["wheat"] = 1.2f },
                Demand = new DemandDef { HighChance = highChance, HighFactor = [1.5f, 1.5f], HighDays = [2, 2] },
            },
        ],
    };

    [Fact]
    public void SalesLowerTheDemandUntilItRecovers()
    {
        var sim = TestContent.NewSim();
        var elevator = sim.World.PoiById("elevator")!;
        var pit = elevator.Trigger("unload")!;
        var sell = elevator.Get<SellingStation>()!;
        var market = sim.Economy.Price("wheat", sim.Clock.Month);
        var trailer = sim.Machines.Spawn("trailer_16", pit.Area.Center, 0f);
        Assert.Equal(market, sim.Pois.Price(sell, "wheat"));

        sim.Pois.Unload(trailer, pit, "wheat", 50_000f);
        Assert.Equal(0.98f, sell.DemandOf("wheat"), 4);
        Assert.Equal(0.98f * market, sim.Pois.Price(sell, "wheat"), 4);
        Assert.Equal(1f, sell.DemandOf("barley"));

        sim.SkipHours(12);
        Assert.Equal(0.99f, sell.DemandOf("wheat"), 4);
        sim.SkipHours(12);
        Assert.Equal(1f, sell.DemandOf("wheat"));

        sim.Pois.Unload(trailer, pit, "wheat", 2_000_000f);
        Assert.Equal(0.7f, sell.DemandOf("wheat"), 4);
    }

    [Fact]
    public void HighDemandPaysMoreForAFewDays()
    {
        var sim = SimWith([Market(highChance: 1f)], new PoiPlacementDef { Id = "market", Type = "test_market", X = 30, Z = 30 });
        var started = Record<HighDemandStarted>(sim);
        var ended = Record<HighDemandEnded>(sim);
        var sell = sim.World.PoiById("market")!.Get<SellingStation>()!;

        sim.SkipHours(16);
        Assert.Empty(started);
        sim.SkipHours(1);
        var high = Assert.Single(started);
        Assert.Equal((1.5f, new Time.GameDate(1, 8, 3)), (high.Factor, high.Until));
        var boosted = high.FillType == "wheat" ? 1.2f * 1.5f : 1.5f;
        Assert.Equal(boosted * sim.Economy.Price(high.FillType, sim.Clock.Month), sim.Pois.Price(sell, high.FillType), 4);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(sell.HighDemand, loaded.World.PoiById("market")!.Get<SellingStation>()!.HighDemand);
        foreach (var s in new[] { sim, loaded }) s.SkipHours(48);
        Assert.Equal(high.FillType, Assert.Single(ended).FillType);
        Assert.Equal(2, started.Count);
        Assert.Equal(sell.HighDemand, loaded.World.PoiById("market")!.Get<SellingStation>()!.HighDemand);
    }

    [Fact]
    public void PriceFactorsAreOnTopOfTheMarketPrice()
    {
        var sim = SimWith([Market(highChance: 0f)], new PoiPlacementDef { Id = "market", Type = "test_market", X = 30, Z = 30 });
        var sell = sim.World.PoiById("market")!.Get<SellingStation>()!;
        var month = sim.Clock.Month;
        Assert.Equal(1.2f * sim.Economy.Price("wheat", month), sim.Pois.Price(sell, "wheat"), 4);
        Assert.Equal(sim.Economy.Price("barley", month), sim.Pois.Price(sell, "barley"), 4);
        sim.SkipHours(24 * 40);
        Assert.Null(sell.HighDemand);
    }

    [Fact]
    public void ConditionAndDirtAreSaved()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.All.First(m => m.Def.Id == "tractor_95");
        (t.Get<Wearable>()!.Condition, t.Dirt) = (0.7f, 0.3f);
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var same = loaded.Machines.ById(t.Id)!;
        Assert.Equal((0.7f, 0.3f), (same.Get<Wearable>()!.Condition, same.Dirt));
    }
}
