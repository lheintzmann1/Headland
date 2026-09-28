using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Work;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class GeometryTests
{
    [Fact]
    public void AxisAlignedRectCoversExpectedCells()
    {
        var cells = new List<int>();
        Span<Vector2> pts = stackalloc Vector2[4];
        MathUtil.RectCorners(new Vector2(10f, 10f), 0f, 1.5f, 0.5f, pts);
        Geometry.RasterizeConvex(pts, 0.5f, 100, 100, cells);
        Assert.Equal(6 * 2, cells.Count);
    }

    [Theory]
    [InlineData(0f, 0.3f)]
    [InlineData(45f, 0.9f)]
    [InlineData(-30f, 1.4f)]
    public void SweptWorkAreaLeavesNoGaps(float headingDeg, float stepMeters)
    {
        // A 3 m x 0.4 m work area moving in steps longer than itself must still cover the whole band.
        const float width = 3f, length = 0.4f, cell = 0.5f;
        var heading = headingDeg * MathUtil.Deg2Rad;
        var fwd = MathUtil.Forward(heading);
        var start = new Vector2(20f, 20f);
        var marked = new HashSet<int>();
        var cells = new List<int>();
        Span<Vector2> pts = stackalloc Vector2[8];
        var prev = start;
        for (var s = 1; s <= 20; s++)
        {
            var cur = start + fwd * (s * stepMeters);
            MathUtil.RectCorners(prev, heading, width / 2, length / 2, pts[..4]);
            MathUtil.RectCorners(cur, heading, width / 2, length / 2, pts[4..]);
            cells.Clear();
            Geometry.RasterizeConvex(pts, cell, 200, 200, cells);
            marked.UnionWith(cells);
            prev = cur;
        }

        var end = start + fwd * (20 * stepMeters);
        var left = MathUtil.Left(heading);
        var missing = 0;
        for (var cz = 0; cz < 200; cz++)
        for (var cx = 0; cx < 200; cx++)
        {
            var c = new Vector2((cx + 0.5f) * cell, (cz + 0.5f) * cell);
            var along = Vector2.Dot(c - start, fwd);
            var across = MathF.Abs(Vector2.Dot(c - start, left));
            var inside = along > 0.1f && along < Vector2.Distance(start, end) - 0.1f && across < width / 2 - 0.05f;
            if (inside && !marked.Contains(cz * 200 + cx)) missing++;
        }
        Assert.Equal(0, missing);
    }

    [Fact]
    public void ObbOverlapUsesOrientation()
    {
        var a = new Obb(Vector2.Zero, new Vector2(1f, 3f), 0f);
        var b = new Obb(new Vector2(2.5f, 0f), new Vector2(1f, 3f), 0f);
        Assert.False(Geometry.Overlaps(a, b));
        var rotated = b with { Heading = MathF.PI / 2f };
        Assert.True(Geometry.Overlaps(a, rotated));
    }
}

