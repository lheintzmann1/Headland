using FarmSim.Core.Machines;
using FarmSim.Core.World;
using Xunit.Abstractions;

namespace FarmSim.Core.Tests;

/// <summary>
/// Agronomy calibration: crops sown in season must ripen in their real harvest months, stay healthy on
/// fertile loam with normal weather, and winter crops must not bolt before winter (vernalization).
/// Runs several weather seeds in a small world.
/// </summary>
public class CalibrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("wheat", 10, 1, new[] { 6, 7, 8 })]
    [InlineData("barley", 4, 1, new[] { 7, 8 })]
    [InlineData("canola", 8, 3, new[] { 6, 7 })]
    [InlineData("corn", 5, 1, new[] { 9, 10, 11 })]
    public void CropsRipenInTheirHarvestMonths(string cropId, int sowMonth, int sowDay, int[] harvestMonths)
    {
        foreach (var seed in new ulong[] { 1, 2, 3, 4 })
        {
            var sim = TestContent.SmallSim(seed);
            var def = sim.Content.CropById(cropId)!;
            var cropIndex = sim.Content.CropIndex(cropId);
            var bolting = Array.FindIndex(def.Stages, s => s.RequiresVernalization);
            TestContent.SkipTo(sim, sowMonth, sowDay, 8f);
            var L = sim.World.Layers;
            var cells = Enumerable.Range(0, L.Ground.Length).Where(i => L.FieldId[i] == 1).ToList();
            foreach (var i in cells) WorkOps.Sow(sim.World, i, cropIndex, 255, 0);

            string? ripeDate = null;
            var ripeMonth = 0;
            var lastMonth = 0;
            for (var day = 0; day < sim.Calendar.DaysPerYear + 6 && ripeDate == null; day++)
            {
                sim.SkipHours(24);
                var date = sim.Clock.Date;
                var alive = cells.Where(i => L.Stage[i] != CropStage.Dead).ToList();
                if (date.Month != lastMonth && alive.Count > 0)
                {
                    lastMonth = date.Month;
                    var stage = alive.GroupBy(i => L.Stage[i]).OrderByDescending(g => g.Count()).First().Key;
                    var temps = Enumerable.Range(0, sim.Calendar.DaysPerMonth)
                        .Select(d => sim.Weather.GetDay(sim.Clock.DayIndex - d).TempMean).Average();
                    output.WriteLine($"  {date}: {temps:0.0}°C, {def.Stages[stage].Name}, moisture {alive.Average(i => L.Moisture[i] / 2.55):0}%, " +
                                     $"N {alive.Average(i => (double)L.Nitrogen[i]):0}, chill {alive.Average(i => (double)L.Chill[i]):0}, " +
                                     $"health {alive.Average(i => L.Health[i] / 2.55):0}%");
                }
                if (bolting >= 0 && date.Month is 11 or 12 or 1)
                {
                    // Before and through winter a winter crop may only just be starting to bolt.
                    var early = alive.Count(i => L.Stage[i] > bolting ||
                                                 (L.Stage[i] == bolting && L.Progress[i] > def.Stages[bolting].Gdd * 0.3f));
                    Assert.True(early == 0, $"{cropId} seed {seed}: bolted by {date}, before vernalization could finish");
                }
                var ripe = alive.Count(i => def.Stages[L.Stage[i]].Harvestable);
                if (ripe > cells.Count / 2)
                {
                    ripeDate = date.ToString();
                    ripeMonth = date.Month;
                }
            }

            var health = cells.Average(i => L.Health[i] / 255.0);
            var dead = cells.Count(i => L.Stage[i] == CropStage.Dead) / (double)cells.Count;
            output.WriteLine($"{cropId} seed {seed}: ripe {ripeDate ?? "never"}, health {health:P0}, dead {dead:P0}");
            Assert.NotNull(ripeDate);
            Assert.Contains(ripeMonth, harvestMonths);
            Assert.True(health > 0.55, $"{cropId} seed {seed}: health {health:P0}");
            Assert.True(dead < 0.05, $"{cropId} seed {seed}: {dead:P0} dead");
        }
    }
}
