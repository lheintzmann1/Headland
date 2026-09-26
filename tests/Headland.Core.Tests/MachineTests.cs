using System.Numerics;
using Headland.Core.Machines;
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
        m.Controller = c;
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
        var eye = trailer.LocalToWorld(0f, trailer.Def.Attacher!.Z);
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
        sim.Machines.ToggleAttach(t);
        Assert.Same(t, c.Parent);
        sim.Machines.ToggleAttach(t);
        Assert.Null(c.Parent);
    }

    [Fact]
    public void CultivatorTurnsGrassIntoSeedbed()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 278f), 0f);
        sim.Machines.Attach(t, "rear", c);
        c.Lowered = true;
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
        s.SelectedCrop = sim.Content.CropIndex("canola");
        s.Lowered = true;
        s.TurnedOn = true;
        var seedBefore = s.Unit("seed")!.Level;
        Drive(t, 1f);
        Run(sim, 12f);
        var canola = (byte)(sim.Content.CropIndex("canola") + 1);
        var sown = CountCells(sim, i => sim.World.Layers.Crop[i] == canola && sim.World.Layers.FieldId[i] == 3);
        Assert.True(sown > 6 * 50, $"sown cells: {sown}");
        Assert.True(s.Unit("seed")!.Level < seedBefore);
        Assert.Null(s.Status); // August is inside canola's sowing window
    }

    [Fact]
    public void CombineHarvestsRipeWheatIntoItsTank()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(260f, 88f), 0f);
        var header = sim.Machines.Spawn("header_grain_6", new Vector2(260f, 90f), 0f);
        Assert.True(sim.Machines.Attach(combine, "header", header));
        combine.TurnedOn = true;
        header.Lowered = true;
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
        combine.TurnedOn = true;
        header.Lowered = true;
        Drive(combine, 1f);
        Run(sim, 10f);
        Assert.True(combine.Unit("tank")!.IsEmpty);
        Assert.Equal("Wrong header for Wheat", header.Status);
    }

    [Fact]
    public void PipeUnloadsCombineIntoTrailer()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(269f, 300f), 0f);
        combine.Unit("tank")!.Add("wheat", 5000f);
        var pipe = combine.Def.Pipe!;
        var outlet = combine.LocalToWorld(pipe.X, pipe.Z);
        var trailerDef = sim.Content.Machines["trailer_16"];
        var trailer = sim.Machines.Spawn("trailer_16", outlet - MathUtil.Forward(0f) * trailerDef.Size.CenterZ, 0f);
        combine.PipeOut = true;
        Run(sim, 50f);
        Assert.True(combine.Unit("tank")!.IsEmpty);
        Assert.Equal(5000f, trailer.Unit("main")!.Level, 1);
    }

    [Fact]
    public void TippingAtSellPointEarnsMoney()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_95", new Vector2(441f, 230f), MathF.PI / 2f);
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(435f, 230f), MathF.PI / 2f);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", 10000f);
        var money = sim.Economy.Money;
        var expected = 10000f * sim.Economy.Price("wheat", sim.Clock.Month);

        sim.Player.Enter(t);
        sim.CommandUnload();
        Assert.True(trailer.Tipping);
        for (var s = 0f; s < 60f && (trailer.Tipping || trailer.TipAnim > 0f); s += Dt) sim.Tick(Dt);
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
        sim.ToggleEnterExit();
        Assert.Same(t, sim.Player.Vehicle);
        Assert.Same(sim.Player.Controls, t.Controller);
        sim.ToggleEnterExit();
        Assert.Null(sim.Player.Vehicle);
        Assert.Null(t.Controller);
        Assert.True(t.Footprint.Distance(sim.Player.Position) > PlayerCharacter.Radius);
    }

    // A plot inside field 4 (grass), so headland turns stay on open grass (which the cultivator would happily work).
    private static readonly FieldInfo Plot = new() { Id = 4, X = 240, Z = 290, W = 24, H = 40 };

    private static (Simulation sim, Machine tractor, FieldWorkController helper) HireCultivatorHelper()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(Plot.X + 2f, Plot.Z - 10f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(Plot.X + 2f, Plot.Z - 12f), 0f);
        sim.Machines.Attach(t, "rear", c);
        var helper = new FieldWorkController(t, Plot);
        t.Controller = helper;
        return (sim, t, helper);
    }

    [Fact]
    public void HelperCoversTheFieldAndDoesNotWorkOutsideIt()
    {
        var (sim, _, helper) = HireCultivatorHelper();
        for (var s = 0f; s < 900f && !helper.Finished; s += Dt) sim.Tick(Dt);
        Assert.True(helper.Finished, $"helper stuck at waypoint {helper.Driver.Index}/{helper.Path.Points.Count}");
        Assert.False(helper.Stopped, helper.StopReason);

        int inside = 0, insideDone = 0, outsideDone = 0;
        for (var cz = 0; cz < sim.World.CellsZ; cz++)
        for (var cx = 0; cx < sim.World.CellsX; cx++)
        {
            var p = sim.World.CellCenter(cx, cz);
            var cultivated = sim.World.Layers.Ground[sim.World.CellIndex(cx, cz)] == (byte)GroundType.Cultivated;
            var inField = p.X > Plot.X && p.X < Plot.X + Plot.W && p.Y > Plot.Z && p.Y < Plot.Z + Plot.H;
            var nearField = p.X > Plot.X - 20 && p.X < Plot.X + Plot.W + 20 && p.Y > Plot.Z - 20 && p.Y < Plot.Z + Plot.H + 20;
            if (inField)
            {
                inside++;
                if (cultivated) insideDone++;
            }
            else if (nearField && cultivated) outsideDone++;
        }
        Assert.True(insideDone > inside * 0.95f, $"coverage {insideDone * 100f / inside:F1}%");
        Assert.True(outsideDone < inside * 0.02f, $"worked outside the field: {outsideDone * 100f / inside:F1}% of its area");
    }

    [Fact]
    public void HelperTurnsStayInATightHeadland()
    {
        var (_, t, helper) = HireCultivatorHelper();
        var mot = t.Def.Motorized!;
        var turnRadius = mot.Wheelbase / MathF.Tan(mot.MaxSteerDeg * MathUtil.Deg2Rad) * 1.15f;
        var worst = helper.Path.Points.Max(p =>
        {
            var dx = MathF.Max(0f, MathF.Max(Plot.X - p.X, p.X - (Plot.X + Plot.W)));
            var dz = MathF.Max(0f, MathF.Max(Plot.Z - p.Y, p.Y - (Plot.Z + Plot.H)));
            return MathF.Sqrt(dx * dx + dz * dz);
        });
        // Past the edge by the implement's offset, plus at most a turning circle.
        Assert.True(worst <= helper.Margin + 2f * turnRadius + 0.5f, $"path reaches {worst:F1} m outside the field");
        Assert.True(helper.Margin < 4.5f, $"margin {helper.Margin:F1} m");
    }

    [Fact]
    public void HelperLanesRunAlongTheLongSide()
    {
        var wide = new FieldInfo { X = 0, Z = 0, W = 60, H = 20 };
        var path = FieldPlanner.Lanes(wide, 3f, 3.5f, 2f);
        var lane = path.Points[1] - path.Points[0];
        Assert.True(MathF.Abs(lane.X) > MathF.Abs(lane.Y), "lanes should run along x for a wide field");
        Assert.Equal(7, path.LaneCount);
    }
}
