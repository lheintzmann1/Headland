using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Saves;

namespace Headland.Core.Tests;

/// <summary>The kinds that go on any entity: lights, animated parts, fill units and hotspots, on machines and POIs.</summary>
public class SharedComponentTests
{
    private const float Dt = 1f / 60f;

    private const string Shed = """
        [{
          "id": "test_shed", "name": "Test shed", "w": 20, "d": 16,
          "components": {
            "lights": { "lamps": [
              { "id": "yard", "type": "workFront", "switch": "dark", "y": 5, "z": 8 },
              { "id": "evening", "type": "workFront", "switch": "hours", "hours": [18, 6], "y": 5, "z": 8 },
              { "id": "porch", "type": "workFront", "switch": "trigger", "trigger": { "z": 12, "w": 6, "d": 6, "by": "farmer" }, "y": 3, "z": 8 },
              { "id": "bay", "type": "workFront", "switch": "trigger", "trigger": { "z": 12, "w": 6, "d": 6, "by": "machines" }, "y": 3, "z": 8 } ] },
            "animatedParts": { "parts": [
              { "id": "door", "offset": [0, 4, 0], "seconds": 2, "trigger": { "z": 12, "w": 6, "d": 6 } } ] },
            "fillUnits": { "units": [
              { "id": "wheat", "capacity": 1000, "fillTypes": ["wheat"] },
              { "id": "bin", "capacity": 500, "fillTypes": ["wheat", "barley"] } ] },
            "hotspots": { "spots": [ { "icon": "warehouse", "z": 5 }, { "icon": "sell", "name": "Shop" } ] }
          }
        }]
        """;

    private static Simulation ShedSim() =>
        PoiTests.SimWith(TestContent.WithPois(Shed), new PoiPlacementDef { Id = "shed", Type = "test_shed", X = 32, Z = 32, HeadingDeg = 90, Farm = 1 });

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    /// <summary>Skips to the next <paramref name="hour"/>:30 and ticks once, so the components see it.</summary>
    private static void At(Simulation sim, int hour)
    {
        var now = (int)MathF.Floor(sim.Clock.HourOfDay);
        sim.SkipHours((hour - now + 24) % 24 is var h and > 0 ? h : 24);
        sim.Tick(Dt);
    }

    private static int Lamp(Lights lights, string id) => Array.FindIndex(lights.Def.Lamps, l => l.Id == id);

    [Fact]
    public void TheSunRisesAndSets()
    {
        var sim = ShedSim();
        At(sim, 12);
        Assert.True(sim.Weather.SunElevation > 0.3f);
        Assert.True(sim.Weather.Daylight > 0.99f);
        Assert.False(sim.Weather.Night);
        At(sim, 0);
        Assert.True(sim.Weather.SunElevation < 0f);
        Assert.Equal(0f, sim.Weather.Daylight);
        Assert.True(sim.Weather.Night && sim.Weather.Dim);
    }

    [Fact]
    public void ABuildingsLampsComeOnInTheDarkOnATimerOrAsSomeoneComesBy()
    {
        var sim = ShedSim();
        var shed = sim.World.PoiById("shed")!;
        var lights = shed.Get<Lights>()!;
        At(sim, 12);
        Assert.Equal(sim.Weather.Dim, lights.Lit(Lamp(lights, "yard")));
        Assert.False(lights.Lit(Lamp(lights, "evening")));
        Assert.False(lights.Lit(Lamp(lights, "porch")));

        // The farmer walks up to the door: the porch lamp is for them, the bay lamp for machines.
        var porch = shed.LocalToWorld(0f, 12f);
        sim.Player.Position = porch;
        sim.Tick(Dt);
        Assert.True(lights.Lit(Lamp(lights, "porch")));
        Assert.False(lights.Lit(Lamp(lights, "bay")));
        var tractor = sim.Machines.Spawn("tractor_95", porch - Vector2.UnitY * sim.Content.Machines["tractor_95"].Size.CenterZ, 0f);
        sim.Player.Position = new Vector2(2f, 2f);
        sim.Tick(Dt);
        Assert.False(lights.Lit(Lamp(lights, "porch")));
        Assert.True(lights.Lit(Lamp(lights, "bay")));
        sim.Machines.Teleport(tractor, new Vector2(10f, 55f), 0f);

        At(sim, 23);
        Assert.True(lights.Lit(Lamp(lights, "yard")));
        Assert.True(lights.Lit(Lamp(lights, "evening")));
        Assert.False(lights.Lit(Lamp(lights, "bay")));
        At(sim, 7);
        Assert.False(lights.Lit(Lamp(lights, "evening")));
    }

