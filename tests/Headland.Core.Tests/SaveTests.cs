using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Pois.Components;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class SaveTests
{
    private const float Dt = 1f / 60f;

    private static void Run(Simulation sim, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt) sim.Tick(Dt);
    }

    /// <summary>A game in the middle of things: a helper harvesting, grain in the tank, a parcel bought, days passed.</summary>
    private static Simulation BusyGame()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(30);
        var combine = sim.Machines.All.First(m => m.Def.Id == "combine_7");
        var f2 = sim.World.FieldById(2)!.Shape;
        sim.Player.Enter(combine);
        sim.Machines.Teleport(combine, f2.Min + new Vector2(3f, -8f), 0f);
        sim.HireHelper(combine, FieldInfo.Rect(2, f2.Min.X, f2.Min.Y, 18f, f2.Size.Y), maxLanes: 2);
        Run(sim, 25f);
        sim.Farms.SetOwner(sim.World.FarmlandById(5)!, Farm.PlayerId);
        return sim;
    }

    private static Simulation RoundTrip(Simulation sim, ContentDatabase? content = null) =>
        SaveGame.Load(content ?? sim.Content, SaveGame.Capture(sim, "test")).Sim;

    private static void AssertSameWorld(Simulation a, Simulation b)
    {
        Assert.Equal(a.Clock.TotalSeconds, b.Clock.TotalSeconds);
        Assert.Equal(a.Weather.Temperature, b.Weather.Temperature);
        Assert.Equal(a.Weather.GroundWetness, b.Weather.GroundWetness);
        Assert.Equal(a.Economy.Money, b.Economy.Money);
        var (la, lb) = (a.World.Layers, b.World.Layers);
        Assert.Equal(la.Ground, lb.Ground);
        Assert.Equal(la.Crop, lb.Crop);
        Assert.Equal(la.Stage, lb.Stage);
        Assert.Equal(la.Progress, lb.Progress);
        Assert.Equal(la.Moisture, lb.Moisture);
        Assert.Equal(la.Nitrogen, lb.Nitrogen);
        Assert.Equal(la.Health, lb.Health);
        Assert.Equal(la.WorkAngle, lb.WorkAngle);
        Assert.Equal(la.Chill, lb.Chill);
        Assert.Equal(a.Machines.All.Select(m => (m.Id, m.Def.Id, m.Position, m.Heading, m.Speed, m.Parent?.Id, m.Get<Attachable>()?.Lowered)),
            b.Machines.All.Select(m => (m.Id, m.Def.Id, m.Position, m.Heading, m.Speed, m.Parent?.Id, m.Get<Attachable>()?.Lowered)));
        Assert.Equal(a.Machines.All.SelectMany(m => m.FillUnits.Select(u => (u.FillType, u.Level))),
            b.Machines.All.SelectMany(m => m.FillUnits.Select(u => (u.FillType, u.Level))));
    }

    [Fact]
    public void ALoadedGameIsTheSavedGame()
    {
        var sim = BusyGame();
        var loaded = RoundTrip(sim);
        AssertSameWorld(sim, loaded);

        Assert.Equal(sim.Clock.Date, loaded.Clock.Date);
        Assert.Equal(sim.Weather.Forecast(sim.Clock.DayIndex, 6).Select(d => (d.TempMean, d.PrecipMm)),
            loaded.Weather.Forecast(loaded.Clock.DayIndex, 6).Select(d => (d.TempMean, d.PrecipMm)));
        Assert.Equal(sim.Statistics.Harvested, loaded.Statistics.Harvested);
        Assert.Equal(sim.Statistics.DaysPlayed, loaded.Statistics.DaysPlayed);
        Assert.Equal(Farm.PlayerId, loaded.World.FarmlandById(5)!.FarmId);
        Assert.Equal(sim.RealTime, loaded.RealTime);

        var combine = loaded.Machines.All.First(m => m.Def.Id == "combine_7");
        Assert.Same(combine, loaded.Player.Vehicle);
        Assert.True(combine.Unit("tank")!.Level > 100f);
        var (h1, h2) = ((FieldWorkController)sim.Player.Vehicle!.Get<Drivable>()!.Controller!, Assert.IsType<FieldWorkController>(combine.Get<Drivable>()!.Controller));
        // The route itself is kept: planned again, it would start from where the combine is now.
        Assert.Equal(h1.Path.Points, h2.Path.Points);
        Assert.Equal(h1.Path.Segments, h2.Path.Segments);
        Assert.Equal(h1.Driver.Index, h2.Driver.Index);
        Assert.Equal(h1.Margin, h2.Margin);
        Assert.Equal((h1.WagePerHour, h1.WorkedSeconds, h1.WagesPaid), (h2.WagePerHour, h2.WorkedSeconds, h2.WagesPaid));

        // New machines never reuse an id.
        Assert.Equal(sim.Machines.Spawn("cultivator_3", Vector2.Zero, 0f).Id, loaded.Machines.Spawn("cultivator_3", Vector2.Zero, 0f).Id);
    }

    [Fact]
    public void ALoadedGameGoesOnExactlyAsTheOriginal()
    {
        var sim = BusyGame();
        var loaded = RoundTrip(sim);
        foreach (var s in new[] { sim, loaded })
        {
            Run(s, 20f);
            s.SkipHours(5);
        }
        AssertSameWorld(sim, loaded);
    }

    [Fact]
    public void AHelperFromASaveWithoutItsRoutePlansAgainFromWhereItIs()
    {
        var sim = BusyGame();
        var file = SaveGame.Capture(sim, "test");
        // Older saves didn't keep the route.
        var state = JsonSerializer.Deserialize<SaveState>(file.State, SaveGame.Json)!;
        foreach (var m in state.Machines)
            if (m.Helper is { } h) h.Route = [];
        var loaded = SaveGame.Load(sim.Content, file with { State = JsonSerializer.SerializeToUtf8Bytes(state, SaveGame.Json) }).Sim;

        var combine = loaded.Machines.All.First(m => m.Def.Id == "combine_7");
        var helper = Assert.IsType<FieldWorkController>(combine.Get<Drivable>()!.Controller);
        // Planned again from where the combine is: on along the lane it was harvesting.
        Assert.Equal(0, helper.Driver.Index);
        Assert.Equal(PathSegment.Work, helper.Path.Segments[0]);
        Assert.InRange(helper.Path.Points[0].X, combine.Position.X - 0.2f, combine.Position.X + 0.2f);
    }

    [Fact]
    public void AFormat1SaveHandsItsMachineStateToTheirComponents()
    {
        // The machines of BusyGame() as version 0.7.0 saved them, before components.
        var file = SaveGame.Capture(BusyGame(), "test");
        var state = JsonNode.Parse(file.State)!.AsObject();
        var format1 = JsonNode.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "tests", "Headland.Core.Tests", "Fixtures", "machines-format1.json")))!.AsArray();
        // Format 1 kept nothing of the motor (fuel burned but not yet taken from the tank): the game it matches is the
        // same one without that, and without the machines the map has gained since.
        var ids = format1.Select(m => (int)m!["id"]!).ToHashSet();
        var machines = state["machines"]!.AsArray();
        foreach (var m in machines.Where(m => !ids.Contains((int)m!["id"]!)).ToList()) machines.Remove(m);
        foreach (var m in machines) m!["components"]!.AsObject().Remove("motor");
        var sim = SaveGame.Load(TestContent.Content, file with { State = Encoding.UTF8.GetBytes(state.ToJsonString()) }).Sim;
        state["machines"] = format1;
        file.Meta.Format = 1;
        var loaded = SaveGame.Load(sim.Content, file with { State = Encoding.UTF8.GetBytes(state.ToJsonString()) });
        Assert.Empty(loaded.Warnings);

        AssertSameWorld(sim, loaded.Sim);
        var (combine, header, seeder) = (Find(loaded.Sim, "combine_7"), Find(loaded.Sim, "header_grain_6"), Find(loaded.Sim, "seeder_3"));
        Assert.True(combine.Get<Thresher>()!.On);
        Assert.Equal(269.50943f, combine.Unit("tank")!.Level, 3);
        Assert.Equal(60.838486f, combine.Get<RunningGear>()!.Distance, 3);
        Assert.Equal((true, 1f), (header.Get<Attachable>()!.Lowered, header.Get<Attachable>()!.LowerAnim));
        Assert.Equal(sim.Content.CropIndex("canola"), seeder.Get<WorkAreas>()!.Crop);
        Assert.IsType<FieldWorkController>(combine.Get<Drivable>()!.Controller);

        // It goes on as the game it was saved from.
        foreach (var s in new[] { sim, loaded.Sim }) Run(s, 10f);
        AssertSameWorld(sim, loaded.Sim);
    }

    private static Machine Find(Simulation sim, string def) => sim.Machines.All.First(m => m.Def.Id == def);

    [Fact]
    public void AFormat2SaveHandsItsPoiStateToTheirComponents()
    {
        // The POIs as version 0.13.0 saved them: grain in the farm silo, wheat and flour at the mill with a cycle half
        // done, and the elevator's demand lowered by sales, with canola in high demand.
        var file = SaveGame.Capture(TestContent.NewSim(), "test");
        var state = JsonNode.Parse(file.State)!.AsObject();
        state["pois"] = JsonNode.Parse(File.ReadAllText(Path.Combine(TestContent.RepoRoot, "tests", "Headland.Core.Tests", "Fixtures", "pois-format2.json")));
        file.Meta.Format = 2;
        var loaded = SaveGame.Load(TestContent.Content, file with { State = Encoding.UTF8.GetBytes(state.ToJsonString()) });
        Assert.Empty(loaded.Warnings);

        var pois = loaded.Sim.World;
        var silo = pois.PoiById("silo")!.Get<FillUnits>()!;
        Assert.Equal((12_000f, 3_500f, 0f), (silo.Level("wheat"), silo.Level("barley"), silo.Level("corn")));
        var mill = pois.PoiById("mill")!;
        Assert.Equal((21_000f, 4_000f), (mill.Get<FillUnits>()!.Level("wheat"), mill.Get<FillUnits>()!.Level("flour")));
        Assert.Equal(0.9916f, mill.Get<SellingStation>()!.DemandOf("wheat"), 4);
        var elevator = pois.PoiById("elevator")!.Get<SellingStation>()!;
        Assert.Equal((0.9f, 0.95f, 1f), (elevator.DemandOf("wheat"), elevator.DemandOf("barley"), elevator.DemandOf("corn")));
        Assert.Equal(new HighDemand("canola", 1.3f, 5), elevator.HighDemand);

        // Saved again, it's the same game.
        var again = SaveGame.Load(TestContent.Content, SaveGame.Capture(loaded.Sim, "test")).Sim;
        Assert.Equal(12_000f, again.World.PoiById("silo")!.Get<FillUnits>()!.Level("wheat"));
        Assert.Equal(elevator.HighDemand, again.World.PoiById("elevator")!.Get<SellingStation>()!.HighDemand);
    }

    [Fact]
    public void SavesSurviveContentChanges()
    {
        var sim = TestContent.NewSim();
        var content = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        content.Crops.Reverse();
        content.Machines.Remove("seeder_3");

        var loaded = SaveGame.Load(content, SaveGame.Capture(sim, "test"));
        var cell = loaded.Sim.World.CellIndex(500, 300); // field 2, ripe wheat
        Assert.Equal("wheat", content.Crops[loaded.Sim.World.Layers.Crop[cell] - 1].Id);
        Assert.DoesNotContain(loaded.Sim.Machines.All, m => m.Def.Id == "seeder_3");
        Assert.Contains("Machine 'seeder_3' no longer exists: it was removed", loaded.Warnings);
    }

    [Fact]
    public void SavesFromNewerVersionsOrOtherMapsAreRefused()
    {
        var sim = TestContent.NewSim();
        var file = SaveGame.Capture(sim, "test");
        file.Meta.Format = SaveGame.Format + 1;
        Assert.Contains("newer version", Assert.Throws<SaveException>(() => SaveGame.Load(sim.Content, file)).Message);

        var content = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        content.Maps.Remove("default");
        Assert.Contains("'default' is not installed",
            Assert.Throws<SaveException>(() => SaveGame.Load(content, SaveGame.Capture(sim, "test"))).Message);
    }

    [Fact]
    public void SlotsAreZipsListedNewestFirst()
    {
        var dir = Directory.CreateTempSubdirectory("headland-saves-");
        try
        {
            var store = new SaveStore(Path.Combine(dir.FullName, "saves"));
            Assert.Empty(store.List());
            var sim = TestContent.SmallSim();
            var older = SaveGame.Capture(sim, "0.3.0");
            older.Meta.SavedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            store.Write("farm-1", older);
            sim.SkipHours(24);
            store.Write("autosave", SaveGame.Capture(sim, "0.3.0"));
            File.WriteAllText(Path.Combine(store.Directory, "junk.zip"), "not a zip");

            Assert.Equal(["autosave", "farm-1"], store.List().Select(s => s.Name));
            Assert.Equal("0.3.0", store.List()[0].Meta.GameVersion);
            Assert.DoesNotContain(Directory.GetFiles(store.Directory), f => f.EndsWith(".tmp"));

            var loaded = SaveGame.Load(sim.Content, store.Read("autosave")).Sim;
            Assert.Equal(sim.Clock.TotalSeconds, loaded.Clock.TotalSeconds);
            Assert.Equal(sim.World.Layers.Moisture, loaded.World.Layers.Moisture);

            store.Delete("farm-1");
            Assert.False(store.Exists("farm-1"));
            Assert.Throws<SaveException>(() => store.Read("farm-1"));
            Assert.Throws<ArgumentException>(() => store.PathOf("../evil"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
