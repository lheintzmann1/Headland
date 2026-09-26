using System.Numerics;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class CropTests
{
    // Open meadow on the default map, far from roads and buildings.
    private static readonly Vector2 Plot = new(270f, 310f);

    private static (Simulation sim, int cell) Setup(string cropId, byte stage, float moisture, byte nitrogen = 200)
    {
        var sim = TestContent.NewSim();
        var cropIndex = sim.Content.CropIndex(cropId);
        var L = sim.World.Layers;
        var loam = (byte)sim.Content.SoilIndex("loam");
        TestContent.PrepareCells(sim.World, Plot, 4, i =>
        {
            L.Ground[i] = (byte)GroundType.Seeded;
            L.Soil[i] = loam;
            L.Crop[i] = (byte)(cropIndex + 1);
            L.Stage[i] = stage;
            L.Progress[i] = 0f;
            L.Health[i] = 255;
            L.Nitrogen[i] = nitrogen;
            L.Moisture[i] = WorldGen.ToByte(moisture);
        });
        var (cx, cz) = sim.World.WorldToCell(Plot);
        return (sim, sim.World.CellIndex(cx, cz));
    }

    private static void RunHours(Simulation sim, int hours, float temp, float rain = 0f, float? keepMoisture = null, int cell = -1)
    {
        for (var h = 0; h < hours; h++)
        {
            sim.Weather.ForceSnapshot(temp, rain);
            sim.Crops.TickHour(sim.Weather, sim.Clock.DayIndex, h);
            if (keepMoisture is { } m) sim.World.Layers.Moisture[cell] = WorldGen.ToByte(m);
        }
    }

    [Fact]
    public void WheatAdvancesByDegreeDays()
    {
        var (sim, cell) = Setup("wheat", 1, 0.6f);
        var wheat = sim.Content.CropById("wheat")!;
        // 15 °C above a 0 °C base, ~10 real days per game day: ~6.3 degree-days per game hour.
        var perHour = 15f * sim.Calendar.RealDaysPerGameDay / 24f;
        var hoursNeeded = (int)MathF.Ceiling(wheat.Stages[1].Gdd / perHour);
        RunHours(sim, hoursNeeded - 2, 15f, keepMoisture: 0.6f, cell: cell);
        Assert.Equal(1, sim.World.Layers.Stage[cell]);
        RunHours(sim, 4, 15f, keepMoisture: 0.6f, cell: cell);
        Assert.Equal(2, sim.World.Layers.Stage[cell]);
    }

    [Fact]
    public void CornDoesNotGrowBelowItsBaseTemperature()
    {
        var (sim, cell) = Setup("corn", 2, 0.6f);
        RunHours(sim, 200, 8f, keepMoisture: 0.6f, cell: cell);
        Assert.Equal(2, sim.World.Layers.Stage[cell]);
        Assert.Equal(0f, sim.World.Layers.Progress[cell]);
    }

    [Fact]
    public void DroughtHurtsHealthAndSlowsGrowth()
    {
        var (wet, wetCell) = Setup("barley", 2, 0.6f);
        var (dry, dryCell) = Setup("barley", 2, 0.1f);
        RunHours(wet, 72, 18f, keepMoisture: 0.6f, cell: wetCell);
        RunHours(dry, 72, 18f, keepMoisture: 0.1f, cell: dryCell);
        Assert.True(dry.World.Layers.Health[dryCell] < 200, $"health {dry.World.Layers.Health[dryCell]}");
        Assert.Equal(255, wet.World.Layers.Health[wetCell]);
        var wetProgress = wet.World.Layers.Stage[wetCell] * 1000 + wet.World.Layers.Progress[wetCell];
        var dryProgress = dry.World.Layers.Stage[dryCell] * 1000 + dry.World.Layers.Progress[dryCell];
        Assert.True(dryProgress < wetProgress);
    }

    [Fact]
    public void FrostKillsCorn()
    {
        var (sim, cell) = Setup("corn", 3, 0.6f);
        RunHours(sim, 6, -4f);
        Assert.Equal(CropStage.Dead, sim.World.Layers.Stage[cell]);
    }

    [Fact]
    public void WinterWheatSurvivesMildFrost()
    {
        var (sim, cell) = Setup("wheat", 2, 0.6f);
        RunHours(sim, 24, -6f);
        Assert.NotEqual(CropStage.Dead, sim.World.Layers.Stage[cell]);
        Assert.True(sim.World.Layers.Health[cell] > 200);
    }

    [Fact]
    public void NitrogenShortfallCostsHealthOnStageAdvance()
    {
        var (sim, cell) = Setup("wheat", 1, 0.6f, nitrogen: 0);
        RunHours(sim, 70, 15f, keepMoisture: 0.6f, cell: cell);
        Assert.True(sim.World.Layers.Stage[cell] >= 2);
        Assert.True(sim.World.Layers.Health[cell] < 255);
    }

    [Fact]
    public void RainRaisesMoistureAndSandDrainsFasterThanClay()
    {
        var sim = TestContent.NewSim();
        var L = sim.World.Layers;
        var (cx, cz) = sim.World.WorldToCell(Plot);
        var sand = sim.World.CellIndex(cx, cz);
        var clay = sim.World.CellIndex(cx + 2, cz);
        foreach (var (i, soil) in new[] { (sand, "sand"), (clay, "clay") })
        {
            L.Ground[i] = (byte)GroundType.Cultivated;
            L.Crop[i] = 0;
            L.Soil[i] = (byte)sim.Content.SoilIndex(soil);
            L.Moisture[i] = WorldGen.ToByte(0.3f);
        }
        RunHours(sim, 4, 12f, rain: 8f);
        Assert.True(L.Moisture[sand] > WorldGen.ToByte(0.5f));
        Assert.True(L.Moisture[clay] > WorldGen.ToByte(0.4f));

        // From saturation, sand loses its water much faster than clay.
        L.Moisture[sand] = L.Moisture[clay] = WorldGen.ToByte(0.95f);
        RunHours(sim, 12, 12f);
        var sandDrop = 0.95f - L.Moisture[sand] / 255f;
        var clayDrop = 0.95f - L.Moisture[clay] / 255f;
        Assert.True(sandDrop > clayDrop * 2f, $"sand -{sandDrop:F2}, clay -{clayDrop:F2}");
    }
}
