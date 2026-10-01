using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;
using Headland.Core.Objects;
using Headland.Core.Objects.Components;
using Headland.Core.Ownership;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class BaleTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds, Func<bool>? until = null)
    {
        for (var t = 0f; t < seconds && until?.Invoke() != true; t += Dt) sim.Tick(Dt);
    }

    private static void Drive(Machine m, float throttle) =>
        m.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = throttle } };

    private static IEnumerable<int> Cells(Simulation sim, Func<int, bool> where) =>
        Enumerable.Range(0, sim.World.Layers.FieldId.Length).Where(where);

    private static float Lying(Simulation sim, string fillType) =>
        Cells(sim, i => Windrows.FillTypeAt(sim.Content, sim.World.Layers, i)?.Id == fillType).Sum(i => sim.World.Layers.Windrow[i]);

    /// <summary>A vehicle at <paramref name="at"/> heading south (+z), with <paramref name="implement"/> on its joint, behind it.</summary>
    private static (Machine vehicle, Machine implement) Hitched(Simulation sim, string vehicle, string implement, string joint, Vector2 at)
    {
        var v = sim.Machines.Spawn(vehicle, at, 0f);
        var i = sim.Machines.Spawn(implement, at - new Vector2(0f, 3f), 0f);
        Assert.True(sim.Machines.Attach(v, joint, i));
        return (v, i);
    }

    /// <summary>Lays <paramref name="perCell"/> of <paramref name="fillType"/> on every cell of a rectangle (x, z, w, h).</summary>
    private static float Lay(Simulation sim, string fillType, float x, float z, float w, float h, float perCell)
    {
        var fill = Windrows.FillOf(sim.Content, fillType);
        var total = 0f;
        foreach (var i in Cells(sim, i => sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX) is var p
                                         && p.X >= x && p.X < x + w && p.Y >= z && p.Y < z + h))
        {
            Windrows.Add(sim.World, i, fill, perCell);
            total += perCell;
        }
        return total;
    }

    [Fact]
    public void AMowerLeavesTheGrassInAWindrowInItsMiddle()
    {
        var sim = TestContent.NewSim();
        var L = sim.World.Layers;
        var (t, mower) = Hitched(sim, "tractor_125", "mower_3", "rear", new Vector2(167f, 268f));
        mower.Get<Attachable>()!.Lowered = true;
        mower.Get<WorkAreas>()!.On = true;
        Drive(t, 1f);
        Run(sim, 10f);

        var grass = sim.Content.CropById("grass")!;
        var mown = Cells(sim, i => L.FieldId[i] == 7 && L.Stage[i] == 1).ToList();
        Assert.True(mown.Count > 6 * 40, $"mown cells: {mown.Count}");
        // Every bit of grass cut lies in the windrow, 1.2 m wide in the middle of the 3 m mower.
        var cut = mown.Sum(i => MowerWork.WindrowOf(L, grass, grass.Windrow!, i));
        Assert.Equal(cut, Lying(sim, "grass"), cut * 0.01f);
        var windrow = Cells(sim, i => Windrows.Has(L, i)).ToList();
        Assert.All(windrow, i => Assert.InRange(sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX).X, 167f - 0.6f - 0.25f, 167f + 0.6f + 0.25f));
        Assert.InRange(windrow.Count / (float)mown.Count, 0.3f, 0.55f);
        var report = sim.InspectCell(sim.World.CellCenter(windrow[0] % sim.World.CellsX, windrow[0] / sim.World.CellsX));
        Assert.Equal("grass", report.WindrowFill?.Id);

        // Cultivating the meadow works it into the ground.
        foreach (var i in windrow) CultivatorWork.Till(sim.World, i, 0);
        Assert.Equal(0f, Lying(sim, "grass"));
    }

    [Fact]
    public void ACombineDropsTheStrawOfWheatInItsSwathAndNothingForCanola()
    {
        var sim = TestContent.NewSim();
        var L = sim.World.Layers;
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        combine.Get<Thresher>()!.On = true;
        header.Get<Attachable>()!.Lowered = true;
        var wheat = sim.Content.CropById("wheat")!;
        var straw = 0f;
        sim.Events.Subscribe<CropHarvested>(e => straw += e.Amount / wheat.YieldPerHa * wheat.Windrow!.PerHa);
        Drive(combine, 1f);
        Run(sim, 20f);

        Assert.True(straw > 100f, $"straw due: {straw}");
        // What fell behind it: all of it but the last few meters, still in the combine.
        var lying = Lying(sim, "straw");
        Assert.InRange(lying, straw * 0.6f, straw * 1.001f);
        var dropped = Cells(sim, i => Windrows.Has(L, i)).Select(i => sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX)).ToList();
        Assert.All(dropped, p => Assert.InRange(p.X, 260f - 0.8f - 0.25f, 260f + 0.8f + 0.25f));
        Assert.All(dropped, p => Assert.True(p.Y < combine.Position.Y - 5f, $"straw at {p} ahead of the swath"));

        // Canola leaves nothing to bale.
        var canola = (byte)(sim.Content.CropIndex("canola") + 1);
        foreach (var i in Cells(sim, i => L.FieldId[i] == 2 && L.Crop[i] != 0)) L.Crop[i] = canola;
        combine.Unit("tank")!.Remove(9000f);
        var before = Lying(sim, "straw");
        Run(sim, 5f);
        Assert.Equal(before, Lying(sim, "straw"));
        Assert.Equal("canola", combine.Unit("tank")!.FillType);
    }

    [Fact]
    public void ATedderSpreadsTheGrassAndMakesHay()
    {
        var sim = TestContent.NewSim();
        // A 1.2 m windrow along the tedder's way.
        var grass = Lay(sim, "grass", 166.4f, 275f, 1.2f, 20f, 2f);
        var (t, tedder) = Hitched(sim, "tractor_125", "tedder_6", "rear", new Vector2(167f, 268f));
        tedder.Get<Attachable>()!.Lowered = true;
        tedder.Get<WorkAreas>()!.On = true;
        Assert.True(WorkTypes.Tedder.WouldChange(sim.World, sim.Content, tedder.Get<WorkAreas>()!.Def.Areas[0],
            Cells(sim, i => Windrows.Has(sim.World.Layers, i)).First()));
        Drive(t, 1f);
        Run(sim, 12f, () => t.Position.Y > 300f);

        Assert.Equal(0f, Lying(sim, "grass"));
        // Hay, 0.6 of the grass, spread over the tedder's 6.4 m.
        Assert.Equal(grass * 0.6f, Lying(sim, "hay"), grass * 0.02f);
        var hay = Cells(sim, i => Windrows.Has(sim.World.Layers, i)).Select(i => sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX)).ToList();
        Assert.True(hay.Max(p => p.X) - hay.Min(p => p.X) > 5f, "the hay is spread across the tedder");
    }

    [Fact]
    public void ARakeGathersWhatLiesSpreadIntoAWindrow()
    {
        var sim = TestContent.NewSim();
        var hay = Lay(sim, "hay", 165f, 275f, 4f, 20f, 0.5f);
        var (t, rake) = Hitched(sim, "tractor_125", "rake_4", "rear", new Vector2(167f, 268f));
        rake.Get<Attachable>()!.Lowered = true;
        rake.Get<WorkAreas>()!.On = true;
        Drive(t, 1f);
        Run(sim, 12f, () => t.Position.Y > 300f);

        Assert.Equal(hay, Lying(sim, "hay"), hay * 0.001f);
        // All of it in the 1.3 m windrow in the rake's middle.
        var lying = Cells(sim, i => Windrows.Has(sim.World.Layers, i)).Select(i => sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX)).ToList();
        Assert.All(lying, p => Assert.InRange(p.X, 167f - 0.65f - 0.25f, 167f + 0.65f + 0.25f));
    }

    [Fact]
    public void ABalerPicksUpTheWindrowAndDropsABaleEachTimeItIsFull()
    {
        var sim = TestContent.NewSim();
        var grass = Lay(sim, "grass", 166.5f, 280f, 1f, 30f, 84f);
        Assert.Equal(10080f, grass, 1f);
        var (t, baler) = Hitched(sim, "tractor_125", "baler_125", "drawbar", new Vector2(167f, 270f));
        var picked = 0f;
        var made = new List<WorldObject>();
        sim.Events.Subscribe<WindrowPickedUp>(e => picked += e.Amount);
        sim.Events.Subscribe<BaleMade>(e => made.Add(e.Bale));
        baler.Get<WorkAreas>()!.On = true;
        Drive(t, 1f);
        Run(sim, 20f, () => t.Position.Y > 320f);

        Assert.Equal(0f, Lying(sim, "grass"));
        Assert.Equal(grass, picked, 1f);
        // Two full bales behind it on the way, the rest still in the chamber.
        Assert.Equal(2, made.Count);
        Assert.All(made, b => Assert.Equal(("grass", 4000f), (b.Content!.FillType, b.Content.Level)));
        Assert.All(made, b => Assert.Null(b.Holder));
        Assert.All(made, b => Assert.InRange(b.Position.Y, 280f, 320f));
        var chamber = baler.Get<Baler>()!.Chamber;
        Assert.Equal(grass - 8000f, chamber.Level, 1f);
        Assert.Equal(2, sim.Statistics.BalesMade);
        Assert.Equal(grass, sim.Statistics.Harvested["grass"], 1f);

        // At the end of the field, the unload key drops what's left as a smaller bale.
        t.Get<Drivable>()!.Controller = null;
        Assert.True(sim.Player.Enter(t));
        Assert.StartsWith("Drop bale", sim.Offers().Of(Input.InputActions.Unload)!.Label);
        sim.Perform(Input.InputActions.Unload);
        Assert.True(chamber.IsEmpty);
        Assert.Equal(3, sim.Objects.All.Count);
        Assert.Equal(grass - 8000f, made[2].Content!.Level, 1f);
    }

    [Fact]
    public void ABalerFullOfGrassLeavesStrawLyingAndSaysSo()
    {
        var sim = TestContent.NewSim();
        Lay(sim, "straw", 166.5f, 280f, 1f, 10f, 5f);
        var (t, baler) = Hitched(sim, "tractor_125", "baler_125", "drawbar", new Vector2(167f, 270f));
        baler.Get<Baler>()!.Chamber.Add("grass", 500f);
        baler.Get<WorkAreas>()!.On = true;
        Drive(t, 1f);
        Run(sim, 10f, () => t.Position.Y > 300f);

        Assert.Equal(200f, Lying(sim, "straw"), 0.5f);
        Assert.Contains(baler.Conditions, c => c is BalerHolds { FillType.Id: "grass", Stops: true });
    }

    [Fact]
    public void ABaleLoaderPicksUpTheBalesBesideItAndSetsThemDownBehind()
    {
        var sim = TestContent.NewSim();
        var (t, loader) = Hitched(sim, "tractor_125", "baleloader_8", "drawbar", new Vector2(167f, 270f));
        sim.Machines.Teleport(t, t.Position, 0f);
        // Bales in a row, as a baler leaves them, by the loader's front right corner as it drives south (its right is west).
        var bales = Enumerable.Range(0, 3).Select(k => sim.Objects.Spawn("round_bale", new Vector2(165f, 285f + k * 20f), 0f, Farm.PlayerId)).ToList();
        bales.ForEach(b => b.Content!.Add("hay", 3000f));
        var other = sim.Objects.Spawn("round_bale", new Vector2(165f, 279f), 0f, Farm.None);
        var bed = loader.Get<BaleLoader>()!;
        bed.On = true;
        Drive(t, 0.4f);
        Run(sim, 40f, () => bed.Count == 3);

        Assert.Equal(3, bed.Count);
        Assert.All(bales, b => Assert.Same(bed, b.Holder));
        Assert.Null(other.Holder);
        // They ride on its bed.
        Assert.All(bales, b => Assert.True(loader.Footprint.Contains(b.Position), $"bale at {b.Position}, loader at {loader.Position}"));
        Assert.All(bales, b => Assert.Equal(1f, b.Elevation, 0.01f));

        // Moving, it won't set them down; standing, all of them behind it.
        t.Get<Drivable>()!.Controller = null;
        Assert.True(sim.Player.Enter(t));
        Assert.Equal("Unload bales (3)", sim.Offers().Of(Input.InputActions.Unload)!.Label);
        bed.Unload(sim);
        Assert.Equal(3, bed.Count);
        sim.Player.Controls.Input = new VehicleInput { Brake = true };
        Run(sim, 3f);
        bed.Unload(sim);
        Assert.Equal(0, bed.Count);
        Assert.All(bales, b => Assert.Null(b.Holder));
        Assert.All(bales, b => Assert.True(MathUtil.WorldToLocal(loader.Position, loader.Heading, b.Position).Y < -3f));
        Assert.All(bales, b => Assert.Equal(0f, b.Elevation));
    }

    [Fact]
    public void TheDairyBuysTheBalesLeftInItsBaleArea()
    {
        var sim = TestContent.NewSim();
        var dairy = sim.World.PoiById("dairy")!;
        var sell = dairy.Get<SellingStation>()!;
        var area = dairy.Trigger("objects")!.Area;
        Assert.Null(dairy.Trigger("unload"));
        var hay = sim.Objects.Spawn("round_bale", area.Center, 0f, Farm.PlayerId);
        hay.Content!.Add("hay", 4000f);
        var straw = sim.Objects.Spawn("round_bale", area.Center + area.AxisX * 2f, 0f, Farm.PlayerId);
        straw.Content!.Add("straw", 3000f);
        var outside = sim.Objects.Spawn("round_bale", area.Center + area.AxisY * 20f, 0f, Farm.PlayerId);
        outside.Content!.Add("hay", 4000f);
        var sold = new List<ObjectsSold>();
        sim.Events.Subscribe<ObjectsSold>(sold.Add);
        var money = sim.Economy.Money;
        var expected = 4000f * sim.Pois.Price(sell, "hay") + 3000f * sim.Pois.Price(sell, "straw");
        Run(sim, 1f);

        Assert.Equal(expected, sim.Economy.Money - money, 1f);
        Assert.Equal([outside], sim.Objects.All);
        Assert.Equal(["hay", "straw"], sold.Select(s => s.FillType).Order());
        Assert.All(sold, s => Assert.Equal(1, s.Count));
        Assert.Equal(4000f, sim.Statistics.Sold["hay"]);
        Assert.True(sell.DemandOf("hay") < 1f);
        Assert.Contains(sim.Notifications.Items, n => n.Text.StartsWith("Sold a round bale of hay (4,000 L) to Dairy Farm"));
    }

    [Fact]
    public void ABaleJobIsMownBaledAndDoneOnceTheBalesAreAtTheBuyer()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Game.StartMonth = 6;
        foreach (var id in db.ContractTypes.Keys.Where(id => id != "bale").ToList()) db.ContractTypes.Remove(id);
        db.Maps["meadows"] = new MapDef
        {
            Id = "meadows", Name = "Meadows", Size = 256, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands =
            [
                new FarmlandDef { Id = 1, Npc = "hendricks", Farm = Farm.PlayerId, X = 0, Z = 0, W = 64, H = 128 },
                new FarmlandDef { Id = 2, Npc = "morrow", X = 64, Z = 0, W = 192, H = 128 },
            ],
            Fields =
            [
                new FieldDef { Id = 1, X = 8, Z = 8, W = 48, H = 48, Ground = "stubble" },
                new FieldDef { Id = 2, X = 72, Z = 8, W = 50, H = 50, Ground = "grass", Crop = "grass", Stage = "harvestable" },
                new FieldDef { Id = 3, X = 132, Z = 8, W = 50, H = 50, Ground = "seeded", Crop = "wheat", Stage = "harvestable" },
            ],
            Pois = [new PoiPlacementDef { Id = "dairy", Type = "dairy_farm", X = 128, Z = 200 }],
            PlayerX = 2, PlayerZ = 2,
        };
        db.Game.Map = "meadows";
        var sim = Simulation.Create(db);
        var meadow = sim.World.FieldById(2)!;
        // Offered on the meadow only: wheat's straw isn't the crop.
        var job = Assert.Single(sim.Contracts.Offers);
        Assert.Equal(("Bale grass on Field 2", "dairy", "grass"), (job.Label, job.Poi?.Id, job.Goods?.Id));
        Assert.True(sim.Contracts.Accept(job));

        // The job lets the field be mown, tedded and raked as well as baled; nothing else.
        var L = sim.World.Layers;
        var cells = Cells(sim, i => L.FieldId[i] == 2).ToList();
        var cell = cells[0];
        foreach (var work in new[] { "mower", "tedder", "windrower", "baler" }) Assert.True(sim.Farms.MayWork(Farm.PlayerId, cell, work, null), work);
        Assert.False(sim.Farms.MayWork(Farm.PlayerId, cell, "cultivator", null));

        // Mown: the grass lies in windrows, and the job isn't done until they're picked up.
        var grass = Windrows.FillOf(sim.Content, "grass");
        foreach (var i in cells)
        {
            MowerWork.Cut(sim.World, sim.Content.CropById("grass")!, i, 0);
            if (i % 3 == 0) Windrows.Add(sim.World, i, grass, 1f);
        }
        sim.SkipHours(1);
        Assert.InRange(job.Progress, 0.6f, 0.7f);

        // Baled: done on the field, and 90% of it is owed at the dairy.
        var baler = sim.Machines.Spawn("baler_125", new Vector2(10f, 70f), 0f);
        foreach (var i in cells) Windrows.Clear(sim.World, i);
        sim.Events.Publish(new WindrowPickedUp(baler, "grass", 8000f, 2));
        sim.SkipHours(1);
        Assert.Equal((1f, 8000f), (job.Progress, job.Harvested));
        Assert.Equal(ContractState.Active, job.State);

        var area = sim.World.PoiById("dairy")!.Trigger("objects")!.Area;
        var money = sim.Economy.Money;
        foreach (var k in new[] { -1f, 1f })
            sim.Objects.Spawn("round_bale", area.Center + area.AxisX * k * 2f, 0f, Farm.PlayerId).Content!.Add("grass", 4000f);
        Run(sim, 1f);
        Assert.Equal(ContractState.Completed, job.State);
        Assert.Equal(8000f, job.Delivered);
        // The bales were the neighbor's: only the reward is paid.
        Assert.Equal(money + job.Reward, sim.Economy.Money, 1f);
        Assert.Empty(sim.Objects.All);
    }

    [Fact]
    public void WindrowsAndBalesAreSaved()
    {
        var sim = TestContent.NewSim();
        Lay(sim, "hay", 166.5f, 280f, 1f, 4f, 3f);
        var loose = sim.Objects.Spawn("round_bale", new Vector2(160f, 290f), 0.5f, Farm.PlayerId);
        loose.Content!.Add("straw", 2500f);
        loose.Elevation = 1.25f;
        var (t, loader) = Hitched(sim, "tractor_125", "baleloader_8", "drawbar", new Vector2(167f, 230f));
        var held = sim.Objects.Spawn("round_bale", new Vector2(0f, 0f), 0f, Farm.PlayerId);
        held.Content!.Add("grass", 4000f);
        Assert.True(loader.Get<BaleLoader>()!.Restore(held, 3));
        loader.Get<BaleLoader>()!.On = true;
        var hayCells = Cells(sim, i => Windrows.Has(sim.World.Layers, i)).ToList();

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.All(hayCells, i => Assert.Equal((sim.World.Layers.Windrow[i], "hay"),
            (loaded.World.Layers.Windrow[i], Windrows.FillTypeAt(loaded.Content, loaded.World.Layers, i)?.Id)));
        Assert.Equal(2, loaded.Objects.All.Count);
        var o = loaded.Objects.ById(loose.Id)!;
        Assert.Equal((loose.Position, loose.Heading, 1.25f, "straw", 2500f), (o.Position, o.Heading, o.Elevation, o.Content!.FillType, o.Content.Level));
        var bed = loaded.Machines.ById(loader.Id)!.Get<BaleLoader>()!;
        Assert.True(bed.On);
        Assert.Same(loaded.Objects.ById(held.Id), bed.Slots[3]);
        Assert.Same(bed, bed.Slots[3]!.Holder);
        Assert.Equal("grass", bed.Slots[3]!.Content!.FillType);

        // A new object gets a new id.
        Assert.True(loaded.Objects.Spawn("round_bale", Vector2.Zero, 0f, Farm.PlayerId).Id > held.Id);
    }

    [Fact]
    public void ALoaderGoingAwayLeavesItsBalesOnTheGround()
    {
        var sim = TestContent.NewSim();
        var (_, loader) = Hitched(sim, "tractor_125", "baleloader_8", "drawbar", new Vector2(167f, 230f));
        var bale = sim.Objects.Spawn("round_bale", Vector2.Zero, 0f, Farm.PlayerId);
        Assert.True(loader.Get<BaleLoader>()!.Restore(bale, 0));
        sim.Tick(Dt);
        var at = bale.Position;
        sim.Garage.Sell(loader);
        Assert.Null(bale.Holder);
        Assert.Equal((at, 0f), (bale.Position, bale.Elevation));
    }

    [Fact]
    public void BalesBalersAndLoadersAreChecked()
    {
        var errors = Assert.Throws<ContentException>(() => TestContent.Modded(new()
        {
            ["objects/test.json"] = """
                [{ "id": "brick", "components": { "bale": { "shape": "oval" } } },
                 { "id": "flat", "size": { "length": 0 }, "components": { "fillUnits": { "units": [ { "id": "a", "fillTypes": ["hay"] } ] } } }]
                """,
            ["machines/test.json"] = """
                [{ "id": "x", "components": {
                     "fillUnits": { "units": [ { "id": "chamber", "capacity": 9000, "fillTypes": ["hay"] } ] },
                     "baler": { "bale": "round_bale" }, "baleLoader": { "grabSeconds": 0, "shapes": ["cube"] } } },
                 { "id": "y", "components": { "bale": {}, "baler": { "bale": "flat", "fillUnit": "none" } } }]
                """,
        })).Message;
        Assert.Contains("object 'brick' bale: shape must be round or square", errors);
        Assert.Contains("object 'brick' bale: a bale needs fillUnits with one unit: what it's made of", errors);
        Assert.Contains("object 'flat': size needs length, width and height > 0", errors);
        Assert.Contains("machine 'x' baler: a bale of 'round_bale' can't hold what the chamber does: room for 9,000, and its fill types", errors);
        Assert.Contains("machine 'x' baler: needs a work area of type baler: its pickup", errors);
        Assert.Contains("machine 'x' baleLoader: needs slots", errors);
        Assert.Contains("machine 'x' baleLoader: grabSeconds must be > 0", errors);
        Assert.Contains("machine 'x' baleLoader: unknown shape 'cube' (round, square)", errors);
        Assert.Contains("machine 'y' bale: goes on objects only", errors);
        Assert.Contains("machine 'y' baler: fill unit 'none' missing", errors);
        Assert.Contains("machine 'y' baler: 'flat' is not a bale", errors);

        var station = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "z", "components": { "sellingStation": { "fillTypes": ["hay"] } } }]
            """)).Message;
        Assert.Contains("poi 'z' sellingStation: needs a trigger, an objectTrigger or both", station);
        var crops = Assert.Throws<ContentException>(() => TestContent.Modded(new()
        {
            ["filltypes.json"] = TestContent.WithEntry("filltypes.json", """{ "id": "silage", "name": "Silage", "tedded": { "fillType": "dust" } }"""),
            ["crops/zz.json"] = File.ReadAllText(Path.Combine(TestContent.DataDir, "crops", "grass.json"))
                .Replace("\"grass\", \"name\"", "\"zz\", \"name\"").Replace("\"fillType\": \"grass\", \"perHa\": 20000", "\"fillType\": \"dust\", \"perHa\": 0"),
        })).Message;
        Assert.Contains("fill type 'silage': tedded needs another known fillType and a factor > 0", crops);
        Assert.Contains("crop 'zz': windrow needs a known fillType and perHa > 0", crops);
    }
}
