using Headland.Core.Content;
using Headland.Core.Saves;
using Headland.Core.Time;
using Headland.Core.Weather;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class ContentTests
{
    [Fact]
    public void ShippedDataLoadsAndValidates()
    {
        var db = TestContent.Content;
        Assert.Empty(db.Validate());
        Assert.Equal(4, db.Crops.Count);
        Assert.Contains("combine_7", db.Machines.Keys);
        Assert.Contains("grain_elevator", db.Pois.Keys);
        Assert.Equal(3, db.Soils.Count);
    }

    [Fact]
    public void InvalidReferencesAreReported()
    {
        // Content of its own: games of other tests run on the shared one meanwhile.
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Crops[0].FillType = "unobtainium";
        Assert.Contains(db.Validate(), e => e.Contains("unobtainium"));
    }

    [Fact]
    public void DuplicateFieldNumbersAreReported()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["dup"] = new MapDef
        {
            Id = "dup",
            Fields = [new FieldDef { Id = 3, W = 10, H = 10 }, new FieldDef { Id = 3, X = 20, W = 10, H = 10 }],
        };
        Assert.Contains(db.Validate(), e => e.Contains("field 3 is defined more than once"));
    }

    [Fact]
    public void AMachineLeftOutGoesWithItsPlacesAndLeaseSets()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        Assert.Equal(
            [
                "removed from map 'default' (1 place)",
                "the cultivator_3 hitched to it on map 'default' stands unhitched",
                "removed from the lease sets of 'cultivate' contracts",
                "removed from the lease sets of 'sow' contracts",
            ],
            db.RemoveMachine("tractor_125"));
        Assert.Empty(db.RemoveMachine("tractor_125"));
        Assert.DoesNotContain("tractor_125", db.Machines.Keys);
        Assert.Empty(db.Validate());

        // The cultivator stands where the tractor stood, and the other hitches follow their machines' new places.
        var placed = db.Maps["default"].Machines;
        Assert.Equal(("cultivator_3", 152f, 206f, 90f), (placed[0].Def, placed[0].X, placed[0].Z, placed[0].HeadingDeg));
        Assert.Equal(new int?[] { null, null, 1, null, 3, null, null }, placed.Select(p => p.AttachToIndex));
        var sim = Simulation.Create(db);
        Assert.DoesNotContain(sim.Machines.All, m => m.Def.Id == "tractor_125");
        Assert.Null(sim.Machines.All.Single(m => m.Def.Id == "cultivator_3").Parent);
        Assert.Equal("combine_7", sim.Machines.All.Single(m => m.Def.Id == "header_grain_6").Parent?.Def.Id);

        // A game saved with it loads without it.
        var loaded = SaveGame.Load(db, SaveGame.Capture(TestContent.NewSim(), "test"));
        Assert.Contains("Machine 'tractor_125' no longer exists: it was removed", loaded.Warnings);
        Assert.Contains("Tiller 300 could not be re-attached", loaded.Warnings);
    }

    [Fact]
    public void WorldGenIsDeterministic()
    {
        var a = WorldGen.Generate(TestContent.Content.Map, TestContent.Content);
        var b = WorldGen.Generate(TestContent.Content.Map, TestContent.Content);
        Assert.Equal(a.Height.Heights, b.Height.Heights);
        Assert.Equal(a.Layers.Soil, b.Layers.Soil);
        Assert.Equal(a.Trees.Count, b.Trees.Count);
        Assert.Equal(3, a.Networks.Count);
        Assert.Equal(16f, a.TileSize);
    }

    [Fact]
    public void NetworkBandsGetTheirGroundTypeOnTheSharedGrid()
    {
        var world = WorldGen.Generate(TestContent.Content.Map, TestContent.Content);
        var roads = world.Networks.Single(n => n.Id == "roads");
        Assert.All(roads.Tiles, t => Assert.NotEqual(0, t.Mask));
        // The crossing of the two main roads is a 4-way tile.
        Assert.Equal(15, roads.Tiles.Single(t => t.X == 12 && t.Z == 15).Mask);

        // Asphalt only in the middle band of a straight tile; the verge keeps its ground.
        var center = new System.Numerics.Vector2(3.5f * 16f, 15.5f * 16f);
        Assert.Equal(GroundType.Road, world.GroundAt(center));
        Assert.NotEqual(GroundType.Road, world.GroundAt(center + new System.Numerics.Vector2(0f, 6f)));

        // Dead-end track tiles are drawn as straights along their run.
        var track = world.Networks.Single(n => n.Id == "tracks").Tiles.Single(t => t.X == 8 && t.Z == 12);
        Assert.Equal(8, track.Mask);
        Assert.Equal(2 | 8, WorldGen.EffectiveMask(track));

        // The creek is water and sits lower than its banks.
        var creek = new System.Numerics.Vector2(21.5f * 16f, 3.5f * 16f);
        Assert.Equal(GroundType.Water, world.GroundAt(creek));
        Assert.True(world.HeightAt(creek) < world.HeightAt(creek + new System.Numerics.Vector2(8f, 0f)));
    }
}

