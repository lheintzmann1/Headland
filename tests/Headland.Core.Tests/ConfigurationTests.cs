using System.Numerics;
using System.Text.Json.Nodes;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;

namespace Headland.Core.Tests;

public class ConfigurationTests
{
    private static Dictionary<string, string> Options(params (string configuration, string option)[] choices) =>
        choices.ToDictionary(c => c.configuration, c => c.option);

    [Fact]
    public void ChangesMergeObjectsAndArraysByIdOrIndex()
    {
        var target = JsonNode.Parse("""
            { "size": { "width": 2, "height": 3 }, "color": "#111", "tags": ["a", "b"],
              "joints": [ { "id": "rear", "z": -1 }, { "id": "drawbar", "z": -1.5 } ],
              "axles": [ { "z": 0, "wheels": { "type": "single" } }, { "z": 2 } ],
              "loader": { "z": 1 } }
            """)!.AsObject();
        JsonMerge.Into(target, JsonNode.Parse("""
            { "Size": { "width": 3.5 }, "color": "#222", "tags": ["c"],
              "joints": [ { "id": "front", "z": 3 }, { "id": "rear", "y": 0.5 } ],
              "axles": [ { "wheels": { "type": "dual" } }, {}, { "z": -2 } ],
              "loader": null }
            """)!.AsObject());

        Assert.Equal("""
            {"size":{"width":3.5,"height":3},"color":"#222","tags":["c"],"joints":[{"id":"rear","z":-1,"y":0.5},{"id":"drawbar","z":-1.5},{"id":"front","z":3}],"axles":[{"z":0,"wheels":{"type":"dual"}},{"z":2},{"z":-2}]}
            """, target.ToJsonString());
    }

    [Fact]
    public void AMachineComesWithItsDefaultOptions()
    {
        var tractor = TestContent.Content.Machines["tractor_125"];
        Assert.Equal(Options(("wheels", "single"), ("frontLoader", "none"), ("frontHitch", "threePoint"), ("beacons", "none"),
            ("engine", "125"), ("color", "red")), tractor.Choices);
        Assert.Equal((98000f, 5600f, 125f), (tractor.Price, tractor.Mass, tractor.Get<MotorDef>()!.PowerHp));
        Assert.Equal(["rear", "drawbar", "front"], tractor.Joints.Select(j => j.Id));
        Assert.Equal("125 hp", tractor.Chosen(tractor.Configurations.Single(c => c.Id == "engine"))!.Name);
    }

    [Fact]
    public void OptionsChangeThePriceTheMassAndTheComponents()
    {
        var content = TestContent.Content;
        var tractor = content.Machines["tractor_125"].Configure(Options(("wheels", "dual"), ("frontLoader", "bracket"),
            ("frontHitch", "weight"), ("beacons", "lightbar"), ("engine", "145"), ("color", "graphite")));

        Assert.Equal(98000f + 5800f + 4200f - 1800f + 1250f + 12500f, tractor.Price);
        Assert.Equal(5600f + 620f + 310f + 650f + 14f + 90f, tractor.Mass);
        Assert.All(tractor.Get<RunningGearDef>()!.Axles, a => Assert.Equal("dual", a.Wheels.Type));
        Assert.Equal((0.8f, 3.6f), (tractor.Get<RunningGearDef>()!.Axles[0].Wheels.Radius, tractor.Size.Width));
        Assert.Equal(["rear", "drawbar", "frontLoader"], tractor.Joints.Select(j => j.Id));
        Assert.Equal(["head", "head", "beacon", "beacon", "beacon", "beacon"], tractor.Get<LightsDef>()!.Lamps.Select(l => l.Type));
        Assert.Equal((145f, 2.4f, 40f), (tractor.Get<MotorDef>()!.PowerHp, tractor.Get<MotorDef>()!.Acceleration, tractor.Get<MotorDef>()!.MaxSpeedKmh));
        Assert.Equal("#48494a", tractor.Visual.Color);

        // The machine as it comes is left as it was.
        var standard = content.Machines["tractor_125"];
        Assert.Equal(("single", 2.45f, 2), (standard.Get<RunningGearDef>()!.Axles[0].Wheels.Type, standard.Size.Width, standard.Get<LightsDef>()!.Lamps.Length));
    }

