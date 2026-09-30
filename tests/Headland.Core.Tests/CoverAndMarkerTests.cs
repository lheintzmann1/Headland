using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class CoverAndMarkerTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    private static string? Hint(Simulation sim, string action) => sim.Offers().Of(action)?.Label;

    private const string Machines = """
        [
        {
          "id": "test_covered_trailer", "name": "Covered Tipper", "size": { "length": 6.2, "width": 2.55, "height": 2.7, "centerZ": 0.6 },
          "components": {
            "runningGear": { "axles": [ { "z": 0.65, "track": 2.04 }, { "z": -0.65, "track": 2.04 } ] },
            "attachable": { "type": "drawbar", "mode": "trailed", "z": 4.4 },
            "fillUnits": { "units": [ { "id": "main", "capacity": 16000, "fillTypeCategories": ["grain"] } ] },
            "tipper": { "fillUnit": "main", "ratePerSecond": 450 },
            "animatedParts": { "parts": [ { "id": "tarp", "rotationDeg": [0, 0, -170], "seconds": 3 } ] },
            "cover": { "covers": [ { "parts": ["tarp"], "fillUnits": ["main"] } ], "words": { "cover": ["Roll tarp open", "Roll tarp shut"] } }
          }
        },
        {
          "id": "test_folding_tiller", "name": "Folding Tiller", "size": { "length": 2, "width": 6, "height": 1.4, "centerZ": -1 },
          "components": {
            "attachable": { "type": "threePoint", "mode": "mounted", "lowerable": true },
            "animatedParts": { "startFolded": true, "parts": [
              { "id": "wing", "rotationDeg": [0, 0, 90], "seconds": 1, "key": "fold" },
              { "id": "markerL", "rotationDeg": [0, 0, -120], "seconds": 1 },
              { "id": "markerR", "rotationDeg": [0, 0, 120], "seconds": 1 } ] },
            "ridgeMarker": { "markers": [
              { "name": "left", "parts": ["markerL"], "x": 6, "z": -1 },
              { "name": "right", "parts": ["markerR"], "x": -6, "z": -1 } ] },
            "workAreas": { "areas": [ { "type": "cultivator", "width": 6, "length": 1, "z": -1.05 } ] }
          }
        }
        ]
        """;

    private static Simulation Sim()
    {
        var sim = Simulation.Create(TestContent.WithMachines(Machines));
        TestContent.OwnField4(sim);
        return sim;
    }

    private static (Machine tractor, Machine seeder) Seeder(Simulation sim, Vector2 at)
    {
        var t = sim.Machines.Spawn("tractor_125", at, 0f);
        var seeder = sim.Machines.Spawn("seeder_3", at - new Vector2(0f, 5f), 0f);
        Assert.True(sim.Machines.Attach(t, "drawbar", seeder));
        return (t, seeder);
    }

    [Fact]
    public void ALidOpensByItselfAtTheSeedShopAndNothingFillsItClosed()
    {
        var sim = Sim();
        var shop = sim.World.PoiById("supplies")!.Trigger("fill")!;
        var (t, seeder) = Seeder(sim, shop.Area.Center + new Vector2(0f, 40f));
        var lid = seeder.Get<Cover>()!;
        var seed = seeder.Unit("seed")!;
        seed.Remove(seed.Level);
        sim.Player.Enter(t);

        // Closed, it takes nothing: a pipe over it finds nowhere to pour.
        Assert.Equal((0, "Open cover"), (lid.State, Hint(sim, InputActions.Cover)));
        Assert.True(seeder.ClosedOver(seed));
        Assert.Null(sim.Machines.FindReceiver(seeder.Footprint.Center, "seeds", t));

        // Parked at the shop, it opens by itself; driving off, it closes again.
        sim.Machines.Teleport(t, shop.Area.Center + new Vector2(0f, 3f), 0f);
        sim.Tick(Dt);
        Assert.Equal((1, true), (lid.State, lid.Auto));
        Run(sim, 1.6f);
        Assert.Equal(1f, seeder.Get<AnimatedParts>()!.Part("lid")!.Position);
        sim.Activate(t);
        Assert.Equal(seed.Capacity, seed.Level);
        sim.Machines.Teleport(t, shop.Area.Center + new Vector2(0f, 40f), 0f);
        sim.Tick(Dt);
        Assert.Equal(0, lid.State);

        // Opened by the driver, it stays open; closed by the driver at the shop, the shop can't fill it.
        sim.Perform(InputActions.Cover);
        Assert.Equal((1, "Close cover"), (lid.State, Hint(sim, InputActions.Cover)));
        seed.Remove(500f);
        Assert.Same(seed, sim.Machines.FindReceiver(seeder.Footprint.Center, "seeds", t));
        sim.Machines.Teleport(t, shop.Area.Center + new Vector2(0f, 3f), 0f);
        sim.Tick(Dt);
        sim.Machines.Teleport(t, shop.Area.Center + new Vector2(0f, 40f), 0f);
        sim.Tick(Dt);
        Assert.Equal(1, lid.State);
        sim.Machines.Teleport(t, shop.Area.Center + new Vector2(0f, 3f), 0f);
        sim.Tick(Dt);
        sim.Perform(InputActions.Cover);
        sim.Activate(t);
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Open the cover of {seeder.Def.Name} first");
        Assert.Equal(seed.Capacity - 500f, seed.Level);

        // Saved as it is.
        sim.Perform(InputActions.Cover);
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(seeder.Id)!;
        Assert.Equal((1, 1f), (loaded.Get<Cover>()!.State, loaded.Get<AnimatedParts>()!.Part("lid")!.Position));
    }

    [Fact]
    public void ATarpOpensToTipAndSaysItInItsWords()
    {
        var sim = Sim();
        var t = sim.Machines.Spawn("tractor_95", new Vector2(447f, 230f), MathF.PI / 2f);
        var trailer = sim.Machines.Spawn("test_covered_trailer", new Vector2(441f, 230f), MathF.PI / 2f);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", 4000f);
        sim.Player.Enter(t);
        var tarp = trailer.Get<Cover>()!;

        Assert.Equal("Roll tarp open", Hint(sim, InputActions.Cover));
        sim.Perform(InputActions.Unload);
        sim.Tick(Dt);
        Assert.Equal((1, "Roll tarp shut"), (tarp.State, Hint(sim, InputActions.Cover)));
        for (var s = 0f; s < 30f && !trailer.Unit("main")!.IsEmpty; s += Dt) sim.Tick(Dt);
        Assert.True(trailer.Unit("main")!.IsEmpty);
        // Opened to tip, it stays open.
        Assert.Equal(1, tarp.State);
    }

    [Fact]
    public void RidgeMarkersStepLeftRightAndUpAndDrawTheNextPassOnlyLowered()
    {
        var sim = Sim();
        var field = sim.World.FieldById(4)!;
        var (t, seeder) = Seeder(sim, new Vector2(269f, 285f));
        var markers = seeder.Get<RidgeMarker>()!;
        var arm = seeder.Get<AnimatedParts>()!.Part("markerL")!;
        sim.Player.Enter(t);

        Assert.Equal("Ridge marker left", Hint(sim, InputActions.MoveParts));
        sim.Perform(InputActions.MoveParts);
        Assert.Equal(("left", "Ridge marker right"), (markers.Down?.Name, Hint(sim, InputActions.MoveParts)));
        // Raised, the marker stands half down, and draws nothing.
        var before = (byte[])sim.World.Layers.Ground.Clone();
        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 1f } };
        Run(sim, 4f);
        Assert.Equal(0.6f, arm.Position, 3);
        Assert.Equal(before, sim.World.Layers.Ground);

        // Lowered, it goes all the way down and draws a line in the middle of the next pass, 3 m to the side.
        t.Get<Drivable>()!.Controller = sim.Player.Controls;
        sim.Perform(InputActions.Lower);
        Run(sim, 2f);
        Assert.Equal(1f, arm.Position);
        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 1f } };
        Run(sim, 6f);
        var drawn = Enumerable.Range(0, before.Length).Where(i => sim.World.Layers.Ground[i] != before[i]).ToList();
        Assert.True(drawn.Count > 20, $"cells drawn: {drawn.Count}");
        Assert.All(drawn, i =>
        {
            var local = MathUtil.WorldToLocal(seeder.Position, seeder.Heading, sim.World.CellCenter(i % sim.World.CellsX, i / sim.World.CellsX));
            Assert.InRange(local.X, 2.5f, 3.5f);
            Assert.True(sim.World.Layers.FieldId[i] == field.Id);
            Assert.Equal(before[i] == (byte)GroundType.Plowed ? GroundType.Cultivated : GroundType.Plowed, (GroundType)sim.World.Layers.Ground[i]);
        });

        // Right, then up; saved as it is.
        sim.Perform(InputActions.MoveParts);
        Assert.Equal(("right", "Ridge markers up"), (markers.Down?.Name, Hint(sim, InputActions.MoveParts)));
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(seeder.Id)!;
        Assert.Equal("right", loaded.Get<RidgeMarker>()!.Down?.Name);
        sim.Perform(InputActions.MoveParts);
        Assert.Null(markers.Down);

        // A helper leaves them up.
        sim.Perform(InputActions.MoveParts);
        sim.Player.Exit(sim);
        sim.HireHelper(t, field, maxLanes: 1);
        Assert.Null(markers.Down);
    }

    [Fact]
    public void FoldingPutsTheMarkersUpAndTheyStayUpFolded()
    {
        var sim = Sim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var tiller = sim.Machines.Spawn("test_folding_tiller", new Vector2(269f, 278f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", tiller));
        sim.Player.Enter(t);
        var markers = tiller.Get<RidgeMarker>()!;

        Assert.Null(Hint(sim, InputActions.MoveParts));
        sim.Perform(InputActions.Fold);
        Run(sim, 1.1f);
        sim.Perform(InputActions.MoveParts);
        Assert.Equal("left", markers.Down?.Name);
        sim.Perform(InputActions.Fold);
        sim.Tick(Dt);
        Assert.Null(markers.Down);
        Assert.Null(Hint(sim, InputActions.MoveParts));
    }

    [Fact]
    public void CoversAndMarkersAreCheckedAgainstTheirMachine()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "size": { "length": 2, "width": 3, "height": 1, "centerZ": 0 },
               "components": {
                 "attachable": {},
                 "fillUnits": { "units": [ { "id": "seed", "fillTypes": ["seeds"] } ] },
                 "animatedParts": { "parts": [ { "id": "lid" }, { "id": "arm", "key": "fold" } ] },
                 "cover": { "covers": [ { "parts": ["lid", "flap"], "fillUnits": ["hopper"] } ] },
                 "ridgeMarker": { "key": "lower", "markers": [ { "name": "left", "parts": ["arm", "lid"] } ] } } }]
            """)).Message;
        Assert.Contains("machine 'x' cover: cover 1: part 'flap' missing in animatedParts", bad);
        Assert.Contains("machine 'x' cover: cover 1: fill unit 'hopper' missing", bad);
        Assert.Contains("machine 'x' ridgeMarker: key must be move_parts or turn_on", bad);
        Assert.Contains("machine 'x' ridgeMarker: needs an attachable that lowers", bad);
        Assert.Contains("machine 'x' ridgeMarker: needs workAreas", bad);
        Assert.Contains("machine 'x' ridgeMarker: marker 'left': part 'lid' is moved by the cover", bad);
        Assert.Contains("machine 'x' animatedParts: part 'arm': the ridgeMarker moves it; it has no key, middle, trigger or support of its own", bad);
    }
}
