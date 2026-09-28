using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Crops;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class WorkTypeTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static void Drive(Machine m, float throttle) =>
        m.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = throttle } };

    private static int Count(Simulation sim, int field, Func<int, bool> predicate)
    {
        var L = sim.World.Layers;
        return Enumerable.Range(0, L.FieldId.Length).Count(i => L.FieldId[i] == field && predicate(i));
    }

    /// <summary>A vehicle at <paramref name="at"/> heading south (+z), with <paramref name="implement"/> on its joint.</summary>
    private static (Machine vehicle, Machine implement) Hitched(Simulation sim, string vehicle, string implement, string joint, Vector2 at)
    {
        var v = sim.Machines.Spawn(vehicle, at, 0f);
        var i = sim.Machines.Spawn(implement, at - new Vector2(0f, 3f), 0f);
        Assert.True(sim.Machines.Attach(v, joint, i));
        return (v, i);
    }

    [Fact]
    public void EveryKindOfWorkIsRegisteredAndTheShippedMachinesDoIt()
    {
        Assert.Equal(["cultivator", "plow", "seeder", "harvester", "spreader", "sprayer", "mower"], WorkTypes.All.Select(t => t.Id));
        var done = TestContent.Content.Machines.Values
            .SelectMany(m => m.Get<WorkAreasDef>()?.Areas ?? [])
            .Select(a => a.Work.Id)
            .ToHashSet();
        Assert.Equal(WorkTypes.All.Select(t => t.Id).Order(), done.Order());
        Assert.Throws<ArgumentException>(() => WorkTypes.Register(new PlowWork()));
    }

    [Fact]
    public void WorkAreasAreCheckedByTheirKindOfWork()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": {
                 "attachable": {},
                 "fillUnits": { "units": [ { "id": "bin", "fillTypes": ["seeds"] } ] },
                 "workAreas": { "areas": [
                   { "type": "juggler" }, { "type": "spreader", "fillUnit": "bin" }, { "type": "sprayer", "ratePerHa": -1 },
                   { "type": "mower" } ] } } }]
            """));
        Assert.Contains("machine 'x' workAreas: unknown type 'juggler' (cultivator, plow, seeder, harvester, spreader, sprayer, mower)", bad.Message);
        Assert.Contains("machine 'x' workAreas: a spreader needs ratePerHa > 0", bad.Message);
        Assert.Contains("machine 'x' workAreas: a spreader spreads fertilizer: 'seeds' has no nitrogen", bad.Message);
        Assert.Contains("machine 'x' workAreas: sprayer: width, length and maxWorkSpeedKmh must be > 0, requiredPowerHp and ratePerHa >= 0", bad.Message);
        Assert.Contains("machine 'x' workAreas: a sprayer needs the fillUnit its herbicide comes from", bad.Message);
        Assert.Contains("machine 'x' workAreas: a mower needs harvestGroups", bad.Message);
    }

    [Fact]
    public void ACultivatorKillsSmallWeedsAndThePlowBuriesThemAll()
    {
        var sim = TestContent.NewSim();
        var (world, L) = (sim.World, sim.World.Layers);
        var cells = Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 1).Take(4).ToArray();
        byte[] weeds = [WeedState.None, WeedState.Small, WeedState.Grown, WeedState.Sprayed];
        for (var k = 0; k < 4; k++) L.Weeds[cells[k]] = weeds[k];

        foreach (var i in cells) Assert.True(CultivatorWork.Till(world, i, 0));
        Assert.Equal([WeedState.None, WeedState.None, WeedState.Grown, WeedState.None], cells.Select(i => L.Weeds[i]));
        Assert.All(cells, i => Assert.Equal((byte)GroundType.Cultivated, L.Ground[i]));
        // Tilled again, only the cell whose weeds survived is a change (and they survive it again).
        Assert.Equal([false, false, false, false], cells.Select(i => CultivatorWork.CanTill(world, i)));

        foreach (var i in cells) Assert.True(PlowWork.Plow(world, i, 0));
        Assert.All(cells, i => Assert.Equal((WeedState.None, (byte)GroundType.Plowed), (L.Weeds[i], L.Ground[i])));
        Assert.All(cells, i => Assert.True(SeederWork.CanSow(world, i)));
    }

    [Fact]
    public void APlowTurnsTheStubbleOver()
    {
        var sim = TestContent.NewSim();
        var (t, plow) = Hitched(sim, "tractor_125", "plow_5", "rear", new Vector2(85f, 64f));
        plow.Get<Attachable>()!.Lowered = true;
        Drive(t, 1f);
        Run(sim, 12f);
        var plowed = Count(sim, 1, i => sim.World.Layers.Ground[i] == (byte)GroundType.Plowed);
        // 2 m wide, some 25 m at up to 10 km/h.
        Assert.True(plowed > 4 * 40, $"plowed cells: {plowed}");
        Assert.True(t.Speed <= 10f / 3.6f + 0.01f, "the plow's work speed");
    }

    [Fact]
    public void ASpreaderGivesTheSoilTheNitrogenOfWhatItSpreads()
    {
        var sim = TestContent.NewSim();
        var (t, spreader) = Hitched(sim, "tractor_95", "spreader_24", "rear", new Vector2(85f, 64f));
        var L = sim.World.Layers;
        var nitrogen = (byte[])L.Nitrogen.Clone();
        var hopper = spreader.Unit("hopper")!;
        var before = hopper.Level;
        spreader.Get<WorkAreas>()!.On = true;
        Drive(t, 1f);
        Run(sim, 10f);

        var spread = Count(sim, 1, i => L.Fertilized[i] == 1);
        Assert.True(spread > 40 * 40, $"fertilized cells: {spread}");
        // 250 kg a hectare of 27% nitrogen: 68 kg of nitrogen.
        var i = Enumerable.Range(0, L.FieldId.Length).First(i => L.FieldId[i] == 1 && L.Fertilized[i] == 1);
        Assert.Equal(Math.Min(255, nitrogen[i] + 68), L.Nitrogen[i]);
        var used = before - hopper.Level;
        var cellsSpread = Enumerable.Range(0, L.Fertilized.Length).Count(c => L.Fertilized[c] > 0);
        Assert.Equal(cellsSpread * 250f * WorldMap.CellArea / 10000f, used, 1);
    }

    [Fact]
    public void ASprayerUnfoldsKillsTheWeedsAndKeepsThemOutUntilHarvest()
    {
        var sim = TestContent.NewSim();
        var (t, sprayer) = Hitched(sim, "tractor_95", "sprayer_12", "drawbar", new Vector2(85f, 66f));
        var L = sim.World.Layers;
        for (var i = 0; i < L.Weeds.Length; i++)
            if (L.FieldId[i] == 1) L.Weeds[i] = WeedState.Grown;
        var tank = sprayer.Unit("tank")!;
        var before = tank.Level;

        // It comes folded: lowering it unfolds the boom first.
        sim.Player.Enter(t);
        Assert.True(sprayer.Get<AnimatedParts>()!.Folded);
        sim.CommandLower();
        sim.CommandTurnOn();
        Run(sim, 6f);
        Assert.True(sprayer.Get<AnimatedParts>()!.Unfolded);
        sim.Player.Controls.Input = new VehicleInput { Throttle = 1f };
        Run(sim, 10f);

        var sprayed = Count(sim, 1, i => L.Weeds[i] == WeedState.Sprayed);
        Assert.True(sprayed > 20 * 40, $"sprayed cells: {sprayed}");
        Assert.Equal(sprayed * 100f * WorldMap.CellArea / 10000f, before - tank.Level, 1);

        // No weeds come up where it sprayed, until the harvest.
        var cell = Enumerable.Range(0, L.FieldId.Length).First(i => L.FieldId[i] == 1 && L.Weeds[i] == WeedState.Sprayed);
        L.Weeds[cell + 1] = WeedState.None;
        L.Ground[cell + 1] = L.Ground[cell];
        L.FieldId[cell + 1] = 1;
        for (var h = 0; h < 24 * 6; h++)
        {
            sim.Weather.ForceSnapshot(18f, 0f);
            sim.Crops.TickHour(sim.Weather, sim.Clock.DayIndex, h);
        }
        Assert.Equal(WeedState.Sprayed, L.Weeds[cell]);
        Assert.NotEqual(WeedState.None, L.Weeds[cell + 1]);
        HarvesterWork.Clear(sim.World, cell, 0);
        Assert.Equal(WeedState.None, L.Weeds[cell]);
    }

    [Fact]
    public void WeedsComeUpOnTilledFieldGroundInGrowingWeather()
    {
        var sim = TestContent.SmallSim();
        var L = sim.World.Layers;
        void Hours(int hours, float temp)
        {
            for (var h = 0; h < hours; h++)
            {
                sim.Weather.ForceSnapshot(temp, 0f);
                sim.Crops.TickHour(sim.Weather, sim.Clock.DayIndex, h);
            }
        }

        Hours(24 * 3, 2f);
        Assert.Equal(0, Count(sim, 1, i => L.Weeds[i] != WeedState.None));
        Hours(24 * 3, 16f);
        var field = Count(sim, 1, _ => true);
        var weedy = Count(sim, 1, i => WeedState.Living(L.Weeds[i]));
        Assert.InRange(weedy, field / 2, field - 1);
        Assert.True(Count(sim, 1, i => L.Weeds[i] == WeedState.Grown) > 0);
        // Only on field ground, and not on a meadow's sward.
        Assert.Equal(0, Count(sim, 0, i => L.Weeds[i] != WeedState.None));
        var grass = sim.Content.CropIndex("grass");
        var meadow = Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 1 && L.Weeds[i] == WeedState.None).Take(50).ToList();
        foreach (var i in meadow) SeederWork.Sow(sim.World, sim.Content, i, grass, 255, 0);
        Hours(24 * 6, 16f);
        Assert.All(meadow, i => Assert.Equal((WeedState.None, (byte)GroundType.Grass), (L.Weeds[i], L.Ground[i])));
    }

    [Fact]
    public void WeedsAmongACropCostItYield()
    {
        var content = TestContent.Content;
        var (wheat, corn) = (content.CropById("wheat")!, content.CropById("corn")!);
        Assert.Equal((1f, 0.9f, 0.8f, 1f), (CropSystem.WeedFactor(wheat, WeedState.None), CropSystem.WeedFactor(wheat, WeedState.Small),
            CropSystem.WeedFactor(wheat, WeedState.Grown), CropSystem.WeedFactor(wheat, WeedState.Sprayed)));
        Assert.Equal(0.7f, CropSystem.WeedFactor(corn, WeedState.Grown), 4);

        // The combine threshes less where weeds grew up among the wheat.
        float Harvest(byte weeds)
        {
            var sim = TestContent.NewSim();
            var L = sim.World.Layers;
            for (var i = 0; i < L.Weeds.Length; i++)
                if (L.FieldId[i] == 2) L.Weeds[i] = weeds;
            var (combine, header) = (sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f), sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f));
            Assert.True(sim.Machines.Attach(combine, "header", header));
            combine.Get<Thresher>()!.On = true;
            header.Get<Attachable>()!.Lowered = true;
            Drive(combine, 1f);
            Run(sim, 20f);
            return combine.Unit("tank")!.Level / Count(sim, 2, i => L.Ground[i] == (byte)GroundType.Stubble);
        }
        Assert.Equal(0.8f, Harvest(WeedState.Grown) / Harvest(WeedState.Sprayed), 2);
    }

    [Fact]
    public void AMowerCutsTheRipeGrassWhichGrowsBack()
    {
        var sim = TestContent.NewSim();
        var L = sim.World.Layers;
        var grass = (byte)(sim.Content.CropIndex("grass") + 1);
        // Field 7, the farm's meadow, is ready to mow.
        Assert.All(Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 7),
            i => Assert.Equal((grass, (byte)3, (byte)GroundType.Grass), (L.Crop[i], L.Stage[i], L.Ground[i])));
        var (t, mower) = Hitched(sim, "tractor_125", "mower_3", "rear", new Vector2(167f, 268f));
        mower.Get<Attachable>()!.Lowered = true;
        mower.Get<WorkAreas>()!.On = true;
        foreach (var i in Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 7)) L.Fertilized[i] = 1;
        Drive(t, 1f);
        Run(sim, 10f);

        var mown = Enumerable.Range(0, L.FieldId.Length).Where(i => L.FieldId[i] == 7 && L.Stage[i] == 1).ToList();
        Assert.True(mown.Count > 6 * 40, $"mown cells: {mown.Count}");
        Assert.All(mown, i => Assert.Equal((grass, (byte)GroundType.Grass, (byte)0), (L.Crop[i], L.Ground[i], L.Fertilized[i])));
        // It cuts only ripe crops it's made for: not wheat.
        var wheat = Enumerable.Range(0, L.FieldId.Length).First(i => L.FieldId[i] == 2);
        var cutsGrass = mower.Get<WorkAreas>()!.Def.Areas[0];
        Assert.False(WorkTypes.Mower.WouldChange(sim.World, sim.Content, cutsGrass, wheat));
        Assert.False(WorkTypes.Mower.WouldChange(sim.World, sim.Content, cutsGrass, mown[0]));

        // In summer weather it's ready again in a few game days.
        var cell = mown[0];
        L.Moisture[cell] = WorldGen.ToByte(0.6f);
        var hours = 0;
        for (; hours < 24 * 20 && L.Stage[cell] != 3; hours++)
        {
            sim.Weather.ForceSnapshot(18f, 0f);
            sim.Crops.TickHour(sim.Weather, sim.Clock.DayIndex, hours);
            L.Moisture[cell] = WorldGen.ToByte(0.6f);
        }
        Assert.InRange(hours / 24f, 2f, 5f);
    }

    [Fact]
    public void AHelperTurnsASpreaderOnOnlyAlongItsLanes()
    {
        var sim = TestContent.NewSim();
        var plot = FieldInfo.Rect(1, 50f, 80f, 60f, 60f);
        var t = sim.Machines.Spawn("tractor_95", plot.Shape.Min + new Vector2(2f, -12f), 0f);
        var spreader = sim.Machines.Spawn("spreader_24", plot.Shape.Min + new Vector2(2f, -15f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", spreader));
        var helper = sim.HireHelper(t, plot);
        var areas = spreader.Get<WorkAreas>()!;
        Assert.False(areas.On);

        bool onInTurns = false, onOnLanes = false;
        for (var s = 0f; s < 600f && !helper.Finished; s += Dt)
        {
            sim.Tick(Dt);
            if (helper.Finished) break;
            var onLane = helper.Path.Segments[helper.Driver.Index] == PathSegment.Work;
            if (areas.On && !onLane && !plot.Contains(spreader.LocalToWorld(areas.Bounds.center))) onInTurns = true;
            if (areas.On && onLane) onOnLanes = true;
        }
        Assert.True(helper.Finished && !helper.Stopped, helper.StopReason?.Text);
        Assert.True(onOnLanes);
        Assert.False(onInTurns, "on while turning outside the field");
        Assert.False(areas.On);

        var L = sim.World.Layers;
        var inside = new List<int>();
        plot.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ, (cx, cz) => inside.Add(sim.World.CellIndex(cx, cz)));
        Assert.True(inside.Count(i => L.Fertilized[i] > 0) > 0.97f * inside.Count);
        Assert.Equal(inside.Count(i => L.Fertilized[i] > 0), Enumerable.Range(0, L.Fertilized.Length).Count(i => L.Fertilized[i] > 0));
    }
}
