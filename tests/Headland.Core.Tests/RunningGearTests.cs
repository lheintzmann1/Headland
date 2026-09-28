using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class RunningGearTests
{
    private const float Dt = 1f / 60f;

    private const string Machines = """
        [
          {
            "id": "test_truck", "name": "Truck", "mass": 9000, "size": { "length": 8, "width": 2.5, "height": 3, "centerZ": 1.5 },
            "components": {
              "runningGear": { "maxSteerDeg": 35, "axles": [
                { "z": 4.0, "track": 2.0, "steering": "front" },
                { "z": 2.7, "track": 2.0, "steering": "front" },
                { "z": 0.65, "track": 1.9 },
                { "z": -0.65, "track": 1.9 } ] },
              "motor": { "powerHp": 300 },
              "drivable": {},
              "attacherJoints": { "joints": [
                { "id": "fifth", "type": "fifthWheel", "z": 0.3, "y": 1.2 },
                { "id": "drawbar", "type": "drawbar", "z": -2.7 } ] }
            }
          },
          {
            "id": "test_semi", "name": "Semi-trailer", "mass": 7000, "size": { "length": 13.6, "width": 2.55, "height": 3.5, "centerZ": 2.4 },
            "components": {
              "runningGear": { "axles": [ { "z": 0.65, "track": 2 }, { "z": -0.65, "track": 2 }, { "z": -1.95, "track": 2, "steering": "self" } ] },
              "attachable": { "type": "fifthWheel", "mode": "trailed", "z": 8, "maxArticulationDeg": 90, "hitchLoad": 0.35 },
              "fillUnits": { "units": [ { "id": "main", "capacity": 30000, "fillTypes": ["wheat"] } ] }
            }
          },
          {
            "id": "test_dolly", "name": "Dolly", "mass": 1500, "size": { "length": 3.2, "width": 2.5, "height": 1.2, "centerZ": 0.6 },
            "components": {
              "runningGear": { "axles": [ { "z": 0, "track": 2 } ] },
              "attachable": { "type": "drawbar", "mode": "trailed", "z": 2.8 },
              "attacherJoints": { "joints": [ { "id": "fifth", "type": "fifthWheel", "z": 0, "y": 1.1 } ] }
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
            "id": "test_singles", "name": "Singles", "mass": 5600, "size": { "length": 4.7, "width": 2.45, "height": 3, "centerZ": 1.25 },
            "components": {
              "runningGear": { "maxSteerDeg": 40, "axles": [
                { "z": 0, "track": 1.84, "wheels": { "radius": 0.8, "width": 0.55 } },
                { "z": 2.65, "track": 1.76, "steering": "front", "wheels": { "radius": 0.55, "width": 0.42 } } ] },
              "motor": { "powerHp": 125 },
              "drivable": {},
              "attacherJoints": { "joints": [ { "id": "rear", "type": "threePoint", "z": -1.2 } ] }
            }
          },
          {
            "id": "test_duals", "name": "Duals", "mass": 5600, "size": { "length": 4.7, "width": 3.2, "height": 3, "centerZ": 1.25 },
            "components": {
              "runningGear": { "maxSteerDeg": 40, "axles": [
                { "z": 0, "track": 1.84, "wheels": { "type": "dual", "radius": 0.8, "width": 0.55 } },
                { "z": 2.65, "track": 1.76, "steering": "front", "wheels": { "radius": 0.55, "width": 0.42 } } ] },
              "motor": { "powerHp": 125 },
              "drivable": {},
              "attacherJoints": { "joints": [ { "id": "rear", "type": "threePoint", "z": -1.2 } ] }
            }
          },
          {
            "id": "test_tracked", "name": "Tracked", "mass": 5600, "size": { "length": 4.7, "width": 2.6, "height": 3, "centerZ": 1.25 },
            "components": {
              "runningGear": { "maxSteerDeg": 40, "axles": [
                { "z": 0, "track": 1.9, "wheels": { "type": "tracks", "radius": 0.5, "width": 0.6, "length": 1.8 } },
                { "z": 2.65, "track": 1.76, "steering": "front", "wheels": { "radius": 0.55, "width": 0.42 } } ] },
              "motor": { "powerHp": 125 },
              "drivable": {},
              "attacherJoints": { "joints": [ { "id": "rear", "type": "threePoint", "z": -1.2 } ] }
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
        sim.Perform(InputActions.Steering);
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
        sim.Perform(InputActions.Steering);
        sim.Perform(InputActions.Steering);
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
        sim.Perform(InputActions.Steering);
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
        // Locked, it holds the trailer like the others: backing up, the trailer turns about the middle of all three.
        Assert.Equal(0f, gear.Def.Pivot(SteeringMode.Normal, false));
        Assert.Equal(-0.65f, gear.Def.Pivot(SteeringMode.Normal, true), 4);
    }

    /// <summary>A flat world of grass, nothing on it, with this soil moisture and a dry surface.</summary>
    private static Simulation FlatSim(float moisture = 0.3f)
    {
        var db = TestContent.WithMachines(Machines);
        db.Maps["flat"] = new MapDef { Id = "flat", Name = "Flat", Size = 200, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f, PlayerX = 2, PlayerZ = 2 };
        db.Game.Map = "flat";
        var sim = Simulation.Create(db);
        var layers = sim.World.Layers;
        for (var i = 0; i < layers.Moisture.Length; i++) layers.Moisture[i] = WorldGen.ToByte(moisture);
        sim.Weather.GroundWetness = 0f;
        return sim;
    }

    /// <summary>Tilts the world up toward +z: <paramref name="grade"/> meters up per meter.</summary>
    private static void Ramp(Simulation sim, float grade)
    {
        var h = sim.World.Height;
        for (var z = 0; z <= h.Size; z++)
        for (var x = 0; x <= h.Size; x++)
            h.Heights[z * h.Stride + x] = grade * z;
    }

    [Fact]
    public void DualsAndTracksPressLessAndSlipLessOnWetGround()
    {
        (float pressure, float slip, float speed) Cultivate(string tractor, float moisture, float wetness)
        {
            var sim = FlatSim(moisture);
            sim.Weather.GroundWetness = wetness;
            var ground = sim.World.Layers.Ground;
            for (var i = 0; i < ground.Length; i++) ground[i] = (byte)GroundType.Cultivated;
            var t = sim.Machines.Spawn(tractor, new Vector2(100f, 40f), 0f);
            var c = sim.Machines.Spawn("cultivator_3", new Vector2(100f, 38.8f), 0f);
            Assert.True(sim.Machines.Attach(t, "rear", c));
            c.Get<Attachable>()!.Lowered = true;
            Drive(t, 1f);
            Run(sim, 8f);
            var gear = t.Get<RunningGear>()!;
            return (gear.Pressure, gear.Slip, t.Speed);
        }

        var singles = Cultivate("test_singles", 0.95f, 1f);
        var duals = Cultivate("test_duals", 0.95f, 1f);
        var tracks = Cultivate("test_tracked", 0.95f, 1f);
        // Tractor and cultivator on the tires' footprint: about 76 kPa on singles, 46 on duals, 27 with tracks.
        Assert.InRange(singles.pressure, 70f, 80f);
        Assert.True(singles.pressure > duals.pressure * 1.5f && duals.pressure > tracks.pressure * 1.5f, $"{singles} {duals} {tracks}");
        // They sink in less and grip better, so they slip less and go faster.
        Assert.True(singles.slip > duals.slip && duals.slip > tracks.slip, $"{singles} {duals} {tracks}");
        Assert.True(singles.speed < duals.speed && duals.speed < tracks.speed, $"{singles} {duals} {tracks}");
        // On dry ground the wheels hardly slip.
        var dry = Cultivate("test_singles", 0.3f, 0f);
        Assert.True(dry.slip < 0.06f && singles.slip > 3f * dry.slip, $"dry {dry}, wet {singles}");
    }

    [Fact]
    public void AHeavyLoadGoesSlowerUpASlopeAndBurnsMoreFuel()
    {
        // Full throttle, or held at 7.2 km/h (2 m/s).
        (float speed, float fuel, float slip) Haul(float grade, bool downhill, bool full = true)
        {
            var sim = FlatSim();
            Ramp(sim, grade);
            var (z, heading, back) = downhill ? (185f, MathF.PI, 1f) : (20f, 0f, -1f);
            var t = sim.Machines.Spawn("tractor_95", new Vector2(100f, z), heading);
            var trailer = sim.Machines.Spawn("trailer_16", new Vector2(100f, z + back * (1.35f + 4.4f)), heading);
            Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
            trailer.Unit("main")!.Add("wheat", 16000f);
            if (full) Drive(t, 1f);
            else t.Get<Drivable>()!.Controller = new WaypointController([t.Position + t.Forward * 150f], 7.2f);
            Run(sim, 12f);
            return (t.Speed, t.Get<Motor>()!.FuelPerHour, t.Get<RunningGear>()!.Slip);
        }

        var flat = Haul(0f, false);
        var up = Haul(0.05f, false);
        // 21 t up a 5% slope: the 95 hp engine can't keep flat ground's speed, and the wheels slip more.
        Assert.True(up.speed < 0.6f * flat.speed, $"up {up}, flat {flat}");
        Assert.True(up.slip > 2f * flat.slip, $"up {up}, flat {flat}");

        // At the same 2 m/s it burns more climbing, less going down.
        (flat, up, var down) = (Haul(0f, false, full: false), Haul(0.05f, false, full: false), Haul(0.05f, true, full: false));
        Assert.True(MathF.Abs(up.speed - flat.speed) < 0.05f && MathF.Abs(down.speed - flat.speed) < 0.05f, $"up {up}, flat {flat}, down {down}");
        Assert.True(up.fuel > 1.5f * flat.fuel && flat.fuel > down.fuel, $"up {up}, flat {flat}, down {down}");
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

    /// <summary>How far a trailed machine's eye or kingpin is from the joint it hangs on.</summary>
    private static float HitchGap(Machine child)
    {
        var joint = child.Parent!.Joint(child.ParentJoint!)!;
        var a = child.Get<Attachable>()!.Def;
        return Vector2.Distance(child.Parent.PartToWorld(joint.X, joint.Z).position, child.LocalToWorld(a.X, a.Z));
    }

    [Fact]
    public void ASemiTrailerRestsOnTheFifthWheel()
    {
        var sim = FlatSim();
        var truck = sim.Machines.Spawn("test_truck", new Vector2(100f, 60f), 0f);
        var semi = sim.Machines.Spawn("test_semi", new Vector2(100f, 60f + 0.3f - 8f), 0f);
        var gear = truck.Get<RunningGear>()!;
        Run(sim, 0.1f);
        var alone = gear.Pressure;

        // Backed under it, the fifth wheel takes the kingpin, and a third of the loaded semi-trailer's weight.
        Assert.Equal(("fifth", semi), (sim.Machines.FindAttachable(truck)?.joint.Id, sim.Machines.FindAttachable(truck)?.child));
        sim.Player.Enter(truck);
        sim.Perform(InputActions.Attach);
        semi.Unit("main")!.Add("wheat", 30000f);
        Run(sim, 0.1f);
        var loaded = 7000f + 30000f * 0.78f;
        Assert.Equal(alone * (9000f + 0.35f * loaded) / 9000f, gear.Pressure, 1);
        var semiGear = semi.Get<RunningGear>()!;
        Assert.Equal(0.65f * loaded * 9.81f / semiGear.Def.ContactArea / 1000f, semiGear.Pressure, 1);

        // Turning, the kingpin stays on the fifth wheel as the semi-trailer swings behind.
        Drive(truck, 0.3f, 0.6f);
        Run(sim, 10f);
        Assert.True(HitchGap(semi) < 0.01f);
        Assert.True(MathF.Abs(MathUtil.WrapAngle(semi.Heading - truck.Heading)) > 10f * MathUtil.Deg2Rad);
    }

    [Fact]
    public void ADollyCarriesASemiTrailerBehindADrawbar()
    {
        var sim = FlatSim();
        var truck = sim.Machines.Spawn("test_truck", new Vector2(100f, 150f), 0f);
        var dolly = sim.Machines.Spawn("test_dolly", new Vector2(100f, 150f - 2.7f - 2.8f), 0f);
        var semi = sim.Machines.Spawn("test_semi", new Vector2(100f, 150f - 5.5f - 8f), 0f);
        Assert.True(sim.Machines.Attach(truck, "drawbar", dolly));
        Assert.True(sim.Machines.Attach(dolly, "fifth", semi));

        Drive(truck, 0.3f, 0.5f);
        Run(sim, 12f);
        // Both joints hold, the dolly swings behind the truck and the semi-trailer behind the dolly.
        Assert.True(HitchGap(dolly) < 0.01f && HitchGap(semi) < 0.01f);
        Assert.True(MathF.Abs(MathUtil.WrapAngle(dolly.Heading - truck.Heading)) > 5f * MathUtil.Deg2Rad);
        Assert.True(MathF.Abs(MathUtil.WrapAngle(semi.Heading - dolly.Heading)) > 5f * MathUtil.Deg2Rad);
        // The dolly's wheels carry its own weight and the semi-trailer's share, the truck's none of it.
        var dollyGear = dolly.Get<RunningGear>()!;
        Assert.Equal((1500f + 0.35f * 7000f) * 9.81f / dollyGear.Def.ContactArea / 1000f, dollyGear.Pressure, 1);
        Assert.Equal(9000f * 9.81f / truck.Get<RunningGear>()!.Def.ContactArea / 1000f, truck.Get<RunningGear>()!.Pressure, 1);

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [
              { "id": "a", "components": { "attachable": { "mode": "trailed", "z": 3, "hitchLoad": 1 } } },
              { "id": "b", "components": { "attachable": { "mode": "mounted", "hitchLoad": 0.2 } } }
            ]
            """));
        Assert.Contains("machine 'a' attachable: a trailed one needs a runningGear", bad.Message);
        Assert.Contains("machine 'a' attachable: hitchLoad must be in [0, 1)", bad.Message);
        Assert.Contains("machine 'b' attachable: hitchLoad is for trailed machines: a mounted one is carried whole", bad.Message);
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
