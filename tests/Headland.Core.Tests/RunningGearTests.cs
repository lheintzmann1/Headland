using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;

namespace Headland.Core.Tests;

public class RunningGearTests
{
    private const float Dt = 1f / 60f;

    private const string Machines = """
        [
          {
            "id": "test_truck", "name": "Truck", "size": { "length": 8, "width": 2.5, "height": 3, "centerZ": 1.5 },
            "components": {
              "runningGear": { "maxSteerDeg": 35, "axles": [
                { "z": 4.0, "track": 2.0, "steering": "front" },
                { "z": 2.7, "track": 2.0, "steering": "front" },
                { "z": 0.65, "track": 1.9 },
                { "z": -0.65, "track": 1.9 } ] },
              "motor": { "powerHp": 300 },
              "drivable": {}
            }
          },
          {
            "id": "test_sprayer", "name": "Sprayer", "size": { "length": 6, "width": 2.6, "height": 3.5, "centerZ": 1.6 },
            "components": {
              "runningGear": { "maxSteerDeg": 30, "axles": [
                { "z": 3.2, "track": 2.2, "steering": "front", "wheels": { "radius": 0.8, "width": 0.3 } },
                { "z": 0, "track": 2.2, "steering": "allWheel", "wheels": { "radius": 0.8, "width": 0.3 } } ] },
              "motor": { "powerHp": 250 },
              "drivable": {}
            }
          },
          {
            "id": "test_halftrack", "name": "Half-track", "size": { "length": 5.5, "width": 2.4, "height": 2.8, "centerZ": 1.2 },
            "components": {
              "runningGear": { "axles": [
                { "z": 0, "track": 1.9, "wheels": { "type": "tracks", "radius": 0.4, "width": 0.45, "length": 1.8 } },
                { "z": 2.9, "track": 1.8, "steering": "front", "wheels": { "type": "rowCrop", "radius": 0.5, "width": 0.3 } } ] },
              "motor": { "powerHp": 150 },
              "drivable": {}
            }
          },
          {
            "id": "test_wheel_loader", "name": "Wheel Loader", "size": { "length": 6.5, "width": 2.5, "height": 3.2, "centerZ": 1.5 },
            "components": {
              "runningGear": { "maxSteerDeg": 40, "articulation": { "z": 1.5 }, "axles": [ { "z": 0, "track": 1.9 }, { "z": 3, "track": 1.9 } ] },
              "motor": { "powerHp": 180 },
              "drivable": {},
              "attacherJoints": { "joints": [ { "id": "front", "type": "threePoint", "z": 4.2 } ] }
            }
          },
          {
            "id": "test_crawler", "name": "Crawler", "size": { "length": 5, "width": 2.8, "height": 3.2, "centerZ": 0.3 },
            "components": {
              "runningGear": { "maxSteerDeg": 35, "axles": [ { "z": 0, "track": 2.2, "wheels": { "type": "tracks", "radius": 0.5, "width": 0.6, "length": 2.4 } } ] },
              "motor": { "powerHp": 300 },
              "drivable": {}
            }
          },
          {
            "id": "test_tridem", "name": "Tridem", "size": { "length": 9, "width": 2.55, "height": 3, "centerZ": 0.5 },
            "components": {
              "runningGear": { "axles": [ { "z": 0.65, "track": 2 }, { "z": -0.65, "track": 2 }, { "z": -1.95, "track": 2, "steering": "self" } ] },
              "attachable": { "type": "drawbar", "mode": "trailed", "z": 6 }
            }
          }
        ]
        """;

