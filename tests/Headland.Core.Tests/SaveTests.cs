using System.Numerics;
using System.Text.Json;
using Headland.Core.Content;
using Headland.Core.Machines;
using Headland.Core.Ownership;
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
        Assert.Equal(a.Machines.All.Select(m => (m.Id, m.Def.Id, m.Position, m.Heading, m.Speed, m.Parent?.Id, m.Lowered)),
            b.Machines.All.Select(m => (m.Id, m.Def.Id, m.Position, m.Heading, m.Speed, m.Parent?.Id, m.Lowered)));
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
        var (h1, h2) = ((FieldWorkController)sim.Player.Vehicle!.Controller!, Assert.IsType<FieldWorkController>(combine.Controller));
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
        var helper = Assert.IsType<FieldWorkController>(combine.Controller);
        // Planned again from where the combine is: on along the lane it was harvesting.
        Assert.Equal(0, helper.Driver.Index);
        Assert.Equal(PathSegment.Work, helper.Path.Segments[0]);
        Assert.InRange(helper.Path.Points[0].X, combine.Position.X - 0.2f, combine.Position.X + 0.2f);
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
