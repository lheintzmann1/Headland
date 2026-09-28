using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.Pois.Components;

namespace Headland.Core.Tests;

/// <summary>Fill types come in categories (FS: fill type categories), which fill units and stations take whole.</summary>
public class FillTypeCategoryTests
{
    [Fact]
    public void FillTypesComeInCategoriesThatUnitsAndStationsTake()
    {
        var content = TestContent.Content;
        Assert.Equal(["wheat", "barley", "canola", "corn"], content.FillTypesIn("grain"));
        Assert.Equal(["fertilizer"], content.FillTypesIn("fertilizer"));
        Assert.Empty(content.FillTypesIn("gravel"));

        // Grain into the combine's tank and the trailer, and sold at the elevator; seed, fertilizer and herbicide at the shop.
        Assert.Equal(content.FillTypesIn("grain"), content.Machines["combine_7"].Get<FillUnitsDef>()!.Units.Single(u => u.Id == "tank").FillTypes);
        Assert.Equal(content.FillTypesIn("grain"), content.Machines["trailer_16"].Get<FillUnitsDef>()!.Units[0].FillTypes);
        Assert.Equal(content.FillTypesIn("grain"), content.Pois["grain_elevator"].Get<SellingStationDef>()!.FillTypes);
        Assert.Equal(["seeds", "fertilizer", "herbicide"], content.Pois["farm_shop"].Get<BuyingStationDef>()!.FillTypes);
        // With an option too.
        var bigger = content.Machines["trailer_16"].Configure(new Dictionary<string, string> { ["capacity"] = "20000" });
        Assert.Equal(content.FillTypesIn("grain"), bigger.Get<FillUnitsDef>()!.Units[0].FillTypes);
    }

    [Fact]
    public void ANewGrainGoesWhereverGrainGoes()
    {
        var content = TestContent.Modded(new()
        {
            ["filltypes.json"] = TestContent.WithEntry("filltypes.json",
                """  { "id": "oats", "name": "Oats", "unit": "L", "massPerUnit": 0.5, "pricePerUnit": 0.25, "categories": ["grain"] }"""),
        });
        Assert.Empty(content.Validate());
        var sim = Simulation.Create(content);
        var pit = sim.World.PoiById("elevator")!.Trigger("unload")!;
        var trailer = sim.Machines.Spawn("trailer_16", pit.Area.Center, 0f);
        Assert.True(trailer.Unit("main")!.CanAccept("oats"));
        Assert.True(sim.Machines.All.Single(m => m.Def.Id == "combine_7").Unit("tank")!.CanAccept("oats"));
        Assert.Null(sim.Pois.UnloadBlocker(trailer, pit, "oats", 5000f));
        Assert.NotNull(sim.Pois.SalePrice(trailer, pit, "oats"));
        // The mill only takes its wheat, and the farm silo only has bins for the grains it was built for.
        Assert.Equal("Flour Mill does not buy Oats", sim.Pois.UnloadBlocker(trailer, sim.World.PoiById("mill")!.Trigger("unload")!, "oats", 5000f));
        Assert.False(sim.World.PoiById("silo")!.Get<FillUnits>()!.Keeps("oats"));
    }

    [Fact]
    public void UnknownCategoriesAreRefused()
    {
        var machine = Assert.Throws<ContentException>(() => TestContent.WithMachines("""
            [{ "id": "x", "components": { "fillUnits": { "units": [ { "id": "bin", "fillTypeCategories": ["gravel"] } ] } } }]
            """));
        Assert.Contains("machine 'x' fillUnits: unit 'bin': no fill type is in category 'gravel'", machine.Message);
        Assert.Contains("machine 'x' fillUnits: unit 'bin': needs fillTypes or fillTypeCategories", machine.Message);
        var poi = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "quarry", "name": "Quarry", "components": { "sellingStation": { "fillTypeCategories": ["gravel"] } } }]
            """));
        Assert.Contains("poi 'quarry' sellingStation: no fill type is in category 'gravel'", poi.Message);

        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.ContractTypes["deliver"].Deliver!.FillTypeCategories = ["gravel"];
        Assert.Contains("contract type 'deliver': deliver.fillTypeCategories: no fill type is in category 'gravel'", db.Validate());
    }
}
