using Headland.Core.Components;
using Headland.Core.Content;

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
        Assert.Equal(["tractor", "combine", "header", "trailer", "cultivator", "plow", "seeder", "spreader", "sprayer", "mower"],
            shop.Categories().Select(c => c.Id));
        var content = sim.Content;
        // Cheapest first.
        Assert.Equal(["tractor_95", "tractor_125"], shop.Machines(content.ShopCategories["tractor"]).Select(m => m.Id));
        Assert.Equal(["header_grain_6", "header_corn_6"], shop.Machines(content.ShopCategories["header"]).Select(m => m.Id));

        Assert.Equal(["Fieldmaster", "Harvestor", "Haulmark", "Ridgeline", "Verdant"], shop.Brands.Select(b => b.Name));
        var ridgeline = content.Brands["ridgeline"];
        Assert.Equal(["cultivator", "plow", "seeder"], shop.Categories(ridgeline).Select(c => c.Id));
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
        Assert.Equal("Hitch: Drawbar, trailed; Capacity: 20,000 L: wheat, barley, canola, corn; Tipping: 450 L/s; Mass: 4,940 kg",
            Specs("trailer_16", ("capacity", "20000")));
        Assert.Equal("Hitch: Three-point linkage, mounted; Working width: 4 m; Working speed: 14 km/h; Power needed: 120 hp; Mass: 1,540 kg",
            Specs("cultivator_3", ("width", "4")));
        Assert.Contains("Crops: corn", Specs("header_corn_6"));
        Assert.Contains("Crops: wheat, barley, canola", Specs("header_grain_6"));
        Assert.Contains(new Spec("Unloading", "140 L/s"), shop.Specs(content.Machines["combine_7"]));
    }
}
