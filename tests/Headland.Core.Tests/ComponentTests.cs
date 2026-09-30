using System.Numerics;
using System.Text.Json;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class ComponentTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private const string Forwarder = """
        {
          "id": "test_forwarder", "name": "Forwarder", "size": { "length": 7, "width": 2.6, "height": 3.2, "centerZ": 1.5 },
          "components": {
            "runningGear": { "axles": [ { "z": 0, "track": 2.2 }, { "z": 3, "track": 2.2, "steering": "front" } ] },
            "motor": { "powerHp": 170 },
            "drivable": {},
            "craneArm": { "groups": ["crane", "stick"], "joints": [
              { "id": "slew", "axis": "yaw", "offset": [0, 2, 1], "min": -120, "max": 120, "speed": 30, "control": "x" },
              { "id": "boom", "axis": "pitch", "offset": [0, 0.5, 0], "min": -20, "max": 60, "speed": 20, "control": "y" },
              { "id": "stick", "axis": "extend", "offset": [0, 0, 4], "min": 0, "max": 2, "speed": 0.5, "control": "y", "group": 2 } ] },
            "winch": { "joint": "stick", "offset": [0, 0, 3], "maxLength": 6, "speed": 2 },
            "saw": { "joint": "stick", "offset": [0, -0.5, 3] },
            "lights": { "lamps": [ { "type": "head", "x": 0.8, "z": 4.9 }, { "type": "workRear", "z": -2, "yawDeg": 180 } ] }
          },
          "visual": { "nodes": { "boom": "Boom", "hook": "Hook" } }
        }
        """;

    private const string Machines = "[" + Forwarder + """
        ,
        {
          "id": "test_wide_cultivator", "name": "Wide Tiller", "size": { "length": 2, "width": 6, "height": 1.4, "centerZ": -1 },
          "components": {
            "attachable": { "type": "threePoint", "mode": "mounted", "lowerable": true },
            "animatedParts": { "startFolded": true, "parts": [
              { "id": "wing_left", "rotationDeg": [0, 0, 90], "seconds": 1.5, "key": "fold" },
              { "id": "wing_right", "rotationDeg": [0, 0, -90], "seconds": 1.5, "key": "fold" },
              { "id": "marker", "rotationDeg": [0, 0, 45], "seconds": 1 } ] },
            "workAreas": { "areas": [ { "type": "cultivator", "width": 6, "length": 1, "z": -1.05, "maxWorkSpeedKmh": 12, "requiredPowerHp": 110 } ] }
          }
        },
        {
          "id": "test_middle_cultivator", "name": "Headland Tiller", "noun": "tiller", "size": { "length": 2, "width": 6, "height": 1.4, "centerZ": -1 },
          "components": {
            "attachable": { "type": "threePoint", "mode": "mounted", "lowerable": true },
            "animatedParts": { "startFolded": true, "words": { "fold": ["Fold up", "Fold down"] }, "parts": [
              { "id": "wing_left", "rotationDeg": [0, 0, 90], "seconds": 2, "key": "fold", "middle": 0.25 },
              { "id": "wing_right", "rotationDeg": [0, 0, -90], "seconds": 2, "key": "fold", "middle": 0.25 } ] },
            "workAreas": { "areas": [ { "type": "cultivator", "width": 6, "length": 1, "z": -1.05, "maxWorkSpeedKmh": 12, "requiredPowerHp": 110 } ] }
          }
        },
        {
          "id": "test_legged_trailer", "name": "Legged trailer", "size": { "length": 5, "width": 2.4, "height": 2, "centerZ": 0 },
          "components": {
            "runningGear": { "axles": [ { "z": 0, "track": 1.9 } ] },
            "attachable": { "type": "drawbar", "mode": "trailed", "z": 3 },
            "animatedParts": { "parts": [ { "id": "leg", "offset": [0, -0.4, 0], "seconds": 0.5, "support": true } ] }
          }
        },
        {
          "id": "test_loader", "name": "Loader", "size": { "length": 2.5, "width": 2, "height": 1.5, "centerZ": 1 },
          "components": {
            "attachable": { "type": "frontLoader", "mode": "mounted" },
            "craneArm": { "joints": [ { "id": "lift", "axis": "pitch", "min": -10, "max": 70, "speed": 25 } ] }
          }
        },
        {
          "id": "test_bracket_tractor", "name": "Bracket Tractor", "size": { "length": 4.5, "width": 2.4, "height": 3, "centerZ": 1.2 },
          "components": {
            "runningGear": { "axles": [ { "z": 0, "track": 1.8 }, { "z": 2.6, "track": 1.8, "steering": "front" } ] },
            "motor": { "powerHp": 120 },
            "drivable": {},
            "frontLoaderBracket": { "z": 1.9 }
          }
        }
        ]
        """;

    private static ContentDatabase Content(string json = Machines) => TestContent.WithMachines(json);

    private static Simulation Sim(ContentDatabase content)
    {
        var sim = Simulation.Create(content);
        TestContent.OwnField4(sim);
        return sim;
    }

    [Fact]
    public void MachinesAreBuiltFromTheComponentsTheirJsonLists()
    {
        var def = Content().Machines["test_forwarder"];
        Assert.Equal(["runningGear", "motor", "drivable", "lights", "craneArm", "winch", "saw"], def.Components.Select(c => c.Kind));
        Assert.Equal(3f, def.Get<RunningGearDef>()!.Wheelbase());

        var tractor = TestContent.Content.Machines["tractor_125"];
        Assert.NotNull(tractor.Get<MotorDef>());
        Assert.Equal(["rear", "drawbar", "front"], tractor.Joints.Select(j => j.Id));
    }

    [Fact]
    public void UnknownComponentsAndBadSettingsAreRefusedByName()
    {
        var unknown = Assert.Throws<ContentException>(() => Content("""[{ "id": "x", "components": { "motorised": {} } }]"""));
        Assert.Contains("unknown component 'motorised'", unknown.Message);
        // The blocks machines had before components.
        var old = Assert.Throws<ContentException>(() => Content("""[{ "id": "x", "motorized": { "powerHp": 100 } }]"""));
        Assert.Contains("'motorized'", old.Message);

        var bad = Assert.Throws<ContentException>(() => Content("""
            [{ "id": "x", "components": {
                 "motor": {}, "drivable": {},
                 "winch": { "joint": "boom" },
                 "workAreas": { "areas": [ { "type": "seeder" } ] } },
               "visual": { "nodes": { "wheel0L": "Wheel" } } }]
            """));
        Assert.Contains("machine 'x' motor: needs a runningGear that steers", bad.Message);
        Assert.Contains("machine 'x' winch: crane joint 'boom' missing", bad.Message);
        Assert.Contains("machine 'x' workAreas: a seeder needs the fillUnit its seed comes from", bad.Message);
        Assert.Contains("machine 'x': visual.nodes role 'wheel0L' is not one of its components'", bad.Message);
    }

    [Fact]
    public void AKindIsRefusedOnWhatItDoesNotGoOn()
    {
        var poi = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "test_shed", "name": "Shed", "components": { "motor": {}, "tipper": {} } }]
            """));
        Assert.Contains("poi 'test_shed' motor: goes on machines only", poi.Message);
        Assert.Contains("poi 'test_shed' tipper: goes on machines only", poi.Message);
    }

    [Fact]
    public void ACraneMovesItsJointsWithinTheirTravelAndCarriesItsTools()
    {
        var sim = Sim(Content());
        var m = sim.Machines.Spawn("test_forwarder", new Vector2(269f, 300f), 0f);
        var crane = m.Get<CraneArm>()!;
        // At rest the stick reaches straight out: rope from its end, 3 m past the stick's pivot 4 m along the boom.
        AssertNear(new Vector3(0f, 2.5f, 8f), m.Get<Winch>()!.Anchor);

        crane.MoveTo("boom", 30f);
        crane.MoveTo("slew", 90f);
        crane.MoveTo("stick", 5f); // beyond its 2 m of travel
        Run(sim, 1f);
        Assert.InRange(crane.Joint("boom")!.Value, 19.5f, 20.5f); // 20°/s
        Run(sim, 4f);
        Assert.Equal((30f, 90f, 2f), (crane.Joint("boom")!.Value, crane.Joint("slew")!.Value, crane.Joint("stick")!.Value));

        // Turned left and lifted 30°: the stick's 4 m out along the boom, 2 m slid out, then 3 m to its end.
        var (sin, cos) = MathF.SinCos(30f * MathUtil.Deg2Rad);
        AssertNear(new Vector3((4f + 2f + 3f) * cos, 2.5f + 9f * sin, 1f), m.Get<Winch>()!.Anchor);
        // The saw hangs half a meter under the stick's end, in the stick's frame: tipped up with it.
        AssertNear(m.Get<Winch>()!.Anchor + new Vector3(0.5f * sin, -0.5f * cos, 0f), m.Get<Saw>()!.Blade);

        m.Get<Winch>()!.ReelTo(4f);
        Run(sim, 1f);
        Assert.InRange(m.Get<Winch>()!.Length, 2.4f, 2.6f); // 0.5 m + 2 m/s
        Run(sim, 1f);
        AssertNear(m.Get<Winch>()!.Anchor - new Vector3(0f, 4f, 0f), m.Get<Winch>()!.Hook);
    }

    [Fact]
    public void TheToolKeysMoveTheSelectedControlGroup()
    {
        var sim = Sim(Content());
        var m = sim.Machines.Spawn("test_forwarder", new Vector2(269f, 300f), 0f);
        var crane = m.Get<CraneArm>()!;
        sim.Player.Enter(m);
        string Hint() => sim.Offers().Of(InputActions.SelectImplement)!.Label;
        void Hold(float y, float x, float seconds)
        {
            sim.Player.Controls.Input = new VehicleInput { ToolY = y, ToolX = x, Brake = true };
            Run(sim, seconds);
            sim.Player.Controls.Input = new VehicleInput { Brake = true };
        }

        // The first group: up lifts the boom, right turns the crane right (its yaw is positive to the left).
        Assert.Equal("Select Forwarder: stick", Hint());
        Hold(1f, -1f, 1f);
        Assert.Equal((20f, -30f, 0f), (crane.Joint("boom")!.Value, crane.Joint("slew")!.Value, crane.Joint("stick")!.Value), new FloatTuple(0.6f));
        // The second: up slides the stick out.
        sim.Perform(InputActions.SelectImplement);
        Assert.Equal("Select Forwarder: crane", Hint());
        Hold(1f, 0f, 1f);
        Assert.InRange(crane.Joint("boom")!.Value, 19.5f, 20.5f);
        Assert.InRange(crane.Joint("stick")!.Value, 0.48f, 0.52f);
        sim.Perform(InputActions.SelectImplement);
        Assert.Equal(1, m.Get<Drivable>()!.Group);

        // The mouse with Ctrl takes the next group: the stick, with the crane's group selected.
        sim.Player.Controls.Input = new VehicleInput { ToolY = -1f, ToolGroupOffset = 1, Brake = true };
        Run(sim, 1f);
        Assert.InRange(crane.Joint("boom")!.Value, 19.5f, 20.5f);
        Assert.InRange(crane.Joint("stick")!.Value, 0f, 0.02f);

        // The mouse's hint and a saw's action on its left button, while it's on the tool.
        Assert.Equal("Move", sim.Offers().Of(InputActions.ToolMouse)!.Label);
        Assert.Equal("Start the saw", sim.Offers().Of(InputActions.ToolAction)!.Label);
        sim.Perform(InputActions.ToolAction);
        Assert.True(m.Get<Saw>()!.On);
    }

    [Fact]
    public void ALoaderArmLiftsAndTiltsOnTheToolKeys()
    {
        var sim = Sim(Content());
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f, configuration: new Dictionary<string, string> { ["frontLoader"] = "bracket" });
        var arm = sim.Machines.Spawn("frontloader_arm", new Vector2(269f, 301.5f), 0f);
        sim.Player.Enter(t);
        sim.Perform(InputActions.Attach);
        Assert.Same(arm, t.Get<FrontLoaderBracket>()!.Arm);
        var loader = arm.Get<CraneArm>()!;

        // With the vehicle selected, the tool keys move the arm on it: up lifts it, left tilts the tool back.
        sim.Player.Controls.Input = new VehicleInput { ToolY = 1f, ToolX = 1f, Brake = true };
        Run(sim, 1f);
        Assert.Equal((20f, 35f), (loader.Joint("lift")!.Value, loader.Joint("tilt")!.Value), new FloatTuple(0.6f));
        Run(sim, 4f);
        Assert.Equal((55f, 40f), (loader.Joint("lift")!.Value, loader.Joint("tilt")!.Value));
        // The arm hides the headlights in the hood: those on the roof shine instead.
        Assert.True(Lights.TopLights(t));
        // Its hint for the mouse, which the key hints show.
        Assert.Equal(("Move front loader", true), (sim.Offers().Of(InputActions.ToolMouse)!.Label, sim.Offers().Of(InputActions.ToolMouse)!.Hinted));
    }

    [Fact]
    public void TipControlMovesACranesTipUpAndOut()
    {
        var content = Content(Machines.TrimEnd().TrimEnd(']') + """
            ,
            {
              "id": "test_knuckle", "name": "Knuckle crane", "size": { "length": 4, "width": 2.4, "height": 3, "centerZ": 1 },
              "components": {
                "runningGear": { "axles": [ { "z": 0, "track": 2 }, { "z": 3, "track": 2, "steering": "front" } ] },
                "motor": {}, "drivable": {},
                "craneArm": { "joints": [
                  { "id": "boom", "axis": "pitch", "offset": [0, 2, 0], "min": -30, "max": 80, "rest": 20, "speed": 45, "control": "y" },
                  { "id": "jib", "axis": "pitch", "offset": [0, 0, 4], "min": -150, "max": 0, "rest": -60, "speed": 45, "control": "x" } ],
                  "ik": { "joints": ["boom", "jib"], "tip": [0, 0, 3], "speed": 1 } }
              }
            }
            ]
            """);
        var sim = Sim(content);
        var m = sim.Machines.Spawn("test_knuckle", new Vector2(269f, 300f), 0f);
        var crane = m.Get<CraneArm>()!;
        sim.Player.Enter(m);
        var start = crane.Tip!.Value;

        Assert.Equal("Crane: move its tip", sim.Offers().Of(InputActions.ToolIk)!.Label);
        sim.Perform(InputActions.ToolIk);
        Assert.True(crane.TipControl);
        // The boom's keys raise the tip straight up, the jib's push it straight out, a meter a second.
        sim.Player.Controls.Input = new VehicleInput { ToolY = 1f, Brake = true };
        Run(sim, 1f);
        var up = crane.Tip!.Value - start;
        Assert.InRange(up.Y, 0.9f, 1.1f);
        Assert.InRange(MathF.Abs(up.Z), 0f, 0.05f);
        // Half a meter out: the arm, 7 m long, reaches no further than that from where it is.
        sim.Player.Controls.Input = new VehicleInput { ToolX = 1f, Brake = true };
        Run(sim, 0.5f);
        var @out = crane.Tip!.Value - start - up;
        Assert.InRange(@out.Z, 0.45f, 0.55f);
        Assert.InRange(MathF.Abs(@out.Y), 0f, 0.05f);

        var bad = Assert.Throws<ContentException>(() => Content("""
            [{ "id": "x", "components": { "craneArm": { "joints": [
                 { "id": "a", "control": "z" }, { "id": "b", "control": "y" }, { "id": "c", "control": "y" }, { "id": "d", "axis": "yaw", "control": "x" } ],
               "ik": { "joints": ["b", "d"] } } } }]
            """)).Message;
        Assert.Contains("machine 'x' craneArm: joint 'a': control must be x or y", bad);
        Assert.Contains("machine 'x' craneArm: joints 'b', 'c' share the y keys of group 1", bad);
        Assert.Contains("machine 'x' craneArm: ik: its joints must pitch, with keys", bad);
    }

    /// <summary>Compares tuples of floats within a tolerance.</summary>
    private sealed class FloatTuple(float tolerance) : IEqualityComparer<(float, float)>, IEqualityComparer<(float, float, float)>
    {
        public bool Equals((float, float) a, (float, float) b) => Near(a.Item1, b.Item1) && Near(a.Item2, b.Item2);
        public bool Equals((float, float, float) a, (float, float, float) b) => Near(a.Item1, b.Item1) && Near(a.Item2, b.Item2) && Near(a.Item3, b.Item3);
        public int GetHashCode((float, float) obj) => 0;
        public int GetHashCode((float, float, float) obj) => 0;
        private bool Near(float a, float b) => MathF.Abs(a - b) <= tolerance;
    }

    [Fact]
    public void ComponentStateIsSaved()
    {
        var content = Content();
        var sim = Sim(content);
        var m = sim.Machines.Spawn("test_forwarder", new Vector2(269f, 300f), 0f);
        m.Get<CraneArm>()!.MoveTo("boom", 45f);
        m.Get<Winch>()!.ReelTo(3f);
        m.Get<Lights>()!.Step = 2;
        m.Get<Lights>()!.Signal = TurnSignal.Hazards;
        sim.Player.Enter(m);
        sim.Perform(InputActions.TurnOn);
        Assert.True(m.Get<Saw>()!.On);
        Run(sim, 1f);

        var file = SaveGame.Capture(sim, "test");
        var loaded = SaveGame.Load(content, file).Sim.Machines.ById(m.Id)!;
        Assert.Equal((m.Get<CraneArm>()!.Joint("boom")!.Value, 45f), (loaded.Get<CraneArm>()!.Joint("boom")!.Value, loaded.Get<CraneArm>()!.Joint("boom")!.Target));
        Assert.Equal((m.Get<Winch>()!.Length, 3f), (loaded.Get<Winch>()!.Length, loaded.Get<Winch>()!.Target));
        Assert.True(loaded.Get<Saw>()!.On);
        Assert.Equal((2, TurnSignal.Hazards), (loaded.Get<Lights>()!.Step, loaded.Get<Lights>()!.Signal));

        // Saved per component kind; the seat keeps nothing.
        var saved = JsonSerializer.Deserialize<SaveState>(file.State, SaveGame.Json)!.Machines.Single(s => s.Id == m.Id);
        Assert.Equal(["craneArm", "lights", "motor", "runningGear", "saw", "winch"], saved.Components.Keys.Order());
    }

    [Fact]
    public void AFoldedImplementIsUnfoldedBeforeItGoesDownAndWorks()
    {
        var sim = Sim(Content());
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("test_wide_cultivator", new Vector2(269f, 278f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        var parts = c.Get<AnimatedParts>()!;
        var lift = c.Get<Attachable>()!;
        Assert.True(parts.Folded);
        Assert.Equal(1f, parts.Part("wing_left")!.Position);

        sim.Player.Enter(t);
        // Folded, it stays up: it's unfolded with the fold key first.
        sim.Perform(InputActions.Lower);
        Assert.True(parts.Folded);
        Assert.False(lift.Lowered);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Unfold the {c.Def.Name} first");
        sim.Perform(InputActions.Fold);
        sim.Perform(InputActions.Lower);
        Assert.False(parts.Folded);
        Assert.True(lift.Lowered);
        Run(sim, 1f);
        Assert.Equal(0f, lift.LowerAnim); // still unfolding
        Run(sim, 1.5f);
        Assert.True(parts.Unfolded && lift.LowerAnim > 0.9f);
        Assert.Equal(0f, parts.Part("marker")!.Position); // moves on its own, not with the folding

        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 1f } };
        Run(sim, 10f);
        var cultivated = CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4);
        Assert.True(cultivated > 12 * 40, $"cultivated cells: {cultivated}"); // 6 m wide

        // Folding raises it, and it stops working.
        sim.Perform(InputActions.Fold);
        Assert.False(lift.Lowered);
        Run(sim, 2f);
        var after = CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4);
        Run(sim, 3f);
        Assert.Equal(after, CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4));
    }

    [Fact]
    public void WingsWithAMiddlePoseUnfoldOnTheFoldKeyAndGoDownOnTheLowerKey()
    {
        var sim = Sim(Content());
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("test_middle_cultivator", new Vector2(269f, 278f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        var parts = c.Get<AnimatedParts>()!;
        var wing = parts.Part("wing_left")!;
        sim.Player.Enter(t);
        string Label(string action) => sim.Offers().Of(action)!.Label;

        // Its own words on the fold key, and the lower key naming what it is.
        Assert.Equal(("Fold down", "Lower tiller"), (Label(InputActions.Fold), Label(InputActions.Lower)));
        sim.Perform(InputActions.Fold);
        Run(sim, 2f);
        // Unfolded, the wings stand in their middle pose while it's raised: it doesn't work there.
        Assert.Equal((0.25f, true, false), (wing.Position, parts.Unfolded, parts.InWorkingPose));
        Assert.Equal("Fold up", Label(InputActions.Fold));

        sim.Perform(InputActions.Lower);
        Run(sim, 0.6f);
        Assert.Equal((0f, true), (wing.Position, parts.InWorkingPose));
        Assert.Equal("Lift tiller", Label(InputActions.Lower));
        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 1f } };
        Run(sim, 5f);
        Assert.True(CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4) > 0);

        // Raised, back to the middle pose; folded from there, all the way up.
        sim.Perform(InputActions.Lower);
        Run(sim, 0.6f);
        Assert.Equal(0.25f, wing.Position);
        sim.Perform(InputActions.Lower);
        sim.Perform(InputActions.Fold);
        Assert.False(c.Get<Attachable>()!.Lowered);
        Run(sim, 2f);
        Assert.Equal((1f, true), (wing.Position, parts.Folded));
    }

    [Fact]
    public void APlowRotatesOnTheTurnOnKeyInItsOwnWords()
    {
        var content = Content(Machines.TrimEnd().TrimEnd(']') + """
            ,
            {
              "id": "test_reversible_plow", "name": "Turnover 4", "category": "plow", "size": { "length": 4, "width": 2, "height": 1.4, "centerZ": -2 },
              "components": {
                "attachable": { "type": "threePoint", "mode": "mounted", "lowerable": true },
                "animatedParts": { "words": { "turn_on": ["Rotate plow", "Rotate plow"] }, "parts": [
                  { "id": "frame", "rotationDeg": [0, 0, 180], "seconds": 1.5, "key": "turn_on" } ] },
                "workAreas": { "areas": [ { "type": "plow", "width": 1.6, "length": 1, "z": -2.2 } ] }
              }
            }
            ]
            """);
        var sim = Sim(content);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var plow = sim.Machines.Spawn("test_reversible_plow", new Vector2(269f, 278f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", plow));
        sim.Player.Enter(t);
        string Label(string action) => sim.Offers().Of(action)!.Label;

        // The lower key names it after its shop category; rotating says the same both ways.
        Assert.Equal(("Lower plow", "Rotate plow"), (Label(InputActions.Lower), Label(InputActions.TurnOn)));
        sim.Perform(InputActions.TurnOn);
        Run(sim, 1.6f);
        Assert.Equal(1f, plow.Get<AnimatedParts>()!.Part("frame")!.Position);
        Assert.Equal("Rotate plow", Label(InputActions.TurnOn));
        sim.Perform(InputActions.TurnOn);
        Run(sim, 1.6f);
        Assert.Equal(0f, plow.Get<AnimatedParts>()!.Part("frame")!.Position);
    }

    [Fact]
    public void PartsMoveOnTheKeysTheirJsonGivesInTheirWords()
    {
        var bad = Assert.Throws<ContentException>(() => Content("""
            [{ "id": "x", "size": { "length": 2, "width": 3, "height": 1, "centerZ": 0 },
               "components": {
                 "attachable": { "words": { "lower": ["Lower", "Raise"] } },
                 "animatedParts": { "words": { "fold": ["Fold"], "unload": ["Open", "Close"] }, "parts": [
                   { "id": "a", "key": "unload" },
                   { "id": "b", "key": "lower" },
                   { "id": "c", "key": "fold", "middle": 0.5 },
                   { "id": "d", "middle": 1 },
                   { "id": "e", "key": "fold", "support": true },
                   { "id": "f", "key": "turn_on" } ] },
                 "workAreas": { "areas": [ { "type": "mower", "requiresOn": true, "harvestGroups": ["grass"] } ] } } }]
            """)).Message;
        Assert.Contains("machine 'x' attachable: words: it offers no key to name ('lower')", bad);
        Assert.Contains("machine 'x' animatedParts: words: 'fold' needs two texts, to do it and to undo it", bad);
        Assert.Contains("machine 'x' animatedParts: words: 'unload' is not one of its keys (fold", bad);
        Assert.Contains("part 'a': key must be fold, lower, move_parts, turn_on", bad);
        Assert.Contains("part 'b': only a machine that lowers (attachable lowerable) moves parts as it's lowered", bad);
        Assert.Contains("part 'c': only a machine that lowers", bad);
        Assert.Contains("part 'd': middle must be in [0, 1)", bad);
        Assert.Contains("part 'e': a part moving on a key follows neither a trigger nor the hitch", bad);
        Assert.Contains("part 'f': the turn-on key turns the machine on; put the part on another key", bad);
        // Settings from before the keys are refused rather than ignored.
        Assert.Throws<ContentException>(() => Content("""
            [{ "id": "x", "components": { "animatedParts": { "parts": [ { "id": "a", "fold": true } ] } } }]
            """));
    }

    [Fact]
    public void TheDriverMovesThePartsThatDontFoldAndSupportLegsFollowTheHitch()
    {
        var sim = Sim(Content());
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("test_wide_cultivator", new Vector2(269f, 278f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        var parts = c.Get<AnimatedParts>()!;
        sim.Player.Enter(t);
        string Label() => sim.Offers().Of(InputActions.MoveParts)!.Label;

        Assert.Equal("Move marker", Label());
        sim.Perform(InputActions.MoveParts);
        Run(sim, 1.1f);
        // The marker moves, the folded wings stay as they are.
        Assert.Equal((1f, true), (parts.Part("marker")!.Position, parts.Folded));
        Assert.Equal("Move marker back", Label());

        // A support leg is down while its machine stands unhitched, and nobody's key moves it.
        var trailer = sim.Machines.Spawn("test_legged_trailer", new Vector2(269f, 270f), 0f);
        var leg = trailer.Get<AnimatedParts>()!.Part("leg")!;
        Assert.Equal((true, 1f), (leg.Target, leg.Position));
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        Run(sim, 0.6f);
        Assert.Equal(0f, leg.Position);
        Assert.False(trailer.Get<AnimatedParts>()!.Move("leg", true));
        t.Get<Drivable>()!.Selected = trailer;
        Assert.Null(sim.Offers().Of(InputActions.MoveParts));
        sim.Machines.Detach(trailer);
        Run(sim, 0.6f);
        Assert.Equal(1f, leg.Position);
    }

    [Fact]
    public void AHelperUnfoldsTheImplement()
    {
        var sim = Sim(Content());
        var field = sim.World.FieldById(4)!;
        var t = sim.Machines.Spawn("tractor_125", field.Shape.Min + new Vector2(4f, -10f), 0f);
        var c = sim.Machines.Spawn("test_wide_cultivator", field.Shape.Min + new Vector2(4f, -12f), 0f);
        sim.Machines.Attach(t, "rear", c);
        sim.HireHelper(t, field, maxLanes: 1);
        Assert.False(c.Get<AnimatedParts>()!.Folded);
    }

    [Fact]
    public void AFrontLoaderHitchesToTheBracket()
    {
        var sim = Sim(Content());
        var t = sim.Machines.Spawn("test_bracket_tractor", new Vector2(269f, 300f), 0f);
        var loader = sim.Machines.Spawn("test_loader", new Vector2(269f, 301.9f), 0f);
        var found = sim.Machines.FindAttachable(t);
        Assert.Equal(("frontLoader", loader), (found?.joint.Id, found?.child));
        sim.Player.Enter(t);
        sim.Perform(InputActions.Attach);
        Assert.Same(loader, t.Get<FrontLoaderBracket>()!.Arm);
        Assert.False(sim.Machines.Attach(sim.Machines.Spawn("tractor_125", new Vector2(250f, 300f), 0f), "rear", sim.Machines.Spawn("test_loader", new Vector2(250f, 297f), 0f)));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    private static int CountCells(Simulation sim, Func<int, bool> predicate)
    {
        var n = 0;
        for (var i = 0; i < sim.World.Layers.Ground.Length; i++)
            if (predicate(i)) n++;
        return n;
    }
}
