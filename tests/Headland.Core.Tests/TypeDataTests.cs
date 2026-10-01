using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines.Components;

namespace Headland.Core.Tests;

/// <summary>Kinds of joints and lamps are data (jointtypes.json, lamptypes.json), as a mod would add them.</summary>
public class TypeDataTests
{
    [Fact]
    public void JointAndLampTypesComeFromTheData()
    {
        var content = TestContent.Content;
        Assert.Equal(["threePoint", "drawbar", "fifthWheel", "header", "frontLoader", "loaderTool"], content.JointTypes.Keys.ToArray());
        Assert.Equal(["threePoint"], content.JointTypes.Values.Where(j => j.Linkage).Select(j => j.Id));
        Assert.Equal(["head", "tail", "workFront", "workRear", "highBeam", "beacon", "turnLeft", "turnRight", "brake", "reverse", "cab"], content.LampTypes.Keys.ToArray());
        Assert.Equal([("head", 1), ("tail", 1), ("workFront", 2), ("workRear", 2), ("highBeam", 0)],
            content.LampTypes.Values.Where(l => l.Control == "lights").Select(l => (l.Id, l.Step)));
        Assert.Equal(["beacon"], content.LampTypes.Values.Where(l => l.Rotating).Select(l => l.Id));
        Assert.Equal(["turnLeft", "turnRight"], content.LampTypes.Values.Where(l => l.Blinking).Select(l => l.Id));

        // A tractor's three-point linkage lifts; its drawbar has no linkage to.
        var tractor = content.Machines["tractor_125"];
        Assert.Contains("rearLinkage", tractor.Roles);
        Assert.DoesNotContain("drawbarLinkage", tractor.Roles);
        Assert.All(tractor.Joints, j => Assert.Same(content.JointTypes[j.Type], j.TypeDef));
        Assert.All(tractor.Get<LightsDef>()!.Lamps, l => Assert.Same(content.LampTypes[l.Type], l.TypeDef));
        // Options built later are linked too.
        var withLoader = tractor.Configure(new Dictionary<string, string> { ["frontLoader"] = "bracket", ["beacons"] = "both" });
        Assert.Same(content.JointTypes["frontLoader"], withLoader.Joints.Single(j => j.Id == "frontLoader").TypeDef);
        Assert.All(withLoader.Get<LightsDef>()!.Lamps, l => Assert.NotNull(l.TypeDef));
    }

    [Fact]
    public void ANewKindOfJointOrLampIsJustData()
    {
        var content = TestContent.Modded(new()
        {
            ["jointtypes.json"] = TestContent.WithEntry("jointtypes.json", """  { "id": "hookLift", "name": "Hook lift", "linkage": true }"""),
            ["lamptypes.json"] = TestContent.WithEntry("lamptypes.json", """  { "id": "marker", "name": "Marker lights", "step": 3 }"""),
            ["machines/test.json"] = """
                [{ "id": "truck", "name": "Truck", "size": { "length": 7, "width": 2.5 },
                   "components": {
                     "runningGear": { "axles": [ { "z": 0, "track": 2 }, { "z": 4.5, "track": 2, "steering": "front" } ] },
                     "motor": {}, "drivable": {},
                     "attacherJoints": { "joints": [ { "id": "hook", "type": "hookLift", "z": -1 } ] },
                     "lights": { "lamps": [ { "type": "marker", "z": 5 } ] } },
                   "visual": { "model": "res://truck.glb" } },
                 { "id": "container", "name": "Container", "size": { "length": 6, "width": 2.4 },
                   "components": { "attachable": { "type": "hookLift", "mode": "mounted" } },
                   "visual": { "model": "res://container.glb" } }]
                """,
        });
        Assert.Contains("hookLinkage", content.Machines["truck"].Roles);

        var sim = Simulation.Create(content);
        var truck = sim.Machines.Spawn("truck", new Vector2(60f, 248f), 0f);
        var container = sim.Machines.Spawn("container", new Vector2(60f, 240f), 0f);
        Assert.True(sim.Machines.Attach(truck, "hook", container));
        // Marker lights come on at the light key's step the data gives them: the truck's only one.
        sim.Player.Enter(truck);
        Assert.Equal("Marker lights on", sim.Offers().Of(Input.InputActions.Lights)!.Label);
        sim.Tick(1f / 60f);
        Assert.False(truck.Get<Lights>()!.Lit(0));
        sim.Perform(Input.InputActions.Lights);
        sim.Tick(1f / 60f);
        Assert.Equal(3, truck.Get<Lights>()!.Step);
        Assert.True(truck.Get<Lights>()!.Lit(0));
    }

    [Fact]
    public void UnknownKindsOfJointsAndLampsAreRefused()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": {
                 "attacherJoints": { "joints": [ { "id": "rear", "type": "hitchPin" } ] },
                 "attachable": { "type": "towBall" },
                 "frontLoaderBracket": { "type": "loaderArm" },
                 "lights": { "lamps": [ { "type": "laser" } ] } } }]
            """));
        const string known = "(known: threePoint, drawbar, fifthWheel, header, frontLoader, loaderTool)";
        Assert.Contains($"machine 'x' attacherJoints: joint 'rear': unknown type 'hitchPin' {known}", bad.Message);
        Assert.Contains($"machine 'x' attachable: unknown type 'towBall' {known}", bad.Message);
        Assert.Contains($"machine 'x' frontLoaderBracket: unknown type 'loaderArm' {known}", bad.Message);
        Assert.Contains("machine 'x' lights: unknown lamp type 'laser' (head, tail, workFront, workRear, highBeam, beacon, turnLeft, turnRight, brake, reverse, cab)", bad.Message);
    }
}