    /// <summary>Which of the lamp types (by lamptypes.json id) have a lamp shining, but the cab lights (see their own test).</summary>
    private static string[] Shining(Lights lights) =>
        lights.Def.Lamps.Where((l, i) => lights.Lit(i) && l.Type != "cab").Select(l => l.Type).Distinct().Order().ToArray();

    [Fact]
    public void TheDriverSwitchesTheLightsStepByStepAndSignals()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 50f), 0f);
        var lights = tractor.Get<Lights>()!;
        sim.Player.Enter(tractor);
        string Label(string action) => sim.Offers().Of(action)!.Label;

        // Nothing comes on by itself at night any more: the light key steps through off, headlights, work lights too.
        At(sim, 23);
        Assert.Empty(Shining(lights));
        Assert.Equal("Headlights, tail lights on", Label(InputActions.Lights));
        sim.Perform(InputActions.Lights);
        sim.Tick(Dt);
        Assert.Equal(["head", "tail"], Shining(lights));
        Assert.Equal("Front work lights, rear work lights on", Label(InputActions.Lights));
        sim.Perform(InputActions.Lights);
        sim.Tick(Dt);
        Assert.Equal(["head", "tail", "workFront", "workRear"], Shining(lights));
        Assert.Equal("Lights off", Label(InputActions.Lights));
        sim.Perform(InputActions.Lights);
        sim.Tick(Dt);
        Assert.Empty(Shining(lights));

        // Beacons come with the workshop's option; the turn signals blink one side, the hazards both.
        Assert.Null(sim.Offers().Of(InputActions.Beacons));
        sim.Perform(InputActions.Beacons);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "No beacons: a workshop fits them");
        sim.Perform(InputActions.TurnLeft);
        sim.Tick(Dt);
        Assert.Equal(["turnLeft"], Shining(lights));
        Assert.Equal("Stop signalling", Label(InputActions.TurnLeft));
        sim.Perform(InputActions.Hazards);
        sim.Tick(Dt);
        Assert.Equal(["turnLeft", "turnRight"], Shining(lights));
        sim.Perform(InputActions.Hazards);
        sim.Tick(Dt);
        Assert.Empty(Shining(lights));

        var both = sim.Machines.Spawn("tractor_125", new Vector2(10f, 30f), 0f, configuration: new Dictionary<string, string> { ["beacons"] = "both" });
        sim.Player.Exit(sim);
        sim.Player.Enter(both);
        sim.Perform(InputActions.Beacons);
        sim.Tick(Dt);
        Assert.Equal(["beacon"], Shining(both.Get<Lights>()!));

        // Getting out switches everything off but the hazards.
        sim.Perform(InputActions.Lights);
        sim.Perform(InputActions.TurnRight);
        sim.Player.Exit(sim);
        sim.Tick(Dt);
        Assert.Empty(Shining(both.Get<Lights>()!));
        sim.Player.Enter(both);
        sim.Perform(InputActions.Hazards);
        sim.Player.Exit(sim);
        sim.Tick(Dt);
        Assert.Equal(["turnLeft", "turnRight"], Shining(both.Get<Lights>()!));
    }

    [Fact]
    public void BrakeAndReverseLightsFollowTheDriving()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 20f), 0f);
        var lights = tractor.Get<Lights>()!;
        sim.Player.Enter(tractor);
        sim.Player.Controls.Input = new VehicleInput { Throttle = 1f };
        Run(sim, 2f);
        Assert.Empty(Shining(lights));
        sim.Player.Controls.Input = new VehicleInput { Brake = true };
        sim.Tick(Dt);
        sim.Tick(Dt);
        Assert.Equal(["brake"], Shining(lights));
        Run(sim, 3f);
        // Standing still, braking lights nothing; backing up lights the reverse lights.
        Assert.Empty(Shining(lights));
        sim.Player.Controls.Input = new VehicleInput { Throttle = -1f };
        sim.Tick(Dt);
        sim.Tick(Dt);
        Assert.Equal(["reverse"], Shining(lights));
    }

    [Fact]
    public void TheLightsStepBackAndTheirTypesSwitchAloneOnTheirKeys()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 50f), 0f);
        var lights = tractor.Get<Lights>()!;
        sim.Player.Enter(tractor);
        string Label(string action) => sim.Offers().Of(action)!.Label;
        string[] After(string action)
        {
            sim.Perform(action);
            sim.Tick(Dt);
            return Shining(lights);
        }

        // Back from off, every step comes on; back again, the last step goes off (FS: "Toggle light (Reverse)").
        Assert.Equal("Headlights, tail lights, front work lights, rear work lights on", Label(InputActions.LightsBack));
        Assert.Equal(["head", "tail", "workFront", "workRear"], After(InputActions.LightsBack));
        Assert.Equal("Front work lights, rear work lights off", Label(InputActions.LightsBack));
        Assert.Equal(["head", "tail"], After(InputActions.LightsBack));

        // High beams aren't in the cycle: their own key, and the light key's next step leaves them out again.
        Assert.Equal("High beams on", Label(InputActions.HighBeam));
        Assert.Equal(["head", "highBeam", "tail"], After(InputActions.HighBeam));
        Assert.Equal(["head", "tail", "workFront", "workRear"], After(InputActions.Lights));

        // The rear work lights alone; the light key then turns everything off (FS).
        After(InputActions.Lights);
        Assert.Equal(["workRear"], After(InputActions.WorkLightsRear));
        Assert.Equal("Lights off", Label(InputActions.Lights));
        Assert.Empty(After(InputActions.Lights));
        Assert.Equal(["head", "tail"], After(InputActions.RoadLights));

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(tractor.Id)!.Get<Lights>()!;
        Assert.Equal(["head", "tail"], loaded.On.Order());
    }

    [Fact]
    public void AFrontImplementSwitchesToTheTopLights()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 50f), 0f);
        var lights = tractor.Get<Lights>()!;
        sim.Player.Enter(tractor);
        sim.Perform(InputActions.Lights);
        sim.Tick(Dt);
        // Which headlights shine: those in the hood, or those on the roof.
        string Heads() => string.Join(", ", lights.Def.Lamps.Where((l, i) => l.Type == "head" && lights.Lit(i)).Select(l => l.TopLight ? "roof" : "hood"));
        Assert.Equal("hood, hood", Heads());

        // A mower on the front linkage hides the headlights in the hood: those on the roof shine instead.
        var mower = sim.Machines.Spawn("mower_3", new Vector2(10f, 54f), 0f);
        Assert.True(sim.Machines.Attach(tractor, "front", mower));
        sim.Tick(Dt);
        Assert.Equal("roof, roof", Heads());
        sim.Machines.Detach(mower);
        sim.Tick(Dt);
        Assert.Equal("hood, hood", Heads());
    }

    [Fact]
    public void CabLightsShineWhileSomeoneDrivesDimmedByDay()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 50f), 0f);
        var lights = tractor.Get<Lights>()!;
        var cab = Array.FindIndex(lights.Def.Lamps, l => l.Type == "cab");
        At(sim, 23);
        Assert.False(lights.Lit(cab));
        sim.Player.Enter(tractor);
        sim.Tick(Dt);
        Assert.Equal((true, 1f), (lights.Lit(cab), lights.Level(cab)));
        At(sim, 12);
        Assert.False(lights.Lit(cab));
        At(sim, 17);
        Assert.Equal(0.75f, lights.Level(cab), 2);
        Assert.Equal((0f, 1f, 0.5f, 0f, 0.5f, 1f), (Lights.CabBrightness(13f), Lights.CabBrightness(7f), Lights.CabBrightness(9f),
            Lights.CabBrightness(10f), Lights.CabBrightness(17f), Lights.CabBrightness(19f)));
    }

    [Fact]
    public void BeaconsAlwaysActiveShineWhileSomeoneDrives()
    {
        var content = TestContent.WithMachines("""
            [{ "id": "test_escort", "name": "Escort", "size": { "length": 4, "width": 2 },
               "components": {
                 "runningGear": { "axles": [ { "z": 0, "track": 1.6 }, { "z": 2.5, "track": 1.6, "steering": "front" } ] },
                 "motor": {}, "drivable": {},
                 "lights": { "lamps": [ { "type": "beacon", "y": 2, "alwaysActive": true }, { "type": "beacon", "y": 2, "x": 0.5 } ] } },
               "visual": { "model": "res://escort.glb" } }]
            """);
        var sim = Simulation.Create(content);
        var car = sim.Machines.Spawn("test_escort", new Vector2(60f, 60f), 0f);
        var lights = car.Get<Lights>()!;
        sim.Tick(Dt);
        Assert.False(lights.Lit(0));
        sim.Player.Enter(car);
        sim.Tick(Dt);
        Assert.Equal((true, false), (lights.Lit(0), lights.Lit(1)));
        sim.Perform(InputActions.Beacons);
        sim.Tick(Dt);
        Assert.Equal((true, true), (lights.Lit(0), lights.Lit(1)));
        sim.Player.Exit(sim);
        sim.Tick(Dt);
        Assert.Equal((false, false), (lights.Lit(0), lights.Lit(1)));

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": { "lights": { "lamps": [ { "id": "a", "type": "head", "alwaysActive": true }, { "id": "b", "topLight": true, "bottomLight": true } ] } } }]
            """)).Message;
        Assert.Contains("lamp 'a': alwaysActive is for beacons", bad);
        Assert.Contains("lamp 'b': a top light or a bottom light, not both", bad);
    }

    [Fact]
    public void AHelperLightsEverythingAtNightAndImplementsFollowTheirVehicle()
    {
        var content = TestContent.WithMachines("""
            [{ "id": "test_lit_trailer", "name": "Lit trailer", "size": { "length": 6, "width": 2.4, "centerZ": 0 },
               "components": {
                 "runningGear": { "axles": [ { "z": 0, "track": 1.9 } ] },
                 "attachable": { "type": "drawbar", "mode": "trailed", "z": 4 },
                 "lights": { "lamps": [ { "type": "workRear", "z": -3, "yawDeg": 180 }, { "type": "turnRight", "x": -1, "z": -3 } ] } },
               "visual": { "model": "res://trailer.glb" } }]
            """);
        var sim = Simulation.Create(content);
        TestContent.OwnField4(sim);
        var field = sim.World.FieldById(4)!;
        var t = sim.Machines.Spawn("tractor_125", field.Shape.Min + new Vector2(4f, -10f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", field.Shape.Min + new Vector2(4f, -12f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        At(sim, 23);
        sim.HireHelper(t, field, maxLanes: 1);
        sim.Tick(Dt);
        Assert.Equal(["head", "tail", "workFront", "workRear"], Shining(t.Get<Lights>()!));

        var truck = sim.Machines.Spawn("tractor_95", new Vector2(60f, 60f), 0f);
        var trailer = sim.Machines.Spawn("test_lit_trailer", new Vector2(60f, 55f), 0f);
        Assert.True(sim.Machines.Attach(truck, "drawbar", trailer));
        sim.Player.Enter(truck);
        sim.Perform(InputActions.Lights);
        sim.Perform(InputActions.Lights);
        sim.Perform(InputActions.TurnRight);
        sim.Tick(Dt);
        Assert.Equal(["turnRight", "workRear"], Shining(trailer.Get<Lights>()!));
        sim.Machines.Detach(trailer);
        sim.Tick(Dt);
        Assert.Empty(Shining(trailer.Get<Lights>()!));
    }

    [Fact]
    public void ADoorOpensAsSomeoneComesByAndClosesAfterThem()
    {
        var sim = ShedSim();
        var shed = sim.World.PoiById("shed")!;
        var door = shed.Get<AnimatedParts>()!.Part("door")!;
        Assert.False(shed.Get<AnimatedParts>()!.Move("door", true));
        sim.Player.Position = shed.LocalToWorld(0f, 12f);
        Run(sim, 1f);
        Assert.InRange(door.Position, 0.4f, 0.6f);

        // Half open in the save, and on its way.
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var again = loaded.World.PoiById("shed")!.Get<AnimatedParts>()!.Part("door")!;
        Assert.Equal(door.Position, again.Position, 3);
        Assert.True(again.Target);

        Run(sim, 1.2f);
        Assert.Equal(1f, door.Position);
        sim.Player.Position = new Vector2(2f, 2f);
        Run(sim, 2.1f);
        Assert.Equal(0f, door.Position);
    }

    [Fact]
    public void APoiKeepsGoodsInItsFillUnitsByFillType()
    {
        var sim = ShedSim();
        var units = sim.World.PoiById("shed")!.Get<FillUnits>()!;
        Assert.True(units.Keeps("barley"));
        Assert.False(units.Keeps("corn"));
        Assert.Equal(1500f, units.Free("wheat"));
        Assert.Equal(1200f, units.Add("wheat", 1200f));
        Assert.Equal(1200f, units.Level("wheat"));
        // The bin holds wheat now: no room for barley.
        Assert.Equal(0f, units.Add("barley", 100f));
        Assert.Equal(300f, units.Free("wheat"));
        Assert.Equal(700f, units.Remove("wheat", 700f));
        Assert.Equal(500f, units.Level("wheat"));
        Assert.Equal(500f, units.Remove("wheat", 900f));
        Assert.Equal(100f, units.Add("barley", 100f));
        Assert.Equal("barley", units.Unit("bin")!.FillType);
    }

    [Fact]
    public void HotspotsPutAnEntitysIconsOnTheMap()
    {
        var sim = ShedSim();
        var shed = sim.World.PoiById("shed")!;
        var spots = shed.Get<Hotspots>()!;
        Assert.Equal("warehouse", spots.Icon);
        Assert.Equal([(shed.LocalToWorld(0f, 5f), "Test shed"), (shed.Position, "Shop")], spots.Spots.Select(s => (s.Position, s.Name)));
        Assert.Equal("warehouse", TestContent.Content.Pois["farm_silo"].Get<HotspotsDef>()!.Spots[0].Icon);
    }

    [Fact]
    public void SharedKindsAreCheckedAgainstWhatTheyAreOn()
    {
        var e = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "test_bad", "name": "Bad", "components": {
                 "lights": { "lamps": [
                   { "id": "a", "switch": "sometimes" },
                   { "id": "b", "switch": "hours" },
                   { "id": "c", "switch": "trigger" },
                   { "id": "d", "hours": [18, 6] },
                   { "id": "e", "switch": "trigger", "trigger": { "w": 0, "by": "cows" } } ] },
                 "animatedParts": { "parts": [
                   { "id": "wing", "key": "fold" },
                   { "id": "hatch" },
                   { "id": "gate", "trigger": { "w": 4 } } ] },
                 "hotspots": { "spots": [ { "x": 1 } ], "words": { "use": ["Look", "Look away"] } } } }]
            """)).Message;
        Assert.Contains("poi 'test_bad' lights: lamp 'a': unknown switch 'sometimes'", e);
        Assert.Contains("lamp 'b': the hours switch needs hours", e);
        Assert.Contains("lamp 'c': the trigger switch needs a trigger", e);
        Assert.Contains("lamp 'd': hours go with the hours switch", e);
        Assert.Contains("lamp 'e' trigger: needs w and d > 0", e);
        Assert.Contains("poi 'test_bad' animatedParts: part 'wing': only a machine's parts move on keys; give it a trigger", e);
        Assert.Contains("part 'hatch': only a machine's parts move on keys", e);
        Assert.DoesNotContain("part 'gate'", e);
        Assert.Contains("poi 'test_bad' hotspots: each spot needs an icon", e);
        Assert.Contains("poi 'test_bad' hotspots: words: it offers no key to name ('use')", e);

        var by = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "test_bad", "name": "Bad", "components": {
                 "lights": { "lamps": [ { "switch": "trigger", "trigger": { "by": "cows" } } ] } } }]
            """)).Message;
        Assert.Contains("a head lamp trigger: by must be anyone, farmer, machines", by);
    }
}
