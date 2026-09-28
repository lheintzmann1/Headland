using System.Numerics;
using System.Text.Json;
using Headland.Core.Components;
using Headland.Core.Content;
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
            "craneArm": { "joints": [
              { "id": "slew", "axis": "yaw", "offset": [0, 2, 1], "min": -120, "max": 120, "speed": 30 },
              { "id": "boom", "axis": "pitch", "offset": [0, 0.5, 0], "min": -20, "max": 60, "speed": 20 },
              { "id": "stick", "axis": "extend", "offset": [0, 0, 4], "min": 0, "max": 2, "speed": 0.5 } ] },
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
              { "id": "wing_left", "rotationDeg": [0, 0, 90], "seconds": 1.5, "fold": true },
              { "id": "wing_right", "rotationDeg": [0, 0, -90], "seconds": 1.5, "fold": true },
              { "id": "marker", "rotationDeg": [0, 0, 45], "seconds": 1 } ] },
            "workAreas": { "areas": [ { "type": "cultivator", "width": 6, "length": 1, "z": -1.05, "maxWorkSpeedKmh": 12, "requiredPowerHp": 110 } ] }
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
    public void ComponentStateIsSaved()
    {
        var content = Content();
        var sim = Sim(content);
        var m = sim.Machines.Spawn("test_forwarder", new Vector2(269f, 300f), 0f);
        m.Get<CraneArm>()!.MoveTo("boom", 45f);
        m.Get<Winch>()!.ReelTo(3f);
        Assert.True(m.Get<Lights>()!.Switch("workRear", true));
        Assert.False(m.Get<Lights>()!.Switch("beacon", true));
        sim.Player.Enter(m);
        sim.CommandTurnOn();
        Assert.True(m.Get<Saw>()!.On);
        Run(sim, 1f);

        var file = SaveGame.Capture(sim, "test");
        var loaded = SaveGame.Load(content, file).Sim.Machines.ById(m.Id)!;
        Assert.Equal((m.Get<CraneArm>()!.Joint("boom")!.Value, 45f), (loaded.Get<CraneArm>()!.Joint("boom")!.Value, loaded.Get<CraneArm>()!.Joint("boom")!.Target));
        Assert.Equal((m.Get<Winch>()!.Length, 3f), (loaded.Get<Winch>()!.Length, loaded.Get<Winch>()!.Target));
        Assert.True(loaded.Get<Saw>()!.On);
        Assert.Equal(["workRear"], loaded.Get<Lights>()!.On);

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
        sim.Machines.ToggleLower(t);
        Assert.True(parts.Folded);
        Assert.False(lift.Lowered);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Unfold the {c.Def.Name} first");
        sim.Machines.ToggleFold(t);
        sim.Machines.ToggleLower(t);
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
        sim.Machines.ToggleFold(t);
        Assert.False(lift.Lowered);
        Run(sim, 2f);
        var after = CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4);
        Run(sim, 3f);
        Assert.Equal(after, CountCells(sim, i => sim.World.Layers.Ground[i] == (byte)GroundType.Cultivated && sim.World.Layers.FieldId[i] == 4));
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
        sim.Machines.ToggleAttach(t);
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