    [Fact]
    public void ConfiguringChangesTheOptionsGivenAndBuildsEachSetOnce()
    {
        var tractor = TestContent.Content.Machines["tractor_125"];
        var dual = tractor.Configure(Options(("wheels", "dual")));
        Assert.Same(dual, tractor.Configure(Options(("wheels", "dual"))));
        Assert.Same(tractor, dual.Configure(Options(("wheels", "single"))));

        var stronger = dual.Configure(Options(("engine", "145")));
        Assert.Equal(("dual", "145"), (stronger.Choices["wheels"], stronger.Choices["engine"]));
        // What the machine doesn't have is left out, or taken as the default.
        Assert.Same(stronger, stronger.Configure(Options(("paint", "gold"))));
        Assert.Equal("single", stronger.Configure(Options(("wheels", "golden"))).Choices["wheels"]);
    }

    [Fact]
    public void ConfiguredMachinesDriveAndWorkAsTheirOptionsSay()
    {
        var sim = TestContent.NewSim();
        var tracked = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f, configuration: Options(("wheels", "tracks")));
        var gear = tracked.Get<RunningGear>()!;
        // A half-track: its front wheels steer, its rear tracks carry it on more ground than the tires would.
        Assert.Equal(SteeringKind.Axles, gear.SteeringKind);
        Assert.Equal(["track0L", "track0R", "wheel1L", "wheel1R"], gear.Def.Roles);
        Assert.True(gear.Def.ContactArea > sim.Content.Machines["tractor_125"].Get<RunningGearDef>()!.ContactArea * 2f);

