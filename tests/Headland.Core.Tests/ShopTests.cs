using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class ShopTests
{
    private static Dictionary<string, string> Options(params (string configuration, string option)[] choices) =>
        choices.ToDictionary(c => c.configuration, c => c.option);

    private static Simulation SimOn(string difficulty)
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Game.Difficulty = difficulty;
        return Simulation.Create(db);
    }

    [Fact]
    public void TheShopListsItsMachinesByCategoryAndBrand()
    {
        var sim = TestContent.NewSim();
        var shop = sim.Shop;
        Assert.Equal(["tractor", "combine", "header", "trailer", "cultivator", "plow", "seeder", "spreader", "sprayer", "mower", "frontLoader"],
            shop.Categories().Select(c => c.Id));
        var content = sim.Content;
        // Cheapest first.
        Assert.Equal(["tractor_95", "tractor_125"], shop.Machines(content.ShopCategories["tractor"]).Select(m => m.Id));
        Assert.Equal(["header_grain_6", "header_corn_6"], shop.Machines(content.ShopCategories["header"]).Select(m => m.Id));

        Assert.Equal(["Fieldmaster", "Harvestor", "Haulmark", "Ridgeline", "Verdant"], shop.Brands.Select(b => b.Name));
        var ridgeline = content.Brands["ridgeline"];
        Assert.Equal(["cultivator", "plow", "seeder", "frontLoader"], shop.Categories(ridgeline).Select(c => c.Id));
        Assert.Empty(shop.Machines(content.ShopCategories["tractor"], ridgeline));
        Assert.Equal("Fieldmaster", shop.BrandOf(content.Machines["tractor_125"])!.Name);
    }

    [Fact]
    public void AMachineWithoutACategoryIsNotSold()
    {
        var db = TestContent.WithMachines("""
            [{ "id": "prototype", "name": "Prototype", "brand": "fieldmaster", "price": 1000, "components": {} }]
            """);
        var sim = Simulation.Create(db);
        Assert.DoesNotContain(sim.Shop.Categories().SelectMany(c => sim.Shop.Machines(c)), m => m.Id == "prototype");
    }

    [Fact]
    public void CategoriesAndBrandsAreChecked()
    {
        var bad = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "name": "X", "category": "hovercraft", "brand": "acme", "components": {} }]
            """));
        Assert.Contains("machine 'x': unknown category 'hovercraft' (shopcategories.json: tractor, combine", bad.Message);
        Assert.Contains("machine 'x': unknown brand 'acme' (brands.json: fieldmaster, harvestor", bad.Message);

        var unnamed = Assert.Throws<ContentException>(() => TestContent.Modded(new()
        {
            ["brands.json"] = TestContent.WithEntry("brands.json", """{ "id": "acme", "name": "" }"""),
        }));
        Assert.Contains("brand 'acme': needs a name", unnamed.Message);
    }

    [Fact]
    public void PricesFollowTheOptionsAndTheDifficulty()
    {
        var sim = TestContent.NewSim();
        var tractor = sim.Content.Machines["tractor_125"];
        Assert.Equal(98_000f, sim.Shop.Price(tractor));
        Assert.Equal(98_000f + 12_500f + 5_800f, sim.Shop.Price(tractor.Configure(Options(("engine", "145"), ("wheels", "dual")))));

        var hard = SimOn("hard");
        Assert.Equal(MathF.Round(98_000f * 1.2f), hard.Shop.Price(hard.Content.Machines["tractor_125"]));
    }

    [Fact]
    public void SpecsComeFromTheComponentsAndTheOptions()
    {
        var shop = TestContent.NewSim().Shop;
        var content = TestContent.Content;
        string Specs(string id, params (string, string)[] options) =>
            string.Join("; ", shop.Specs(content.Machines[id].Configure(Options(options))).Select(s => $"{s.Name}: {s.Value}"));

        Assert.Equal("Power: 125 hp; Top speed: 40 km/h; Hitches: Three-point linkage (rear), Drawbar, Three-point linkage (front); " +
                     "Fuel tank: 250 L; Mass: 5,600 kg", Specs("tractor_125"));
        Assert.StartsWith("Power: 145 hp;", Specs("tractor_125", ("engine", "145")));
        Assert.Equal("Hitch: Drawbar, trailed; Capacity: 20,000 L: wheat, barley, canola, corn; Tipping: 450 L/s; Tips to: back, left, right; Mass: 4,940 kg",
            Specs("trailer_16", ("capacity", "20000")));
        Assert.Equal("Hitch: Three-point linkage, mounted; Working width: 4 m; Working speed: 14 km/h; Power needed: 120 hp; Mass: 1,540 kg",
            Specs("cultivator_3", ("width", "4")));
        Assert.Contains("Crops: corn", Specs("header_corn_6"));
        Assert.Contains("Crops: barley, canola, wheat", Specs("header_grain_6"));
        Assert.Contains(new Spec("Unloading", "140 L/s"), shop.Specs(content.Machines["combine_7"]));
    }

    [Fact]
    public void BoughtMachinesWaitOnTheDealersLot()
    {
        var sim = TestContent.NewSim();
        var bought = Record<MachineBought>(sim);
        var money = sim.Economy.Money;
        var def = sim.Content.Machines["tractor_95"].Configure(Options(("wheels", "dual")));
        var price = sim.Shop.Price(def);

        var t = sim.Shop.Buy(def)!;
        var dealer = sim.World.PoiById("dealer")!;
        Assert.Same(def, t.Def);
        Assert.Equal((Farm.PlayerId, 0, (MachineLease?)null), (t.FarmId, t.LeaseContract, t.Lease));
        Assert.True(dealer.Trigger("delivery")!.Contains(t.Footprint.Center));
        Assert.Equal(dealer.Heading, t.Heading);
        Assert.Equal(money - price, sim.Economy.Money);
        Assert.Equal(-price, sim.Economy.Ledger.Today[MoneyCategory.Machines]);
        Assert.Equal((t, dealer, price), (bought.Single().Machine, bought.Single().Poi, bought.Single().Price));
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Bought the Fieldmaster 95 for ${price:N0}: it waits at Machinery Dealer");
        // New, with the fuel and seed a new machine comes with.
        Assert.Equal((1f, 180f), (t.Get<Wearable>()!.Condition, t.Unit("fuel")!.Level));
    }

    [Fact]
    public void TheLotFillsUpAndTheMoneyRunsOut()
    {
        var sim = TestContent.NewSim();
        var combine = sim.Content.Machines["combine_7"];
        Assert.Equal("Not enough money", sim.Shop.BuyBlocker(combine));
        Assert.Null(sim.Shop.Buy(combine));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Not enough money");

        sim.Economy.Earn(100_000_000f, MoneyCategory.Other);
        var delivered = new List<Machine>();
        while (sim.Shop.BuyBlocker(combine) == null) delivered.Add(sim.Shop.Buy(combine)!);
        Assert.Equal("No room on the lot of Machinery Dealer: clear it first", sim.Shop.BuyBlocker(combine));
        Assert.True(delivered.Count > 4);
        var lot = sim.World.PoiById("dealer")!.Trigger("delivery")!;
        Assert.All(delivered, m => Assert.True(lot.Contains(m.Footprint.Center)));
        for (var i = 0; i < delivered.Count; i++)
        for (var j = i + 1; j < delivered.Count; j++)
            Assert.False(Geometry.Overlaps(delivered[i].Footprint, delivered[j].Footprint));
        // Something smaller may still fit between them.
        Assert.Null(sim.Shop.BuyBlocker(sim.Content.Machines["spreader_24"]));
    }

    [Fact]
    public void LeasedMachinesCostAFeeThenTheirHours()
    {
        var sim = TestContent.NewSim();
        var leased = Record<MachineLeased>(sim);
        var money = sim.Economy.Money;
        var def = sim.Content.Machines["tractor_125"];
        var (fee, perHour) = sim.Shop.LeaseTerms(def);
        Assert.Equal((1960f, 2058f), (fee, perHour));

        var t = sim.Shop.Lease(def)!;
        Assert.Equal((fee, perHour, 0f), (t.Lease!.Fee, t.Lease.PerHour, t.Lease.Paid));
        Assert.Equal(money - fee, sim.Economy.Money);
        Assert.Equal(-fee, sim.Economy.Ledger.Today[MoneyCategory.Leasing]);
        Assert.Same(t, leased.Single().Machine);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Leased the Fieldmaster 125 for $1,960 and $2,058 an hour it runs: it waits at Machinery Dealer");

        // Parked, it costs nothing more.
        for (var i = 0; i < 60; i++) sim.Tick(1f);
        Assert.Equal((0.0, money - fee), (t.OperatingHours, sim.Economy.Money));

        // Running, its hours count, and are paid as they add up to whole dollars.
        sim.Player.Enter(t);
        for (var i = 0; i < 60; i++) sim.Tick(1f);
        Assert.Equal(60.0 / 3600.0, t.OperatingHours, 6);
        Assert.Equal(MathF.Floor(perHour / 60f), t.Lease.Paid);
        Assert.Equal(money - fee - t.Lease.Paid, sim.Economy.Money, 1);

        // A leased machine goes back as it came.
        var bay = sim.World.PoiById("workshop")!.Trigger("repair")!;
        sim.Machines.Teleport(t, bay.Area.Center, 0f);
        Assert.False(sim.Pois.Configure(t, Options(("wheels", "dual"))));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "A leased machine goes back as it came");
    }

    [Fact]
    public void MachinesRunWithTheVehicleTheyHangOn()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = TrailerAt(sim, new Vector2(269f, 300f), "wheat", 0f);
        var parked = sim.Machines.Spawn("cultivator_3", new Vector2(250f, 300f), 0f);
        Assert.False(t.Operating);
        sim.Player.Enter(t);
        for (var i = 0; i < 36; i++) sim.Tick(1f);
        Assert.Equal((0.01, 0.01, 0.0), (Math.Round(t.OperatingHours, 6), Math.Round(trailer.OperatingHours, 6), parked.OperatingHours));
        // Out of fuel, the engine stops.
        t.Unit("fuel")!.Remove(t.Unit("fuel")!.Level);
        Assert.False(trailer.Operating);
    }

    [Fact]
    public void LeasesAndHoursAreSaved()
    {
        var sim = TestContent.NewSim();
        var t = sim.Shop.Lease(sim.Content.Machines["tractor_95"])!;
        t.OperatingHours = 1.25;
        sim.Tick(0.01f);
        var loaded = SaveGame.Load(TestContent.Content, SaveGame.Capture(sim, "test")).Sim;
        var back = loaded.Machines.ById(t.Id)!;
        Assert.Equal((t.Lease!.Fee, t.Lease.PerHour, t.Lease.Paid), (back.Lease!.Fee, back.Lease.PerHour, back.Lease.Paid));
        Assert.Equal(t.OperatingHours, back.OperatingHours);
        Assert.Null(loaded.Machines.All.First(m => m.Def.Id == "combine_7").Lease);
    }

    [Fact]
    public void DeliverySpotsSayWhatTheyDeliver()
    {
        var sim = TestContent.NewSim();
        var lot = sim.World.PoiById("dealer")!.Trigger("delivery")!;
        Assert.Equal(["Machines bought or leased at the shop wait here", "Machines leased for contracts wait here"], sim.Pois.Describe(lot));

        var bad = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "yard", "name": "Yard", "components": { "deliverySpot": { "trigger": { "w": 10, "d": 10 } } } }]
            """));
        Assert.Contains("poi 'yard' deliverySpot: delivers nothing: needs sales or leases", bad.Message);
    }
}
