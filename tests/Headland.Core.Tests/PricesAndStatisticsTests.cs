using System.Numerics;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;
using Headland.Core.Ownership;
using Headland.Core.Pois.Components;
using Headland.Core.World;

namespace Headland.Core.Tests;

/// <summary>What the menu's prices, statistics and helpers pages show.</summary>
public class PricesAndStatisticsTests
{
    [Fact]
    public void EachBuyerQuotesItsPriceTheBestFirst()
    {
        var sim = TestContent.NewSim();
        Assert.Equal(["wheat", "barley", "canola", "corn", "flour", "grass", "hay", "straw"], sim.Pois.Sellable.Select(f => f.Id));

        // Wheat: the flour mill pays 10% more than the elevator.
        var wheat = sim.Pois.Quotes("wheat");
        Assert.Equal(["mill", "elevator"], wheat.Select(q => q.Poi.Id));
        Assert.Equal(wheat[1].Price * 1.1f, wheat[0].Price, 0.0001f);
        Assert.Equal(sim.Pois.Price(sim.World.PoiById("elevator")!.Get<SellingStation>()!, "wheat"), wheat[1].Price);
        Assert.All(wheat, q => Assert.True(q is { Loads: true, Objects: false }));
        // Hay at the dairy, by the bale; closed at night.
        var hay = Assert.Single(sim.Pois.Quotes("hay"));
        Assert.Equal(("dairy", false, true, null), (hay.Poi.Id, hay.Loads, hay.Objects, hay.Closed));
        TestContent.SkipTo(sim, sim.Clock.Month, sim.Clock.Date.Day + 1, 23f);
        Assert.Equal("Dairy Farm is closed: open 5:00–21:00", sim.Pois.Quotes("hay")[0].Closed);

    }

    [Fact]
    public void AHighDemandShowsOnItsQuote()
    {
        var elevatorJson = File.ReadAllText(Path.Combine(TestContent.DataDir, "pois", "grain_elevator.json"));
        var sim = Simulation.Create(TestContent.Modded(new()
        {
            ["pois/grain_elevator.json"] = elevatorJson.Replace("\"minAmount\": 500 }", "\"minAmount\": 500, \"demand\": { \"highChance\": 1 } }"),
        }));
        sim.SkipHours(24);
        var elevator = sim.World.PoiById("elevator")!.Get<SellingStation>()!;
        var high = elevator.HighDemand!;
        var quote = sim.Pois.Quotes(high.FillType).Single(q => q.Poi.Id == "elevator");
        Assert.Equal((high, sim.Pois.Price(elevator, high.FillType)), (quote.High, quote.Price));
        Assert.All(sim.Pois.Quotes(high.FillType).Where(q => q.Poi.Id != "elevator"), q => Assert.Null(q.High));
    }

    [Fact]
    public void TheFarmsStockIsInItsSilosBalesAndPallets()
    {
        var sim = TestContent.NewSim();
        Assert.Equal(0f, sim.Stock(Farm.PlayerId, "wheat"));
        sim.World.PoiById("silo")!.Get<Core.Components.FillUnits>()!.Add("wheat", 12000f);
        sim.World.PoiById("grainmill")!.Get<Core.Components.FillUnits>()!.Add("wheat", 3000f);
        // The NPC elevator's wheat isn't the farm's, nor a neighbor's bale.
        sim.Objects.Spawn("round_bale", new Vector2(160f, 200f), 0f, Farm.PlayerId).Content!.Add("hay", 4000f);
        sim.Objects.Spawn("round_bale", new Vector2(165f, 200f), 0f, Farm.None).Content!.Add("hay", 4000f);
        Assert.Equal((15000f, 4000f), (sim.Stock(Farm.PlayerId, "wheat"), sim.Stock(Farm.PlayerId, "hay")));
    }

    [Fact]
    public void AFieldIsSurveyedCellByCell()
    {
        var sim = TestContent.NewSim();
        var L = sim.World.Layers;
        // Field 2: ripe wheat; its yield is what each cell would give.
        var field = sim.World.FieldById(2)!;
        var cells = sim.World.CellsOf(field);
        Assert.Equal(field.AreaHa * 10000f / WorldMap.CellArea, cells.Length, cells.Length * 0.02f);
        foreach (var i in cells.Take(cells.Length / 4)) L.Weeds[i] = WeedState.Grown;
        foreach (var i in cells.Take(cells.Length / 2)) L.Fertilized[i] = 1;
        var s = FieldSurvey.Of(sim.World, sim.Content, field);
        Assert.Equal(("wheat", "Ready to harvest", 1f), (s.Crop?.Id, s.State, s.StateShare));
        Assert.Equal((0.25f, 0.5f), (s.Weeds, s.Fertilized), new FloatPair(0.01f));
        var wheat = sim.Content.CropById("wheat")!;
        Assert.Equal(cells.Sum(i => Crops.CropSystem.YieldPerHa(L, wheat, i)) * WorldMap.CellArea / 10000f, s.Yield, 1f);
        // Field 1: stubble, nothing growing.
        var stubble = FieldSurvey.Of(sim.World, sim.Content, sim.World.FieldById(1)!);
        Assert.Equal((null, "Harvested", 0f), (stubble.Crop, stubble.State, stubble.Yield));
        Assert.Equal(["Cultivated", "Plowed", "Sown", "Harvested", "Fertilized", "Sprayed", "Mown", "Tedded", "Raked", "Baled"],
            WorkTypes.All.Select(w => w.Done));
    }

    private sealed class FloatPair(float tolerance) : IEqualityComparer<(float, float)>
    {
        public bool Equals((float, float) a, (float, float) b) => MathF.Abs(a.Item1 - b.Item1) <= tolerance && MathF.Abs(a.Item2 - b.Item2) <= tolerance;
        public int GetHashCode((float, float) p) => 0;
    }

    [Fact]
    public void AHelperIsDismissedOrJoinedFromTheList()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(167f, 268f), 0f);
        var mower = sim.Machines.Spawn("mower_3", new Vector2(167f, 265f), 0f);
        Assert.True(sim.Machines.Attach(t, "rear", mower));
        var helper = sim.HireHelper(t, sim.World.FieldById(7)!);
        Assert.Equal([helper], sim.Helpers);

        // The farmer takes a seat: the helper goes on.
        Assert.True(sim.TakeSeat(t));
        Assert.Same(t, sim.Player.Vehicle);
        Assert.Same(helper, t.Get<Drivable>()!.Controller);
        // Dismissed, it leaves the vehicle to the farmer in it.
        sim.Dismiss(helper);
        Assert.Empty(sim.Helpers);
        Assert.Same(sim.Player.Controls, t.Get<Drivable>()!.Controller);
        Assert.False(sim.TakeSeat(sim.Machines.Spawn("tractor_95", new Vector2(300f, 300f), 0f, Farm.None)));
        Assert.Same(t, sim.Player.Vehicle);
    }
}