public class CalendarTests
{
    [Fact]
    public void DatesRollOverMonthsAndYears()
    {
        var cal = new Calendar(3);
        Assert.Equal(new GameDate(1, 1, 1), cal.DateOfDay(0));
        Assert.Equal(new GameDate(1, 1, 3), cal.DateOfDay(2));
        Assert.Equal(new GameDate(1, 2, 1), cal.DateOfDay(3));
        Assert.Equal(new GameDate(1, 12, 3), cal.DateOfDay(35));
        Assert.Equal(new GameDate(2, 1, 1), cal.DateOfDay(36));
        Assert.Equal(40, cal.DayIndexOf(cal.DateOfDay(40)));
    }

    [Theory]
    [InlineData(1, Season.Winter)]
    [InlineData(4, Season.Spring)]
    [InlineData(7, Season.Summer)]
    [InlineData(10, Season.Autumn)]
    public void SeasonsFollowMonths(int month, Season expected) => Assert.Equal(expected, Calendar.SeasonOf(month));

    [Fact]
    public void ClockCountsCrossedHours()
    {
        var clock = new GameClock(new Calendar(3), new GameDate(1, 8, 1), 10f) { TimeScale = 60f };
        Assert.Equal(0, clock.Advance(59f)); // 59 game minutes
        Assert.Equal(1, clock.Advance(2f));  // crosses 11:00
        Assert.Equal(24, clock.Skip(24 * 3600));
        Assert.Equal(new GameDate(1, 8, 2), clock.Date);
        // August 2nd, ~11:01 with 3-day months: 1 + (21 + 1 + 11/24) / 3.
        Assert.InRange(clock.MonthFloat, 8.48f, 8.49f);
    }

    [Fact]
    public void CompressedCalendarScalesAgronomicDays() => Assert.Equal(30.4f / 3f, new Calendar(3).RealDaysPerGameDay, 3);
}

public class WeatherTests
{
    [Fact]
    public void SameSeedGivesSameForecast()
    {
        var cal = new Calendar(3);
        var a = new WeatherSystem(TestContent.Content.Climate, cal, 7);
        var b = new WeatherSystem(TestContent.Content.Climate, cal, 7);
        var fa = a.Forecast(20, 10).Select(d => (d.TempMean, d.PrecipMm)).ToList();
        var fb = b.Forecast(20, 10).Select(d => (d.TempMean, d.PrecipMm)).ToList();
        Assert.Equal(fa, fb);
    }

    [Fact]
    public void TemperaturesFollowTheSeasons()
    {
        var cal = new Calendar(3);
        var w = new WeatherSystem(TestContent.Content.Climate, cal, 11);
        var years = 4;
        float Mean(int month) => Enumerable.Range(0, years)
            .SelectMany(y => Enumerable.Range(0, 3).Select(d => w.GetDay(cal.DayIndexOf(new GameDate(y + 1, month, d + 1))).TempMean))
            .Average();
        Assert.InRange(Mean(7), 13f, 24f);
        Assert.InRange(Mean(1), -4f, 7f);
        Assert.True(Mean(7) > Mean(1) + 10f);
    }

    [Fact]
    public void YearHasPrecipitationAndDryDays()
    {
        var cal = new Calendar(3);
        var w = new WeatherSystem(TestContent.Content.Climate, cal, 3);
        var days = w.Forecast(0, cal.DaysPerYear * 2).ToList();
        Assert.Contains(days, d => d.PrecipMm > 0);
        Assert.Contains(days, d => d.PrecipMm == 0);
        var yearly = days.Sum(d => d.PrecipMm) / 2f;
        Assert.InRange(yearly, 350f, 1300f);
    }

    [Fact]
    public void RainWetsTheGroundAndSnowMeltsIntoSoil()
    {
        var w = new WeatherSystem(TestContent.Content.Climate, new Calendar(3), 1);
        w.ForceSnapshot(10f, rainMm: 5f);
        w.TickHour();
        Assert.True(w.GroundWetness > 0.3f);

        w.ForceSnapshot(-5f, snowMm: 10f);
        w.TickHour();
        Assert.True(w.SnowCover > 0.9f);
        w.ForceSnapshot(8f);
        w.TickHour();
        Assert.True(w.MeltMm > 0f);
    }
}

public class WeatherStatisticsTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void SummerMeansVaryModeratelyBetweenYears()
    {
        var cal = new Calendar(3);
        var climate = TestContent.Content.Climate;
        var anomalies = new List<double>();
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var w = new WeatherSystem(climate, cal, seed);
            var days = Enumerable.Range(cal.DayIndexOf(new GameDate(2, 6, 1)), 9).ToList(); // June..August, year 2
            var mean = days.Average(d => w.GetDay(d).TempMean);
            var normal = days.Average(d =>
            {
                var date = cal.DateOfDay(d);
                return WeatherSystem.MonthlyLerp(climate.MonthlyMeanTemp, date.Month + (date.Day - 0.5f) / 3f);
            });
            anomalies.Add(mean - normal);
            if (seed <= 4) output.WriteLine($"seed {seed}: summer anomaly {mean - normal:+0.0;-0.0} °C");
        }
        var sd = Math.Sqrt(anomalies.Average(a => a * a));
        output.WriteLine($"mean {anomalies.Average():+0.00;-0.00}, sd {sd:0.00}, min {anomalies.Min():0.0}, max {anomalies.Max():0.0}");
        Assert.InRange(anomalies.Average(), -0.6, 0.6);
        Assert.InRange(sd, 0.3, 1.3);
    }
}
