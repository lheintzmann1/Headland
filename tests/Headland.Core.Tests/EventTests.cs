using System.Numerics;
using Headland.Core.Events;
using Headland.Core.Time;

namespace Headland.Core.Tests;

public class EventBusTests
{
    private sealed record Ping(int N) : IGameEvent;

    private sealed record Pong : IGameEvent;

    [Fact]
    public void HandlersGetTheirEventTypeInSubscriptionOrder()
    {
        var bus = new EventBus();
        var log = new List<string>();
        bus.Subscribe<Ping>(e => log.Add($"a{e.N}"));
        bus.Subscribe<Ping>(e => log.Add($"b{e.N}"));
        bus.Subscribe<Pong>(_ => log.Add("pong"));
        bus.SubscribeAll(e => log.Add(e.GetType().Name));

        bus.Publish(new Ping(1));
        bus.Publish(new Pong());
        Assert.Equal(["a1", "b1", "Ping", "pong", "Pong"], log);
    }

    [Fact]
    public void DisposingASubscriptionStopsIt()
    {
        var bus = new EventBus();
        var count = 0;
        var sub = bus.Subscribe<Ping>(_ => count++);
        bus.Publish(new Ping(1));
        sub.Dispose();
        sub.Dispose();
        bus.Publish(new Ping(2));
        Assert.Equal(1, count);
    }

    [Fact]
    public void SubscribingDuringDispatchStartsWithTheNextEvent()
    {
        var bus = new EventBus();
        var late = 0;
        bus.Subscribe<Ping>(_ => bus.Subscribe<Ping>(_ => late++));
        bus.Publish(new Ping(1));
        Assert.Equal(0, late);
        bus.Publish(new Ping(2));
        Assert.Equal(1, late);
    }
}

public class GameEventTests
{
    private const float Dt = 1f / 60f;

    private static List<T> Record<T>(Simulation sim) where T : IGameEvent
    {
        var list = new List<T>();
        sim.Events.Subscribe<T>(list.Add);
        return list;
    }

    [Fact]
    public void SkippedTimePublishesEveryHourDayAndMonth()
    {
        var sim = TestContent.SmallSim();
        var hours = Record<HourStarted>(sim);
        var days = Record<DayStarted>(sim);
        var months = Record<MonthStarted>(sim);
        var start = sim.Clock.TotalHours;

        // August 1st 7:30 → September 1st 7:30 with 3-day months.
        sim.SkipHours(72);
        Assert.Equal(Enumerable.Range(1, 72).Select(h => start + h), hours.Select(h => h.Hour));
        Assert.Equal([new GameDate(1, 8, 2), new GameDate(1, 8, 3), new GameDate(1, 9, 1)], days.Select(d => d.Date));
        Assert.Equal([new MonthStarted(1, 9)], months);
        Assert.Equal(3, sim.Statistics.DaysPlayed);
    }

    [Fact]
    public void WorkAndSalesAreCountedInStatistics()
    {
        var sim = TestContent.NewSim();
        var worked = Record<FieldWorked>(sim);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 278f), 0f);
        var attached = Record<ImplementAttached>(sim);
        Assert.True(sim.Machines.Attach(t, "rear", c));
        Assert.Equal([new ImplementAttached(t, "rear", c)], attached);

        c.Lowered = true;
        t.Controller = new Machines.ManualController { Input = new Machines.VehicleInput { Throttle = 1f } };
        for (var s = 0f; s < 8f; s += Dt) sim.Tick(Dt);
        Assert.NotEmpty(worked);
        Assert.All(worked, e => Assert.Equal(("cultivator", 4), (e.Work, e.FieldId)));
        Assert.Equal(c.WorkedHa, worked.Sum(e => e.Hectares), 4);
        Assert.Equal(c.WorkedHa, sim.Statistics.HectaresWorked["cultivator"], 4);
    }

    [Fact]
    public void TippingPublishesOneSaleForTheLoad()
    {
        var sim = TestContent.NewSim();
        var sold = Record<FillSold>(sim);
        var t = sim.Machines.Spawn("tractor_95", new Vector2(441f, 230f), MathF.PI / 2f);
        var trailer = sim.Machines.Spawn("trailer_16", new Vector2(435f, 230f), MathF.PI / 2f);
        sim.Machines.Attach(t, "drawbar", trailer);
        trailer.Unit("main")!.Add("wheat", 8000f);
        sim.Player.Enter(t);
        sim.CommandUnload();
        for (var s = 0f; s < 60f && (trailer.Tipping || trailer.TipAnim > 0f); s += Dt) sim.Tick(Dt);

        var sale = Assert.Single(sold);
        Assert.Equal(("elevator", "wheat", trailer), (sale.Poi.Id, sale.FillType, sale.Machine));
        Assert.Equal(8000f, sale.Amount, 1);
        Assert.Equal(8000f, sim.Statistics.Sold["wheat"], 1);
    }

    [Fact]
    public void HelperIsHiredAndDismissedThroughEvents()
    {
        var sim = TestContent.NewSim();
        var hired = Record<HelperHired>(sim);
        var dismissed = Record<HelperDismissed>(sim);
        var entered = Record<VehicleEntered>(sim);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(242f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(242f, 278f), 0f);
        sim.Machines.Attach(t, "rear", c);
        sim.Player.Enter(t);
        Assert.Equal([new VehicleEntered(t)], entered);

        sim.CommandHelper();
        Assert.Equal(4, Assert.Single(hired).Field.Id);
        sim.CommandHelper();
        var end = Assert.Single(dismissed);
        Assert.Equal((t, HelperEnd.Dismissed), (end.Vehicle, end.End));
        Assert.Same(sim.Player.Controls, t.Controller);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Helper dismissed");
    }
}