        var cultivator = sim.Machines.Spawn("cultivator_3", new Vector2(250f, 300f), 0f, configuration: Options(("width", "4")));
        Assert.Equal((4f, 4f), (cultivator.Get<WorkAreas>()!.MinWidth, cultivator.Def.Size.Width));
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(230f, 300f), 0f, configuration: Options(("capacity", "20000")));
        Assert.Equal(20000f, trailer.Unit("main")!.Capacity);
        var seeder = sim.Machines.Spawn("seeder_3", new Vector2(210f, 300f), 0f, configuration: Options(("width", "4"), ("hopper", "2400")));
        Assert.Equal((2400f, 900f, 3.4f), (seeder.Unit("seed")!.Capacity, seeder.Unit("seed")!.Level, seeder.Get<RunningGear>()!.Def.Axles[0].Track));
    }

    [Fact]
    public void OptionsShowTheModelNodesOfWhatTheyAdd()
    {
        // The front weight is the node named after its option: hidden unless the tractor has it.
        var tractor = TestContent.Content.Machines["tractor_125"];
        Assert.True(tractor.Hides("frontHitch_weight"));
        Assert.False(tractor.Configure(Options(("frontHitch", "weight"))).Hides("frontHitch_weight"));
        Assert.False(tractor.Hides("cab"));

        var x = TestContent.WithMachines("""
            [{ "id": "x", "name": "X", "price": 1000, "size": { "length": 4, "width": 2 },
               "components": {
                 "runningGear": { "axles": [ { "z": 0 }, { "z": 2, "steering": "front" } ] }, "motor": {}, "drivable": {} },
               "visual": { "nodes": { "steeringWheel": "wheel_steering" } },
               "configurations": [
                 { "id": "wheels", "name": "Wheels", "options": [
                   { "id": "single", "name": "Single" },
                   { "id": "dual", "name": "Dual",
                     "changes": { "components": { "runningGear": { "axles": [ { "wheels": { "type": "dual" } } ] } } } },
                   { "id": "tracks", "name": "Tracks",
                     "changes": { "components": { "runningGear": { "axles": [ { "wheels": { "type": "tracks", "length": 1 } } ] } },
                                  "visual": { "nodes": { "track0L": "crawler_l" } } } } ] },
                 { "id": "beacons", "name": "Beacons", "options": [
                   { "id": "none", "name": "None" }, { "id": "left", "name": "Left" }, { "id": "right", "name": "Right" },
                   { "id": "both", "name": "Both", "show": ["beacons_left", "beacons_right"] } ] } ] }]
            """).Machines["x"];
        // An option's pieces: the node named after it, and those named after it and _… (Blender's .001 imports as _001).
        var dual = x.Configure(Options(("wheels", "dual")));
        Assert.Equal((true, true, true), (x.Hides("wheels_dual"), x.Hides("wheels_dual_0L"), x.Hides("wheels_dual_001")));
        Assert.Equal((false, false), (dual.Hides("wheels_dual"), dual.Hides("wheels_dual_0L")));
        Assert.False(x.Hides("wheels_dualWide"));
        Assert.Equal("wheels", x.UnknownOption("wheels_dualWide")?.Id);
        Assert.Null(x.UnknownOption("wheels_dual_001"));
        Assert.Null(x.UnknownOption("wheelsCover"));

        // Moving parts only some options have, named after their role or as visual.nodes says.
        var tracks = x.Configure(Options(("wheels", "tracks")));
        Assert.Equal((false, true, true), (x.Hides("wheel0L"), x.Hides("crawler_l"), x.Hides("track0R")));
        Assert.Equal((true, false, false), (tracks.Hides("wheel0L"), tracks.Hides("crawler_l"), tracks.Hides("track0R")));
        Assert.Equal((false, false), (tracks.Hides("wheel1L"), tracks.Hides("wheel_steering")));
        // The chosen options' own versions of a moving part come first.
        Assert.Equal(["wheels_tracks_track0L", "beacons_none_track0L", "crawler_l"], tracks.NodesOf("track0L"));
        Assert.Equal(["wheels_single_wheel0L", "beacons_none_wheel0L", "wheel0L"], x.NodesOf("wheel0L"));

        // Three-point linkages lift: the front one comes with the front hitch that has it.
        Assert.Equal(["rearLinkage", "frontLinkage"], tractor.Roles.Where(r => r.EndsWith("Linkage")));
        Assert.Equal(["rearLinkage"], tractor.Configure(Options(("frontHitch", "none"))).Roles.Where(r => r.EndsWith("Linkage")));

        // Pieces options share: both beacons are the left one and the right one.
        var left = x.Configure(Options(("beacons", "left")));
        var both = x.Configure(Options(("beacons", "both")));
        Assert.Equal((true, true), (x.Hides("beacons_left"), x.Hides("beacons_right")));
        Assert.Equal((false, true), (left.Hides("beacons_left"), left.Hides("beacons_right")));
        Assert.Equal((false, false), (both.Hides("beacons_left"), both.Hides("beacons_right")));

        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "y", "name": "Y", "components": {},
               "configurations": [ { "id": "c", "name": "C", "options": [ { "id": "o", "name": "O", "show": [""] } ] } ] }]
            """));
        Assert.Contains("machine 'y' configuration 'c' option 'o': show needs node names", bad.Message);
    }

    [Fact]
    public void TheWorkshopChangesOptionsForWhatTheyCostMoreAndTheWork()
    {
        var sim = TestContent.NewSim();
        var configured = PoiTests.Record<MachineConfigured>(sim);
        var bay = sim.World.PoiById("workshop")!.Trigger("repair")!;
        var (t, trailer) = PoiTests.TrailerAt(sim, bay.Area.Center - new Vector2(4f, 0f), "wheat", 12_000f);
        sim.Player.Enter(t);
        Assert.Contains("Change options…", Assert.Single(sim.Activations()).Label);
        var money = sim.Economy.Money;

        // Duals ($4,600) and a front linkage ($3,400), with $250 of work for each.
        Assert.True(sim.Pois.Configure(t, Options(("wheels", "dual"), ("frontHitch", "threePoint"))));
        Assert.Equal(money - 8500f, sim.Economy.Money, 1);
        Assert.Equal(-8500f, sim.Economy.Ledger.Today[MoneyCategory.Machines], 1);
        Assert.Equal(("dual", "threePoint"), (t.Def.Choices["wheels"], t.Def.Choices["frontHitch"]));
        Assert.Equal("dual", t.Get<RunningGear>()!.Def.Axles[0].Wheels.Type);
        Assert.NotNull(t.Joint("front"));
        // Still hitched, and still driven.
        Assert.Same(trailer, t.Attached["drawbar"]);
        Assert.Same(sim.Player.Controls, t.Get<Drivable>()!.Controller);
        Assert.Equal((t, 8500f), (configured.Single().Machine, configured.Single().Cost));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Refitted Fieldmaster 95 (wheels: Dual, front hitch: Front linkage) for $8,500");

        // A cheaper option gives nothing back: only the work is paid.
        money = sim.Economy.Money;
        Assert.True(sim.Pois.Configure(t, Options(("wheels", "single"))));
        Assert.Equal(money - 250f, sim.Economy.Money, 1);

        // The trailer keeps its load; it can't take a smaller bed while it holds more than that.
        Assert.True(sim.Pois.Configure(trailer, Options(("capacity", "20000"))));
        Assert.Equal((20_000f, 12_000f), (trailer.Unit("main")!.Capacity, trailer.Unit("main")!.Level));
        trailer.Unit("main")!.Add("wheat", 6_000f);
        Assert.False(sim.Pois.Configure(trailer, Options(("capacity", "16000"))));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Unload the Tipper 16 first: it holds more than it would take");

        // Elsewhere, nothing changes.
        sim.Machines.Teleport(t, new Vector2(269f, 300f), 0f);
        Assert.False(sim.Pois.Configure(t, Options(("color", "red"))));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Park at a workshop first");
        Assert.Equal("green", t.Def.Choices["color"]);
    }

    [Fact]
    public void TakingAJointAwayUnhitchesWhatHangsOnIt()
    {
        var sim = TestContent.NewSim();
        var bay = sim.World.PoiById("workshop")!.Trigger("repair")!;
        var t = sim.Machines.Spawn("tractor_125", bay.Area.Center, 0f);
        var front = sim.Machines.Spawn("cultivator_3", bay.Area.Center + new Vector2(0f, 4f), MathF.PI);
        Assert.True(sim.Machines.Attach(t, "front", front));

        Assert.True(sim.Pois.Configure(t, Options(("frontHitch", "weight"))));
        Assert.Null(front.Parent);
        Assert.Empty(t.Attached);
        Assert.Null(t.Joint("front"));
        Assert.Equal(5600f + 650f, t.Def.Mass);
    }

    [Fact]
    public void ChosenOptionsAreSavedAndMapsPlaceMachinesWithThem()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f, configuration: Options(("wheels", "tracks"), ("engine", "145")));
        var file = SaveGame.Capture(sim, "test");
        var loaded = SaveGame.Load(TestContent.Content, file);
        Assert.Same(t.Def, loaded.Sim.Machines.ById(t.Id)!.Def);
        Assert.Empty(loaded.Warnings);

        // An option that's gone gives the default.
        var state = System.Text.Encoding.UTF8.GetString(file.State).Replace("\"tracks\"", "\"golden\"");
        var changed = SaveGame.Load(TestContent.Content, file with { State = System.Text.Encoding.UTF8.GetBytes(state) });
        Assert.Equal(("single", "145"), (changed.Sim.Machines.ById(t.Id)!.Def.Choices["wheels"], changed.Sim.Machines.ById(t.Id)!.Def.Choices["engine"]));
        Assert.Contains("The Fieldmaster 125's wheels 'golden' no longer exists: it has Single", changed.Warnings);

        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Map.Machines[0].Configuration = Options(("wheels", "dual"));
        Assert.Equal("dual", Simulation.Create(db).Machines.All[0].Def.Choices["wheels"]);
        db.Map.Machines[0].Configuration = Options(("wheels", "golden"));
        Assert.Contains("map 'default' machine 0: tractor_125 has no option 'golden' of 'wheels'", db.Validate());
    }

    [Fact]
    public void EachOptionIsCheckedAndNamedWhenWrong()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "name": "X", "price": 1000, "size": { "length": 4, "width": 2 },
               "components": {
                 "runningGear": { "axles": [ { "z": 0 }, { "z": 2, "steering": "front" } ] }, "motor": {}, "drivable": {} },
               "configurations": [
                 { "id": "wheels", "name": "Wheels", "options": [
                   { "id": "single", "name": "Single" },
                   { "id": "tracks", "name": "Tracks",
                     "changes": { "components": { "runningGear": { "axles": [ {}, { "wheels": { "type": "tracks", "length": 1 } } ] } } } },
                   { "id": "typo", "name": "Typo", "changes": { "colour": "#fff" } },
                   { "id": "cheap", "name": "Cheap", "price": -2000 },
                   { "id": "single", "name": "Again" } ] },
                 { "id": "engine", "name": "Engine", "options": [
                   { "id": "a", "name": "A" }, { "id": "b", "name": "B", "changes": { "id": "y" } } ] },
                 { "id": "empty", "name": "Empty", "options": [] },
                 { "id": "front hitch", "name": "Front hitch", "options": [ { "id": "3-point", "name": "Three-point" } ] } ] }]
            """));
        Assert.Contains("machine 'x' (wheels: tracks) runningGear: tracks don't steer: they go on fixed axles", bad.Message);
        Assert.Contains("machine 'x' (wheels: typo): The JSON property 'colour' could not be mapped", bad.Message);
        Assert.Contains("machine 'x' (wheels: cheap): price must be >= 0 and mass > 0", bad.Message);
        Assert.Contains("machine 'x' configuration 'wheels': option 'single' is defined more than once", bad.Message);
        Assert.Contains("machine 'x' configuration 'engine' option 'b': changes can't set 'id'", bad.Message);
        Assert.Contains("machine 'x' configuration 'empty': needs options", bad.Message);
        Assert.Contains("machine 'x' configuration 'front hitch': its id must be letters and digits, as model nodes are named after it", bad.Message);
        Assert.Contains("machine 'x' configuration 'front hitch' option '3-point': its id must be letters and digits", bad.Message);
        // Only what an option gets wrong: the machine as it comes is fine.
        Assert.DoesNotContain("machine 'x' motor", bad.Message);
    }
}
