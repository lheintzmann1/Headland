using System.Numerics;
using System.Text.Json.Nodes;
using Headland.Core.Content;
using Headland.Core.Machines.Components;

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
                 { "id": "empty", "name": "Empty", "options": [] } ] }]
            """));
        Assert.Contains("machine 'x' (wheels: tracks) runningGear: tracks don't steer: they go on fixed axles", bad.Message);
        Assert.Contains("machine 'x' (wheels: typo): The JSON property 'colour' could not be mapped", bad.Message);
        Assert.Contains("machine 'x' (wheels: cheap): price must be >= 0 and mass > 0", bad.Message);
        Assert.Contains("machine 'x' configuration 'wheels': option 'single' is defined more than once", bad.Message);
        Assert.Contains("machine 'x' configuration 'engine' option 'b': changes can't set 'id'", bad.Message);
        Assert.Contains("machine 'x' configuration 'empty': needs options", bad.Message);
        // Only what an option gets wrong: the machine as it comes is fine.
        Assert.DoesNotContain("machine 'x' motor", bad.Message);
    }
}
