using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
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

    [Fact]
    public void HeadlightsComeOnAtNightAndWhenTheDriverSwitchesThem()
    {
        var sim = ShedSim();
        var tractor = sim.Machines.Spawn("tractor_125", new Vector2(10f, 50f), 0f);
        var lights = tractor.Get<Lights>()!;
        var head = Array.FindIndex(lights.Def.Lamps, l => l.Type == "head");
        At(sim, 12);
        Assert.False(lights.Lit(head));
        Assert.True(lights.Switch("head", true));
        sim.Tick(Dt);
        Assert.True(lights.Lit(head));
        lights.Switch("head", false);
        At(sim, 23);
        Assert.True(lights.Lit(head));
        Assert.False(lights.Switch("beacon", true));
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
                   { "id": "wing", "fold": true },
                   { "id": "gate", "trigger": { "w": 4 } } ] },
                 "hotspots": { "spots": [ { "x": 1 } ] } } }]
            """)).Message;
        Assert.Contains("poi 'test_bad' lights: lamp 'a': unknown switch 'sometimes'", e);
        Assert.Contains("lamp 'b': the hours switch needs hours", e);
        Assert.Contains("lamp 'c': the trigger switch needs a trigger", e);
        Assert.Contains("lamp 'd': hours go with the hours switch", e);
        Assert.Contains("lamp 'e' trigger: needs w and d > 0", e);
        Assert.Contains("poi 'test_bad' animatedParts: part 'wing': only machines fold", e);
        Assert.DoesNotContain("part 'gate'", e);
        Assert.Contains("poi 'test_bad' hotspots: each spot needs an icon", e);

        var by = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "test_bad", "name": "Bad", "components": {
                 "lights": { "lamps": [ { "switch": "trigger", "trigger": { "by": "cows" } } ] } } }]
            """)).Message;
        Assert.Contains("a head lamp trigger: by must be anyone, farmer, machines", by);
    }
}