    private static readonly Lazy<ContentDatabase> Content = new(() => TestContent.WithMachines(Machines));

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static void Drive(Machine m, float throttle, float steer = 0f) =>
        m.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = throttle, Steer = steer } };

    /// <summary>The default map with nothing in the way: no obstacles, and its machines parked far off.</summary>
    private static Simulation OpenSim()
    {
        var sim = Simulation.Create(Content.Value);
        sim.World.Obstacles.Clear();
        foreach (var m in sim.Machines.All) m.Position += new Vector2(0f, 1000f);
        return sim;
    }

    [Fact]
    public void EverySteeredAxleTurnsAboutTheMiddleOfTheFixedOnes()
    {
        var sim = OpenSim();
        var truck = sim.Machines.Spawn("test_truck", new Vector2(256f, 200f), 0f);
        var gear = truck.Get<RunningGear>()!;
        Assert.Equal(4f, gear.Def.Wheelbase());

        // Standing, the wheels turn to full lock: the first axle at maxSteerDeg, the second less, the tandem not at all.
        Drive(truck, 0f, 1f);
        Run(sim, 1f);
        Assert.Equal(gear.Def.MaxSteer, gear.AxleAngle(0), 4);
        Assert.Equal(2.7f / 4f, MathF.Tan(gear.AxleAngle(1)) / MathF.Tan(gear.AxleAngle(0)), 3);
        Assert.Equal((0f, 0f), (gear.AxleAngle(2), gear.AxleAngle(3)));
        // The inner (left) wheel turns more than the outer one: both roll around the turning center, r to the left.
        var r = 4f / MathF.Tan(gear.Def.MaxSteer);
        Assert.Equal(4f / (r - 1f), MathF.Tan(gear.WheelAngle(0, 1f)), 3);
        Assert.Equal(4f / (r + 1f), MathF.Tan(gear.WheelAngle(0, -1f)), 3);

        // Driving, the middle of the tandem goes round that center.
        AssertCircles(sim, truck, () => 4f / MathF.Tan(gear.SteerAngle));
    }

    [Fact]
    public void ACombineSteersWithItsRearAxle()
    {
        var sim = OpenSim();
        var combine = sim.Machines.Spawn("combine_7", new Vector2(256f, 200f), 0f);
        var gear = combine.Get<RunningGear>()!;
        Drive(combine, 0.3f, 1f);
        Run(sim, 2f);
        Assert.True(MathUtil.WrapAngle(combine.Heading) > 0.1f, "it turns left");
        Assert.Equal(-gear.SteerAngle, gear.AxleAngle(1), 4); // its rear wheels point right
        Assert.Equal(0f, gear.AxleAngle(0));
        Assert.Equal(2.65f / MathF.Tan(40f * MathUtil.Deg2Rad), sim.Content.Machines["tractor_125"].Get<RunningGearDef>()!.TurnRadius, 4);
    }

    [Fact]
    public void AllWheelSteeringTurnsTighterAndCrabSteeringMovesSideways()
    {
        var sim = OpenSim();
        var sprayer = sim.Machines.Spawn("test_sprayer", new Vector2(256f, 200f), 0f);
        var gear = sprayer.Get<RunningGear>()!;
        Assert.Equal([SteeringMode.Normal, SteeringMode.AllWheel, SteeringMode.Crab], gear.Def.Modes);
        Assert.Equal((3.2f, 1.6f), (gear.Def.Wheelbase(SteeringMode.Normal), gear.Def.Wheelbase(SteeringMode.AllWheel)));

        sim.Player.Enter(sprayer);
        sim.CommandSteering();
        Assert.Equal(SteeringMode.AllWheel, gear.Mode);
        // The rear axle steers against the front one; the middle between them goes round the turning center.
        Drive(sprayer, 0.15f, 1f);
        Run(sim, 5f);
        Assert.Equal(-gear.AxleAngle(0), gear.AxleAngle(1), 4);
        var r = 1.6f / MathF.Tan(gear.SteerAngle);
        Vector2 Middle() => sprayer.LocalToWorld(0f, 1.6f);
        var center = Middle() + MathUtil.Left(sprayer.Heading) * r;
        Run(sim, 3f);
        Assert.InRange(Vector2.Distance(Middle(), center), r - 0.05f, r + 0.05f);

        // Crab: both axles turn the same way, and it moves sideways without turning.
        var crab = sim.Machines.Spawn("test_sprayer", new Vector2(230f, 150f), 0f);
        sim.Player.Exit(sim);
        sim.Player.Enter(crab);
        sim.CommandSteering();
        sim.CommandSteering();
        var crabGear = crab.Get<RunningGear>()!;
        Assert.Equal(SteeringMode.Crab, crabGear.Mode);
        Drive(crab, 0.3f, 0.5f);
        Run(sim, 4f);
        Assert.Equal(0f, crab.Heading);
        Assert.True(crab.Position.X > 232f, $"x={crab.Position.X}"); // left of a heading of 0 is +x
        Assert.Equal((crabGear.SteerAngle, crabGear.SteerAngle), (crabGear.AxleAngle(0), crabGear.AxleAngle(1)));

        // The mode is saved; the next press goes back to normal.
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(crab.Id)!;
        Assert.Equal(SteeringMode.Crab, loaded.Get<RunningGear>()!.Mode);
        sim.CommandSteering();
        Assert.Equal(SteeringMode.Normal, crabGear.Mode);
    }

    [Fact]
    public void ASelfSteeringAxleFollowsTheTurnAndLocksStraightBackingUp()
    {
        var sim = OpenSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(256f, 200f), 0f);
        var trailer = sim.Machines.Spawn("test_tridem", new Vector2(256f, 200f - 1.45f - 6f), 0f);
        Assert.True(sim.Machines.Attach(tractor, "drawbar", trailer));
        var gear = trailer.Get<RunningGear>()!;

        Drive(tractor, 0.3f, 0.7f);
        Run(sim, 8f);
        // Turning left, the rear axle trails: its wheels point to the right of the trailer.
        Assert.True(gear.AxleAngle(2) < -2f * MathUtil.Deg2Rad, $"self-steering axle at {gear.AxleAngle(2) * MathUtil.Rad2Deg}°");
        Assert.Equal((0f, 0f), (gear.AxleAngle(0), gear.AxleAngle(1)));

        Drive(tractor, -0.3f);
        Run(sim, 3f);
        Assert.Equal(0f, gear.AxleAngle(2));
    }

    /// <summary>Drives at a steady speed with full left lock, then checks the turning center goes round a circle of the radius given.</summary>
    private static void AssertCircles(Simulation sim, Machine m, Func<float> radius)
    {
        Drive(m, 0.15f, 1f);
        Run(sim, 5f);
        var r = radius();
        var center = m.Position + MathUtil.Left(m.Heading) * r;
        Run(sim, 3f);
        Assert.InRange(Vector2.Distance(m.Position, center), r - 0.05f, r + 0.05f);
    }

    [Fact]
    public void AnArticulatedMachineSteersBySwingingItsFrontFrame()
    {
        var sim = OpenSim();
        var loader = sim.Machines.Spawn("test_wheel_loader", new Vector2(256f, 200f), 0f);
        var gear = loader.Get<RunningGear>()!;
        Assert.Equal(SteeringKind.Articulated, gear.SteeringKind);
        var tool = sim.Machines.Spawn("cultivator_3", new Vector2(256f, 204.2f), 0f);
        Assert.True(sim.Machines.Attach(loader, "front", tool));

        // Standing, the front frame swings left about the hinge with what hangs on it; the rear frame stays put.
        Drive(loader, 0f, 1f);
        Run(sim, 1f);
        Assert.Equal(gear.Def.MaxSteer, gear.SteerAngle, 4);
        Assert.Equal((new Vector2(256f, 200f), 0f), (loader.Position, loader.Heading));
        var (sin, cos) = MathF.SinCos(gear.SteerAngle);
        var joint = new Vector2(256f + 2.7f * sin, 201.5f + 2.7f * cos);
        Assert.True(Vector2.Distance(joint, loader.PartToWorld(0f, 4.2f).position) < 1e-3f);
        Assert.True(Vector2.Distance(joint, tool.Position) < 1e-3f);
        Assert.Equal(gear.SteerAngle, tool.Heading, 4);

        // Driving, the rear axle goes round the circle the hinge's angle gives.
        AssertCircles(sim, loader, () => (1.5f * MathF.Cos(gear.SteerAngle) + 1.5f) / MathF.Sin(gear.SteerAngle));
        Assert.Equal((1.5f * MathF.Cos(gear.Def.MaxSteer) + 1.5f) / MathF.Sin(gear.Def.MaxSteer), gear.Def.TurnRadius, 4);

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [
              { "id": "a", "components": { "runningGear": { "articulation": { "z": 1 }, "axles": [ { "z": 0 }, { "z": 2, "steering": "front" } ] } } },
              { "id": "b", "components": { "runningGear": { "articulation": { "z": 3 }, "axles": [ { "z": 0 }, { "z": 2 } ] } } }
            ]
            """));
        Assert.Contains("machine 'a' runningGear: an articulated machine steers with its hinge: its axles are fixed", bad.Message);
        Assert.Contains("machine 'b' runningGear: the hinge needs axles on both sides of it", bad.Message);
    }

    [Fact]
    public void FullTracksSkidSteerAndTurnOnTheSpot()
    {
        var sim = OpenSim();
        var crawler = sim.Machines.Spawn("test_crawler", new Vector2(256f, 200f), 0f);
        var gear = crawler.Get<RunningGear>()!;
        Assert.Equal(SteeringKind.SkidSteer, gear.SteeringKind);

        // Standing, it turns on the spot, its tracks running against each other.
        Drive(crawler, 0f, 1f);
        Run(sim, 2f);
        Assert.True(Vector2.Distance(crawler.Position, new Vector2(256f, 200f)) < 1e-3f);
        Assert.InRange(crawler.Heading, 0.7f, 1.1f); // about 30°/s
        Assert.True(gear.SideDistance(1.1f) < -0.5f && gear.SideDistance(-1.1f) > 0.5f, "the left track runs back, the right one forward");

        // Braking holds it.
        var heading = crawler.Heading;
        crawler.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Steer = 1f, Brake = true } };
        Run(sim, 1f);
        Assert.Equal(heading, crawler.Heading);

        // Moving, it turns as a steered axle a track's length ahead would.
        AssertCircles(sim, crawler, () => 2.4f / MathF.Tan(gear.SteerAngle));
    }

    [Fact]
    public void AHalfTrackSteersItsWheelsAboutItsTracks()
    {
        var sim = OpenSim();
        var halfTrack = sim.Machines.Spawn("test_halftrack", new Vector2(256f, 200f), 0f);
        var gear = halfTrack.Get<RunningGear>()!;
        Assert.Equal(SteeringKind.Axles, gear.SteeringKind);
        // It doesn't turn on the spot.
        Drive(halfTrack, 0f, 1f);
        Run(sim, 1f);
        Assert.Equal(0f, halfTrack.Heading);
        AssertCircles(sim, halfTrack, () => 2.9f / MathF.Tan(gear.SteerAngle));
    }

    [Theory]
    [InlineData("test_wheel_loader")]
    [InlineData("test_crawler")]
    [InlineData("test_sprayer")]
    public void TheAutopilotDrivesEveryKindOfSteering(string machine)
    {
        var sim = OpenSim();
        var m = sim.Machines.Spawn(machine, new Vector2(256f, 200f), 0f);
        var path = new WaypointController([new Vector2(256f, 215f), new Vector2(266f, 228f), new Vector2(285f, 232f), new Vector2(300f, 232f)], 8f);
        m.Get<Drivable>()!.Controller = path;
        for (var t = 0f; t < 60f && !path.Finished; t += Dt) sim.Tick(Dt);
        Assert.True(path.Finished, $"{machine} stuck at {m.Position}");
        Assert.True(MathF.Abs(m.Position.Y - 232f) < 1f, $"{machine} ended {m.Position.Y - 232f:0.00} m off the path");
    }

    [Fact]
    public void WheelSetsGiveTheirRolesAndTracksDontSteer()
    {
        Assert.Equal(["track0L", "track0R", "wheel1L", "wheel1R"], Content.Value.Machines["test_halftrack"].Get<RunningGearDef>()!.Roles);

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [
              { "id": "a", "components": { "runningGear": { "axles": [ { "z": 0 }, { "z": 2, "steering": "front", "wheels": { "type": "tracks", "length": 1 } } ] } } },
              { "id": "b", "components": { "runningGear": { "axles": [ { "z": 0, "wheels": { "type": "tracks" } } ] } } },
              { "id": "c", "components": { "runningGear": { "axles": [ { "z": 0, "wheels": { "type": "triple" } } ] } } }
            ]
            """));
        Assert.Contains("machine 'a' runningGear: tracks don't steer: they go on fixed axles", bad.Message);
        Assert.Contains("machine 'b' runningGear: tracks need a length > 0", bad.Message);
        Assert.Contains("machine 'c' runningGear: unknown wheels type 'triple'", bad.Message);
    }

    [Fact]
    public void BadAxlesAreRefusedByName()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [
              { "id": "a", "components": { "runningGear": { "axles": [ { "z": 0 }, { "z": -2, "steering": "front" } ] } } },
              { "id": "b", "components": { "runningGear": { "axles": [ { "z": 1 }, { "z": 3, "steering": "front" } ] } } },
              { "id": "c", "components": { "runningGear": { "axles": [ { "z": 3, "steering": "front" }, { "z": 0, "steering": "allWheel" }, { "z": -1 } ] } } },
              { "id": "d", "components": { "runningGear": { "axles": [ { "z": 0, "steering": "pivot" } ] } } }
            ]
            """));
        Assert.Contains("machine 'a' runningGear: front axles must be ahead of where it turns about, rear ones behind it", bad.Message);
        Assert.Contains("machine 'b' runningGear: the origin must be where it turns about: z = 1, the middle of its fixed axles", bad.Message);
        Assert.Contains("machine 'c' runningGear: allWheel axles can't go with fixed ones, which crab steering would drag sideways", bad.Message);
        Assert.Contains("machine 'd' runningGear: unknown steering 'pivot'", bad.Message);

        // The wheel list from before axles is refused rather than read as no wheels.
        var old = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": { "runningGear": { "wheels": [ { "x": 1, "z": 0 } ] } } }]
            """));
        Assert.Contains("'wheels'", old.Message);
    }
}