public class MachineTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static ManualController Drive(Machine m, float throttle, float steer = 0f)
    {
        var c = new ManualController { Input = new VehicleInput { Throttle = throttle, Steer = steer } };
        m.Get<Drivable>()!.Controller = c;
        return c;
    }

    private static int CountCells(Simulation sim, Func<int, bool> predicate)
    {
        var n = 0;
        for (var i = 0; i < sim.World.Layers.Ground.Length; i++)
            if (predicate(i)) n++;
        return n;
    }

    [Fact]
    public void TractorDrivesForwardAlongItsHeading()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        Drive(t, 1f);
        Run(sim, 5f);
        Assert.True(t.Position.Y > 310f, $"z={t.Position.Y}");
        Assert.InRange(t.Position.X, 268.9f, 269.1f);
        Assert.InRange(t.Speed, 5f, 40f / 3.6f + 0.01f);
    }

    [Fact]
    public void TrailerFollowsAndStraightensBehindTractor()
    {
        var sim = TestContent.NewSim();
        sim.World.Obstacles.Clear();
        foreach (var m in sim.Machines.All.ToList()) m.Position += new Vector2(0f, 1000f); // park far away
        var t = sim.Machines.Spawn("tractor_95", new Vector2(256f, 200f), 0f);
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(256f, 194f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));

        var c = Drive(t, 0.3f, 0.6f);
        Run(sim, 15f);
        var turning = MathF.Abs(MathUtil.WrapAngle(trailer.Heading - t.Heading));
        Assert.True(turning > 5f * MathUtil.Deg2Rad, "trailer should articulate while turning");

        c.Input = new VehicleInput { Throttle = 0.3f };
        Run(sim, 30f);
        var articulation = MathF.Abs(MathUtil.WrapAngle(trailer.Heading - t.Heading));
        Assert.True(articulation < 2f * MathUtil.Deg2Rad, $"articulation {articulation * MathUtil.Rad2Deg}°");

        // The trailer's drawbar eye stays on the tractor's hitch.
        var hitch = t.LocalToWorld(0f, t.Joint("drawbar")!.Z);
        var eye = trailer.LocalToWorld(0f, trailer.Get<Attachable>()!.Def.Z);
        Assert.True(Vector2.Distance(hitch, eye) < 0.01f);
    }

    [Fact]
    public void MountedImplementIsRigid()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 290f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 288f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        Drive(t, 0.5f, 0.8f);
        Run(sim, 3f);
        Assert.Equal(t.Heading, c.Heading, 4);
        var joint = t.LocalToWorld(0f, t.Joint("rear")!.Z);
        Assert.True(Vector2.Distance(joint, c.Position) < 0.01f);
    }

    [Fact]
    public void AttachFindsImplementBehindTractor()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 300f - 1.2f - 0.5f), 0f);
        var found = sim.Machines.FindAttachable(t);
        Assert.NotNull(found);
        Assert.Same(c, found.Value.child);
        sim.Player.Enter(t);
        sim.Perform(InputActions.Attach);
        Assert.Same(t, c.Parent);
        sim.Perform(InputActions.Attach);
        Assert.Null(c.Parent);
    }

    [Fact]
    public void CultivatorTurnsGrassIntoSeedbed()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 278f), 0f);
        sim.Machines.Attach(t, "rear", c);
        c.Get<Attachable>()!.Lowered = true;
        Drive(t, 1f);
        Run(sim, 12f);
        var cultivated = CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4);
        // Roughly 3 m wide x 35+ m at up to 14 km/h.
        Assert.True(cultivated > 6 * 60, $"cultivated cells: {cultivated}");
        Assert.True(t.Speed <= 14f / 3.6f + 0.01f, "work speed limit applies");
        Assert.True(c.WorkedHa > 0.008f);
    }

    [Fact]
    public void SeederSowsCultivatedSoilAndUsesSeed()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(90f, 262f), 0f);
        var s = sim.Machines.Spawn("seeder_3", new Vector2(90f, 257f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", s));
        s.Get<WorkAreas>()!.Crop = sim.Content.CropIndex("canola");
        s.Get<Attachable>()!.Lowered = true;
        s.Get<WorkAreas>()!.On = true;
        var seedBefore = s.Unit("seed")!.Level;
        Drive(t, 1f);
        Run(sim, 12f);
        var canola = (byte)(sim.Content.CropIndex("canola") + 1);
        var sown = CountCells(sim, i => sim.World.Layers.Crop[i] == canola && sim.World.Layers.FieldId[i] == 3);
        Assert.True(sown > 6 * 50, $"sown cells: {sown}");
        Assert.True(s.Unit("seed")!.Level < seedBefore);
        Assert.Empty(s.Conditions); // August is inside canola's sowing window
    }

    [Fact]
    public void CombineHarvestsRipeWheatIntoItsTank()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        combine.Get<Thresher>()!.On = true;
        header.Get<Attachable>()!.Lowered = true;
        Drive(combine, 1f);
        Run(sim, 20f);
        var tank = combine.Unit("tank")!;
        Assert.Equal("wheat", tank.FillType);
        Assert.True(tank.Level > 150f, $"tank {tank.Level} L");
        var stubble = CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Stubble && sim.World.Layers.FieldId[i] == 2);
        Assert.True(stubble > 12 * 60);
    }

    [Fact]
    public void WrongHeaderLeavesCropStanding()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_corn_6", new Vector2(260f, 90f), 0f);
        sim.Machines.Attach(combine, "header", header);
        combine.Get<Thresher>()!.On = true;
        header.Get<Attachable>()!.Lowered = true;
        Drive(combine, 1f);
        Run(sim, 10f);
        Assert.True(combine.Unit("tank")!.IsEmpty);
        Assert.Equal(new WrongHeader(sim.Content.CropById("wheat")!), Assert.Single(header.Conditions));
    }

    [Fact]
    public void PipeUnloadsCombineIntoTrailer()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(269f, 300f), 0f);
        combine.Unit("tank")!.Add("wheat", 5000f);
        var outlet = combine.Get<Pipe>()!.Outlet;
        var trailerDef = sim.Content.Machines["trailer_16"];
        var trailer = sim.Machines.Spawn("trailer_16", outlet - MathUtil.Forward(0f) * trailerDef.Size.CenterZ, 0f);
        combine.Get<Pipe>()!.Out = true;
        Run(sim, 50f);
        Assert.True(combine.Unit("tank")!.IsEmpty);
        Assert.Equal(5000f, trailer.Unit("main")!.Level, 1);
    }

    [Fact]
    public void TippingAtTheElevatorEarnsMoney()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_95", new Vector2(441f, 230f), MathF.PI / 2f);
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(435f, 230f), MathF.PI / 2f);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", 10000f);
        var money = sim.Economy.Money;
        var expected = PoiTests.SaleIncome(sim, "wheat", 10000f);

        sim.Player.Enter(t);
        sim.Perform(InputActions.Unload);
        var tipper = trailer.Get<Tipper>()!;
        Assert.True(tipper.Tipping);
        for (var s = 0f; s < 60f && (tipper.Tipping || tipper.Anim > 0f); s += Dt) sim.Tick(Dt);
        Assert.True(trailer.Unit("main")!.IsEmpty);
        Assert.Equal(money + expected, sim.Economy.Money, 0);
        Assert.Contains(sim.Notifications.Items, n => n.Text.StartsWith("Sold 10,000 L Wheat"));
    }

    [Fact]
    public void PlayerEntersAndExitsVehicle()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        sim.Player.Position = t.LocalToWorld(2.2f, 1f);
        sim.Perform(InputActions.Enter);
        Assert.Same(t, sim.Player.Vehicle);
        Assert.Same(sim.Player.Controls, t.Get<Drivable>()!.Controller);
        sim.Perform(InputActions.Enter);
        Assert.Null(sim.Player.Vehicle);
        Assert.Null(t.Get<Drivable>()!.Controller);
        Assert.True(t.Footprint.Distance(sim.Player.Position) > PlayerCharacter.Radius);
    }

    // A plot inside field 4 (grass), so headland turns stay on open grass (which the cultivator would happily work).
    private static readonly FieldInfo Plot = FieldInfo.Rect(4, 240, 290, 24, 40);

    private static (Simulation sim, Machine tractor, FieldWorkController helper) HireCultivatorHelper()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        var t = sim.Machines.Spawn("tractor_125", Plot.Shape.Min + new Vector2(2f, -10f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", Plot.Shape.Min + new Vector2(2f, -12f), 0f);
        sim.Machines.Attach(t, "rear", c);
        var helper = new FieldWorkController(sim, t, Plot);
        t.Get<Drivable>()!.Controller = helper;
        return (sim, t, helper);
    }

    [Fact]
    public void AnEngineBurnsFuelForItsLoadWhileSomeoneDrives()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        var parked = sim.Machines.Spawn("tractor_95", new Vector2(250f, 280f), 0f);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 278f), 0f);
        sim.Machines.Attach(t, "rear", c);
        var (motor, tank) = (t.Get<Motor>()!, t.Get<Motor>()!.FuelTank!);

        // Idling it burns 8% of full power's 23.75 L/h: 1.9 L/h, 0.032 L a minute.
        Drive(t, 0f);
        Run(sim, 60f);
        Assert.Equal(125f * 0.19f * 0.08f, motor.FuelPerHour, 3);
        Assert.InRange(250f - tank.Level, 0.025f, 0.035f);

        // Cultivating takes most of its power.
        c.Get<Attachable>()!.Lowered = true;
        Drive(t, 1f);
        Run(sim, 10f);
        Assert.True(motor.Load > 0.8f, $"load {motor.Load}");
        Assert.True(motor.FuelPerHour > 19f, $"{motor.FuelPerHour} L/h");
        // Nobody in it, the engine is off.
        Assert.Equal((0f, 180f), (parked.Get<Motor>()!.FuelPerHour, parked.Get<Motor>()!.FuelTank!.Level));
    }

    [Fact]
    public void AnEngineStopsWhenItsTankRunsDry()
    {
        var (sim, t, helper) = HireCultivatorHelper();
        var tank = t.Get<Motor>()!.FuelTank!;
        tank.Level = 0.05f;
        HelperDismissed? dismissed = null;
        sim.Events.Subscribe<HelperDismissed>(e => dismissed = e);
        for (var s = 0f; s < 20f && !t.Get<Motor>()!.OutOfFuel; s += Dt) sim.Tick(Dt);
        Assert.True(t.Get<Motor>()!.OutOfFuel);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Fieldmaster 125 ran out of fuel");
        Run(sim, 5f);
        Assert.Equal(0f, t.Speed);
        Assert.IsType<OutOfFuel>(dismissed?.Reason);

        // Refueled, it drives again.
        tank.Add("diesel", 50f);
        Drive(t, 1f);
        Run(sim, 2f);
        Assert.True(t.Speed > 1f);
    }

    [Fact]
    public void SwitchVehicleCyclesAndLeavesHelpersWorking()
    {
        var (sim, t, helper) = HireCultivatorHelper();
        var first = sim.Machines.All.First(m => m.Has<Drivable>());
        Assert.Same(t, sim.Machines.All.Last(m => m.Has<Drivable>()));

        sim.SwitchVehicle(1);
        Assert.Same(first, sim.Player.Vehicle);
        Assert.Same(sim.Player.Controls, first.Get<Drivable>()!.Controller);

        sim.SwitchVehicle(-1);
        Assert.Same(t, sim.Player.Vehicle);
        Assert.Same(helper, t.Get<Drivable>()!.Controller);
        Assert.Null(first.Get<Drivable>()!.Controller);

        sim.SwitchVehicle(1);
        Assert.Same(first, sim.Player.Vehicle);
        Assert.Same(helper, t.Get<Drivable>()!.Controller);
        var start = t.Position;
        Run(sim, 5f);
        Assert.True(Vector2.Distance(start, t.Position) > 5f, "the helper stopped driving");
    }

    [Fact]
    public void ACombineHelperLinesUpBeforeItsHeaderReachesTheCrop()
    {
        // A short field: every lane start left standing would show.
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["corn"] = new MapDef
        {
            Id = "corn", Name = "Corn", Size = 128, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands = [new FarmlandDef { Id = 1, Npc = "hendricks", Farm = 1, W = 128, H = 128 }],
            Fields = [new FieldDef { Id = 1, X = 40, Z = 40, W = 23, H = 30, Ground = "seeded", Crop = "corn", Stage = "harvestable" }],
        };
        db.Game.Map = "corn";
        var sim = Simulation.Create(db);
        var field = sim.World.FieldById(1)!;
        var combine = sim.Machines.Spawn("combine_7", field.Shape.Min + new Vector2(3f, -10f), 0f);
        sim.Machines.Attach(combine, "header", sim.Machines.Spawn("header_corn_6", field.Shape.Min + new Vector2(3f, -8f), 0f));
        var helper = sim.HireHelper(combine, field);
        for (var s = 0f; s < 600f && !helper.Finished; s += Dt) sim.Tick(Dt);
        Assert.True(helper.Finished && !helper.Stopped, helper.StopReason?.Text);
        var cells = CountCells(sim, i => sim.World.Layers.FieldId[i] == 1);
        var standing = CountCells(sim, i => sim.World.Layers.FieldId[i] == 1 && sim.World.Layers.Crop[i] != 0);
        Assert.True(standing < cells * 0.01f, $"{standing * 100f / cells:F1}% left standing");
    }

    [Fact]
    public void HelperCoversTheFieldAndDoesNotWorkOutsideIt()
    {
        var (sim, _, helper) = HireCultivatorHelper();
        for (var s = 0f; s < 900f && !helper.Finished; s += Dt) sim.Tick(Dt);
        Assert.True(helper.Finished, $"helper stuck at waypoint {helper.Driver.Index}/{helper.Path.Points.Count}");
        Assert.False(helper.Stopped, helper.StopReason?.Text);

        int inside = 0, insideDone = 0, outsideDone = 0;
        for (var cz = 0; cz < sim.World.CellsZ; cz++)
        for (var cx = 0; cx < sim.World.CellsX; cx++)
        {
            var p = sim.World.CellCenter(cx, cz);
            var cultivated = sim.World.Layers.Ground[sim.World.CellIndex(cx, cz)] == (byte)GroundType.Cultivated;
            var distance = Plot.Shape.Distance(p);
            if (distance == 0f)
            {
                inside++;
                if (cultivated) insideDone++;
            }
            else if (distance < 20f && cultivated) outsideDone++;
        }
        // Lanes start as soon as the field does: lowered ahead of it, the implement stays down.
        Assert.True(insideDone > inside * 0.99f, $"coverage {insideDone * 100f / inside:F1}%");
        Assert.True(outsideDone < inside * 0.02f, $"worked outside the field: {outsideDone * 100f / inside:F1}% of its area");
    }

    [Fact]
    public void HelperTurnsStayInATightHeadland()
    {
        var (_, t, helper) = HireCultivatorHelper();
        var turnRadius = t.Get<RunningGear>()!.Def.TurnRadius * 1.15f;
        var worst = helper.Path.Points.Max(Plot.Shape.Distance);
        // Past the edge by the implement's offset, plus a turning radius: a mounted implement backs up in its turns.
        Assert.Contains(PathSegment.Reverse, helper.Path.Segments);
        Assert.True(worst <= helper.Margin + turnRadius + 0.5f, $"path reaches {worst:F1} m outside the field");
        Assert.True(helper.Margin < 4.5f, $"margin {helper.Margin:F1} m");
    }

    [Fact]
    public void HelperLanesRunAlongTheLongSide()
    {
        var wide = FieldInfo.Rect(1, 0, 0, 60, 20);
        var path = FieldPlanner.Lanes(wide, new LanePlan(3f, 3.5f, 2f));
        var lane = path.Points[1] - path.Points[0];
        Assert.True(MathF.Abs(lane.X) > MathF.Abs(lane.Y), "lanes should run along x for a wide field");
        Assert.Equal(7, path.LaneCount);
    }

    [Fact]
    public void HelperLanesFollowOneAnother()
    {
        // 3 m lanes and a 3.5 m turning radius: still each lane next to the one before, like a farmer works.
        var path = FieldPlanner.Lanes(FieldInfo.Rect(1, 0, 0, 20, 60), new LanePlan(3f, 3.5f, 2f));
        var lanes = Enumerable.Range(0, path.Points.Count).Where(path.EndsLane).Select(k => path.Points[k].X).ToList();
        Assert.Equal(7, lanes.Count);
        Assert.All(lanes.Zip(lanes.Skip(1)), l => Assert.InRange(l.Second - l.First, 2.5f, 3f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CloseLanesAreJoinedByBackingUpOrByABulbTurn(bool reverse)
    {
        var field = FieldInfo.Rect(1, 0, 0, 20, 60);
        var path = FieldPlanner.Lanes(field, new LanePlan(3f, 3.5f, 2f) { Reverse = reverse });
        Assert.Equal(reverse ? path.LaneCount - 1 : 0, path.Segments.Count(s => s == PathSegment.Reverse));
        // Past the 2 m margin: one turning radius backing up, up to 1 + √3 of them looping out.
        var worst = path.Points.Max(field.Shape.Distance);
        if (reverse) Assert.InRange(worst, 2f + 3.5f - 0.01f, 2f + 3.5f + 0.01f);
        else Assert.InRange(worst, 2f + 2f * 3.5f, 2f + (1f + MathF.Sqrt(3f)) * 3.5f);
    }

    /// <summary>Share of the plot's cells for which <paramref name="done"/> holds.</summary>
    private static float PlotShare(Simulation sim, Func<int, bool> done)
    {
        int inside = 0, n = 0;
        Plot.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ, (cx, cz) =>
        {
            inside++;
            if (done(sim.World.CellIndex(cx, cz))) n++;
        });
        return (float)n / inside;
    }

    private static void RunHelper(Simulation sim, FieldWorkController helper)
    {
        for (var s = 0f; s < 900f && !helper.Finished; s += Dt) sim.Tick(Dt);
        Assert.True(helper.Finished, $"helper stuck at waypoint {helper.Driver.Index}/{helper.Path.Points.Count}");
        Assert.False(helper.Stopped, helper.StopReason?.Text);
    }

    [Fact]
    public void ATrailedSeederHelperLoopsInItsTurnsAndSowsTheWholePlot()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        Plot.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ,
            (cx, cz) => CultivatorWork.Till(sim.World, sim.World.CellIndex(cx, cz), 0));
        var t = sim.Machines.Spawn("tractor_125", Plot.Shape.Min + new Vector2(2f, -10f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", sim.Machines.Spawn("seeder_3", Plot.Shape.Min + new Vector2(2f, -14f), 0f)));
        var helper = sim.HireHelper(t, Plot);
        Assert.DoesNotContain(PathSegment.Reverse, helper.Path.Segments);
        RunHelper(sim, helper);
        Assert.True(PlotShare(sim, i => sim.World.Layers.Crop[i] != 0) > 0.99f);
    }

    [Fact]
    public void AHelperStopsWhenTheSeederRunsOutAndTheSeederSaysSoUntilRefilled()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        Plot.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ,
            (cx, cz) => CultivatorWork.Till(sim.World, sim.World.CellIndex(cx, cz), 0));
        var t = sim.Machines.Spawn("tractor_125", Plot.Shape.Min + new Vector2(2f, -10f), 0f);
        var seeder = sim.Machines.Spawn("seeder_3", Plot.Shape.Min + new Vector2(2f, -14f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", seeder));
        var seed = seeder.Unit("seed")!;
        seed.Remove(seed.Level - 0.05f); // some 100 m² of canola
        var dismissed = new List<HelperDismissed>();
        sim.Events.Subscribe<HelperDismissed>(dismissed.Add);
        sim.HireHelper(t, Plot);
        for (var s = 0f; s < 120f && dismissed.Count == 0; s += Dt) sim.Tick(Dt);

        var outOfSeed = new OutOf(sim.Content.FillTypes["seeds"]);
        Assert.Equal(HelperEnd.Stopped, Assert.Single(dismissed).End);
        Assert.Equal(outOfSeed, dismissed[0].Reason);
        Assert.True(seed.IsEmpty);
        Assert.Equal([outOfSeed], seeder.Conditions);
        Assert.Contains(sim.Notifications.Items, n => n.Text.StartsWith("Helper stopped on Field 4: Out of seeds: buy more at a shop"));

        seed.Add("seeds", 500f);
        Assert.Empty(seeder.Conditions);
    }

    [Fact]
    public void AFullTankStopsTheCombineUntilItIsUnloaded()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        var tank = combine.Unit("tank")!;
        tank.Add("wheat", tank.Capacity - 5f);
        combine.Get<Thresher>()!.On = true;
        header.Get<Attachable>()!.Lowered = true;
        Drive(combine, 1f);
        Run(sim, 10f);
        Assert.Contains(new TankFull(), combine.Conditions);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Grain tank full: unload into a trailer");

        tank.Remove(5000f);
        Assert.DoesNotContain(new TankFull(), combine.Conditions);
    }

    [Fact]
    public void AHelperHiredMidFieldGoesOnFromWhereTheVehicleIs()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        // Halfway up the plot, left of its middle, pointing up the lanes (not quite straight).
        var start = new Vector2(250.2f, 310f);
        var t = sim.Machines.Spawn("tractor_125", start, 0.05f);
        sim.Machines.Attach(t, "rear", sim.Machines.Spawn("cultivator_3", start - new Vector2(0f, 2f), 0.05f));
        var helper = sim.HireHelper(t, Plot);

        // The first lane runs on from the tractor to the far headland, then the helper works toward the nearer edge.
        Assert.Equal(PathSegment.Work, helper.Path.Segments[0]);
        Assert.Equal(start.X, helper.Path.Points[0].X, 2);
        Assert.True(helper.Path.Points[0].Y > Plot.Shape.Max.Y);
        var lanes = Enumerable.Range(0, helper.Path.Points.Count).Where(helper.Path.EndsLane).Select(k => helper.Path.Points[k].X).ToList();
        Assert.True(lanes[1] < lanes[0]);

        Run(sim, 4f);
        Assert.True(t.Position.Y > start.Y + 5f, "the helper should drive straight on");
        Assert.Equal(GroundType.Cultivated, sim.World.GroundAt(start + new Vector2(0f, 1f)));
        // What was left behind the tractor on its lane is done too.
        RunHelper(sim, helper);
        Assert.True(PlotShare(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated) > 0.99f);
    }

    [Fact]
    public void AHelperTakesOverAPartlyWorkedField()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        // The left half of the plot is cultivated already.
        Plot.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ, (cx, cz) =>
        {
            if (sim.World.CellCenter(cx, cz).X < Plot.Center.X) CultivatorWork.Till(sim.World, sim.World.CellIndex(cx, cz), 0);
        });
        var t = sim.Machines.Spawn("tractor_125", Plot.Shape.Min + new Vector2(2f, -10f), 0f);
        sim.Machines.Attach(t, "rear", sim.Machines.Spawn("cultivator_3", Plot.Shape.Min + new Vector2(2f, -12f), 0f));
        var helper = sim.HireHelper(t, Plot);

        // Nine lanes cover the plot: the helper does the five over its right half (one of them straddling the middle).
        Assert.Equal(5, helper.Path.LaneCount);
        RunHelper(sim, helper);
        Assert.True(PlotShare(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated) > 0.99f);
    }

    [Fact]
    public void NoHelperIsHiredForAFieldWithNothingLeftToDo()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        var field = sim.World.FieldById(4)!;
        field.Shape.Rasterize(WorldMap.CellSize, sim.World.CellsX, sim.World.CellsZ,
            (cx, cz) => CultivatorWork.Till(sim.World, sim.World.CellIndex(cx, cz), 0));
        var t = sim.Machines.Spawn("tractor_125", field.Center, 0f);
        sim.Machines.Attach(t, "rear", sim.Machines.Spawn("cultivator_3", field.Center - new Vector2(0f, 2f), 0f));
        sim.Player.Enter(t);
        sim.Perform(InputActions.Helper);
        Assert.Same(sim.Player.Controls, t.Get<Drivable>()!.Controller);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing left for the Tiller 300 to do on Field 4");
    }
}
