namespace Headland.Core.Tests;

/// <summary>
/// Wall-clock budgets. The crop tick is a <c>Parallel.For</c>, so these run alone, after the parallel test
/// classes: sharing a CI runner's few cores with the calibration runs made them flaky.
/// </summary>
[CollectionDefinition(nameof(PerformanceTests), DisableParallelization = true)]
public class PerformanceCollection
{
}

[Collection(nameof(PerformanceTests))]
public class PerformanceTests
{
    [Fact]
    public void HourlyTickIsFastEnough()
    {
        var sim = TestContent.NewSim();
        sim.SkipHours(2); // warm up the thread pool / JIT
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sim.SkipHours(24);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"24 hours took {sw.ElapsedMilliseconds} ms");
    }
}
