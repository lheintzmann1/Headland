using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using Headland.Core.Time;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class FinanceTests
{
    [Fact]
    public void TheBooksKeepFiveDaysAndTwelveMonths()
    {
        var sim = TestContent.SmallSim();
        var books = sim.Economy.Ledger;
        sim.Economy.Earn(1000f, MoneyCategory.Sales);
        sim.Economy.Spend(300f, MoneyCategory.Fuel);
        Assert.Equal((1000f, -300f, 0f), (books.Today[MoneyCategory.Sales], books.Today[MoneyCategory.Fuel], books.Today[MoneyCategory.Land]));
        Assert.Equal((1000f, 300f, 700f), (books.Today.Income, books.Today.Expenses, books.Today.Net));
        Assert.Equal(700f, books.ThisMonth.Net);

        // August 1st, 7:30: midnight turns the day's page; August goes on.
        var first = books.Today;
        sim.SkipHours(24);
        Assert.Equal([new GameDate(1, 8, 2), new GameDate(1, 8, 1)], books.Days.Select(books.DateOf));
        Assert.Same(first, books.Days[1]);
        Assert.Equal(0f, books.Today.Net);
        Assert.Equal(700f, Assert.Single(books.Months).Net);

        // 13 months later, September 2nd of year 2.
        sim.SkipHours(24 * 39);
        Assert.Equal(Ledger.KeptDays, books.Days.Count);
        Assert.Equal(new GameDate(2, 9, 2), books.DateOf(books.Today));
        Assert.Equal(Ledger.KeptMonths, books.Months.Count);
        Assert.Equal([(2, 9), (1, 10)], new[] { books.Months[0], books.Months[^1] }.Select(Ledger.MonthOf));
    }

    [Fact]
    public void PoiMoneyIsBookedUnderItsCategory()
    {
        var sim = TestContent.NewSim();
        var books = sim.Economy.Ledger;
        var bought = Record<FillBought>(sim);
        var money = sim.Economy.Money;

        var pit = sim.World.PoiById("elevator")!.Trigger("pit")!;
        sim.Pois.Unload(sim.Machines.Spawn("trailer_16", pit.Area.Center, 0f), pit, "wheat", 10_000f);
        var seeder = sim.Machines.Spawn("seeder_3", sim.World.PoiById("supplies")!.Trigger("yard")!.Area.Center, 0f);
        seeder.Unit("seed")!.Remove(500f);
        sim.Pois.Use(seeder);
        var t = sim.Machines.Spawn("tractor_95", sim.World.PoiById("gas")!.Trigger("pumps")!.Area.Center, MathF.PI / 2f);
        t.Unit("fuel")!.Remove(100f);
        sim.Pois.Use(t);
        sim.Machines.Teleport(t, sim.World.PoiById("workshop")!.Trigger("bay")!.Area.Center, 0f);
        t.Condition = 0.5f;
        sim.Pois.Use(t);

        Assert.Equal(10_000f * sim.Economy.Price("wheat", sim.Clock.Month), books.Today[MoneyCategory.Sales], 1);
        Assert.Equal(["seeds", "diesel"], bought.Select(b => b.FillType));
        Assert.Equal((-bought[0].Cost, -bought[1].Cost), (books.Today[MoneyCategory.Purchases], books.Today[MoneyCategory.Fuel]));
        Assert.Equal(-72_000f / 100f * 0.5f, books.Today[MoneyCategory.Maintenance], 1);
        Assert.Equal(sim.Economy.Money - money, books.Today.Net, 1);
    }

    /// <summary>A press burning 10 L of canola into 4 L of diesel every hour, for $10 an hour.</summary>
    private static readonly PoiDef Press = new()
    {
        Id = "test_press", Name = "Press",
        Storage = new PoiStorageDef { FillTypes = ["canola", "diesel"], Capacity = 100_000 },
        Actions =
        [
            new PoiActionDef
            {
                Type = "process", RunningCost = 10,
                Inputs = [new FillAmountDef { FillType = "canola", Amount = 10 }],
                Outputs = [new ProcessOutputDef { FillType = "diesel", Amount = 4 }],
            },
        ],
    };

    [Fact]
    public void EachHourIsBookedOnItsOwnDay()
    {
        var sim = SimWith([Press], new PoiPlacementDef { Id = "press", Type = "test_press", X = 30, Z = 30, Farm = Farm.PlayerId });
        sim.World.PoiById("press")!.Storage!.Add("canola", 10_000f);

        // From 7:30 to 7:30 the next day: 16 hours on the first day, and from midnight 8 on the second.
        sim.SkipHours(24);
        var days = sim.Economy.Ledger.Days;
        Assert.Equal([-80f, -160f], days.Select(d => d[MoneyCategory.Production]));
    }

    [Fact]
    public void TheBooksAreSaved()
    {
        var sim = TestContent.SmallSim();
        sim.Economy.Earn(1200f, MoneyCategory.Sales);
        sim.SkipHours(24);
        sim.Economy.Spend(450f, MoneyCategory.Maintenance);
        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Economy.Ledger;
        var books = sim.Economy.Ledger;

        foreach (var (saved, back) in new[] { (books.Days, loaded.Days), (books.Months, loaded.Months) })
        {
            Assert.Equal(saved.Select(p => p.Index), back.Select(p => p.Index));
            Assert.Equal(saved.SelectMany(p => Ledger.Categories.Select(c => p[c])), back.SelectMany(p => Ledger.Categories.Select(c => p[c])));
        }
        Assert.Equal((0f, -450f, 1200f), (loaded.Today[MoneyCategory.Sales], loaded.Today[MoneyCategory.Maintenance], loaded.Days[1].Net));
    }
}
