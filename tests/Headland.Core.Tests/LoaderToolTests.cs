using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Objects;
using Headland.Core.Ownership;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;

namespace Headland.Core.Tests;

public class LoaderToolTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    /// <summary>A tractor with loader consoles at <paramref name="at"/> heading south, its loader arm hitched.</summary>
    private static (Machine tractor, Machine arm) Loader(Simulation sim, Vector2 at)
    {
        var t = sim.Machines.Spawn("tractor_125", at, 0f, configuration: new Dictionary<string, string> { ["frontLoader"] = "bracket" });
        var arm = sim.Machines.Spawn("frontloader_arm", at, 0f);
        Assert.True(sim.Machines.Attach(t, "frontLoader", arm));
        return (t, arm);
    }

    private static Machine Tool(Simulation sim, Machine arm, string id)
    {
        var tool = sim.Machines.Spawn(id, arm.Position, arm.Heading);
        Assert.True(sim.Machines.Attach(arm, "tool", tool));
        return tool;
    }

    /// <summary>Puts the tractor where its fork's tines reach <paramref name="p"/>, facing <paramref name="heading"/>, the arm down.</summary>
    private static void Reach(Simulation sim, Machine tractor, Machine fork, Vector2 p, float heading)
    {
        sim.Machines.Teleport(tractor, tractor.Position, heading);
        var tines = fork.LocalToWorld(0f, fork.Get<Fork>()!.Def.Area.Z);
        sim.Machines.Teleport(tractor, tractor.Position + p - tines, heading);
    }

    [Fact]
    public void ToolsHangOnTheArmsCarrierAndGoWithIt()
    {
        var sim = TestContent.NewSim();
        var (t, arm) = Loader(sim, new Vector2(269f, 300f));
        var spike = Tool(sim, arm, "bale_spike");
        var crane = arm.Get<CraneArm>()!;
        // At rest, on the carrier at the tool's pivot: 2.84 m ahead of the arm's pivots, just off the ground.
        Assert.True(Vector2.Distance(arm.LocalToWorld(0f, 2.84f), spike.Position) < 0.01f);
        Assert.Equal(0.1f, spike.HeightOf(Vector3.Zero), 0.01f);
        Assert.Equal(t.Heading, spike.Heading, 0.001f);

        // Lifted, it goes up with the arm, and back toward the tractor.
        crane.MoveTo("lift", 45f);
        Run(sim, 3f);
        Assert.Equal(crane.ToMachine("tilt", new Vector3(0f, 0f, 0.14f)).Y, spike.HeightOf(Vector3.Zero), 0.01f);
        Assert.True(spike.HeightOf(Vector3.Zero) > 1.8f);
        // Tilted back, its tines point up.
        crane.MoveTo("tilt", 30f);
        Run(sim, 2f);
        Assert.True(spike.HeightOf(new Vector3(0f, 0f, 1f)) > spike.HeightOf(Vector3.Zero) + 0.4f);

        // Unhitched on the ground, the hitch key takes it back.
        crane.MoveTo("lift", 0f);
        crane.MoveTo("tilt", 0f);
        Run(sim, 4f);
        sim.Machines.Detach(spike);
        sim.Player.Enter(t);
        Assert.Equal("Attach Bale spike", sim.Offers().Of(InputActions.Attach)!.Label);
        sim.Perform(InputActions.Attach);
        Assert.Same(arm, spike.Parent);
    }

    [Fact]
    public void ALoaderArmSitsOnItsConsolesAndOnlyLinkagesAndHeadersLift()
    {
        var sim = TestContent.NewSim();
        var (_, arm) = Loader(sim, new Vector2(269f, 300f));
        var spike = Tool(sim, arm, "bale_spike");
        Assert.Equal((0f, 0f), (arm.Get<Attachable>()!.Lift, spike.Get<Attachable>()!.Lift));
        var mower = sim.Machines.Spawn("mower_3", new Vector2(250f, 300f), 0f);
        var t95 = sim.Machines.Spawn("tractor_95", new Vector2(250f, 303f), 0f);
        Assert.True(sim.Machines.Attach(t95, "rear", mower));
        Assert.Equal(0.45f, mower.Get<Attachable>()!.Lift);
        Assert.Equal(0.45f, mower.HeightOf(Vector3.Zero));
        mower.Get<Attachable>()!.Lowered = true;
        Run(sim, 1f);
        Assert.Equal(0f, mower.Get<Attachable>()!.Lift);
    }

    [Fact]
    public void ABaleSpikeTakesABaleAtItsMiddleAndStacksItOnAnother()
    {
        var sim = TestContent.NewSim();
        var (t, arm) = Loader(sim, new Vector2(269f, 300f));
        var spike = Tool(sim, arm, "bale_spike");
        var fork = spike.Get<Fork>()!;
        var at = spike.LocalToWorld(0f, 0.75f);
        var crane = arm.Get<CraneArm>()!;
        // Lifted, the spikes pass over it; at its middle's height, they go in.
        crane.MoveTo("lift", 25f);
        Run(sim, 2f);
        var bale = sim.Objects.Spawn("round_bale", at, 0f, Farm.PlayerId);
        bale.Content!.Add("hay", 2400f);
        var pallet = sim.Objects.Spawn("pallet", at + new Vector2(0.3f, -0.15f), 0f, Farm.PlayerId);
        Run(sim, 0.5f);
        Assert.Empty(fork.Held);
        crane.MoveTo("lift", 0f);
        Run(sim, 2f);
        Assert.Equal([bale], fork.Held);
        Assert.Same(fork, bale.Holder);
        Assert.Null(pallet.Holder);
        sim.Objects.Remove(pallet);

        // It rides up with the arm, set down on another bale it stays on it.
        crane.MoveTo("lift", 45f);
        Run(sim, 3f);
        var under = sim.Objects.Spawn("round_bale", bale.Position, 0f, Farm.PlayerId);
        sim.Player.Enter(t);
        Assert.Equal("Set the round bale down", sim.Offers().Of(InputActions.Unload)!.Label);
        sim.Perform(InputActions.Unload);
        Assert.Null(bale.Holder);
        Assert.Equal(1.25f, bale.Elevation, 0.01f);
        Run(sim, 1f);
        Assert.Empty(fork.Held);
        // The bale under it taken away, it comes down.
        sim.Objects.Remove(under);
        Run(sim, 0.5f);
        Assert.Equal(0f, bale.Elevation);
    }

    [Fact]
    public void TheGrainMillPutsItsFlourOnPalletsForTheForkToTakeToTheShop()
    {
        var sim = TestContent.NewSim();
        var mill = sim.World.PoiById("grainmill")!;
        Assert.Equal(Farm.PlayerId, mill.FarmId);
        var pit = mill.Trigger("unload")!;
        var trailer = sim.Machines.Spawn("trailer_16", pit.Area.Center + new Vector2(30f, 0f), 0f);
        Assert.Equal(6000f, sim.Pois.Unload(trailer, pit, "wheat", 6000f));
        sim.SkipHours(10);

        // 325 kg of flour an hour, on pallets of a ton in its pallet area.
        var area = mill.Trigger("pallets")!.Area;
        var pallets = sim.Objects.LooseIn(area).ToList();
        Assert.Equal(3250f, pallets.Sum(p => p.Content!.Level), 1f);
        Assert.Equal([1000f, 1000f, 1000f, 250f], pallets.Select(p => p.Content!.Level).OrderDescending());
        Assert.All(pallets, p => Assert.Equal(("flour", Farm.PlayerId), (p.Content!.FillType, p.FarmId)));
        Assert.Equal(-150f, sim.Economy.Ledger.Days.Sum(d => d[MoneyCategory.Production]), 1f);
        Assert.Equal(1000f, mill.Get<Headland.Core.Components.FillUnits>()!.Level("wheat"), 1f);

        // The pallet fork takes a full one to the farm shop, which buys it.
        var (t, arm) = Loader(sim, area.Center + new Vector2(0f, 30f));
        var forks = Tool(sim, arm, "pallet_fork");
        var full = pallets.First(p => p.Content!.Level >= 999f);
        Reach(sim, t, forks, full.Position, full.Heading);
        Run(sim, 0.2f);
        Assert.Same(forks.Get<Fork>(), full.Holder);
        arm.Get<CraneArm>()!.MoveTo("lift", 15f);
        Run(sim, 1.5f);

        var shop = sim.World.PoiById("supplies")!;
        var stand = shop.Trigger("objects")!.Area;
        var sell = shop.Get<SellingStation>()!;
        var money = sim.Economy.Money;
        var expected = 1000f * sim.Pois.Price(sell, "flour");
        Reach(sim, t, forks, stand.Center, stand.Heading);
        Run(sim, 0.2f);
        forks.Get<Fork>()!.SetDown(sim);
        Run(sim, 1f);
        Assert.Equal(money + expected, sim.Economy.Money, 1f);
        Assert.DoesNotContain(full, sim.Objects.All);
        Assert.Contains(sim.Notifications.Items, n => n.Text.StartsWith("Sold a pallet of flour (1,000 kg) to Farm Supplies"));
    }

    [Fact]
    public void AForkCarryingABaleIsSaved()
    {
        var sim = TestContent.NewSim();
        var (_, arm) = Loader(sim, new Vector2(269f, 300f));
        var spike = Tool(sim, arm, "bale_spike");
        var bale = sim.Objects.Spawn("round_bale", spike.LocalToWorld(0.1f, 0.8f), 0.2f, Farm.PlayerId);
        Run(sim, 0.1f);
        Assert.Same(spike.Get<Fork>(), bale.Holder);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var fork = loaded.Machines.ById(spike.Id)!.Get<Fork>()!;
        var held = Assert.Single(fork.Held);
        Assert.Equal(bale.Id, held.Id);
        Assert.Equal(fork.PoseOf(held), spike.Get<Fork>()!.PoseOf(bale), new PoseComparer());
    }

    private sealed class PoseComparer : IEqualityComparer<(Vector3 at, float yaw)>
    {
        public bool Equals((Vector3 at, float yaw) a, (Vector3 at, float yaw) b) => Vector3.Distance(a.at, b.at) < 0.01f && MathF.Abs(a.yaw - b.yaw) < 0.001f;
        public int GetHashCode((Vector3 at, float yaw) p) => 0;
    }

    [Fact]
    public void ForksPalletsAndCraneJointsAreChecked()
    {
        var errors = Assert.Throws<ContentException>(() => TestContent.Modded(new()
        {
            ["machines/test.json"] = """
                [{ "id": "x", "components": {
                     "attacherJoints": { "joints": [ { "id": "tool", "type": "loaderTool", "crane": "boom" } ] },
                     "fork": { "area": { "w": 0 }, "takes": ["sack"], "capacity": 0, "into": -1 } } }]
                """,
            ["objects/test.json"] = """[{ "id": "crate", "components": { "pallet": {} } }]""",
            ["pois/test.json"] = """
                [{ "id": "bakery", "components": {
                     "fillUnits": { "units": [ { "id": "flour", "fillTypes": ["flour", "wheat"] } ] },
                     "productionPoint": { "productions": [ { "id": "a", "inputs": [ { "fillType": "wheat", "amount": 1 } ],
                       "outputs": [ { "fillType": "flour", "amount": 1, "mode": "pallet", "pallet": "round_bale" } ] } ] } } }]
                """,
        })).Message;
        Assert.Contains("machine 'x' attacherJoints: joint 'tool': its crane joint 'boom' is not one of its craneArm's", errors);
        Assert.Contains("machine 'x' fork: area: needs w and d > 0", errors);
        Assert.Contains("machine 'x' fork: unknown kind 'sack' (bale, pallet)", errors);
        Assert.Contains("machine 'x' fork: capacity must be >= 1", errors);
        Assert.Contains("machine 'x' fork: into must be >= 0", errors);
        Assert.Contains("object 'crate' pallet: a pallet needs fillUnits with one unit: what it holds", errors);
        Assert.Contains("poi 'bakery' productionPoint: production 'a': output 'flour' goes on pallets: the production point needs a pallets area", errors);
        Assert.Contains("poi 'bakery' productionPoint: production 'a': output 'flour' needs the pallet it goes on (objects/, with a pallet)", errors);
    }
}
