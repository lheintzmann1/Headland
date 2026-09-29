using System.Numerics;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using static Headland.Core.Tests.PoiTests;

namespace Headland.Core.Tests;

public class GarageTests
{
    private const float Dt = 1f / 60f;

    private static Dictionary<string, string> Options(params (string configuration, string option)[] choices) =>
        choices.ToDictionary(c => c.configuration, c => c.option);

    [Fact]
    public void TheGarageListsTheFarmsMachinesWhereTheyAre()
    {
        var sim = TestContent.NewSim();
        var garage = sim.Garage;
        // The map's twelve, tractors first.
        Assert.Equal(12, garage.Machines.Count());
        Assert.Equal(["tractor_95", "tractor_125", "combine_7"], garage.Machines.Take(3).Select(m => m.Def.Id));
        sim.Machines.Spawn("tractor_95", new Vector2(300f, 300f), 0f, Farm.None);
        Assert.Equal(12, garage.Machines.Count());

        var bought = sim.Shop.Buy(sim.Content.Machines["mower_3"])!;
        Assert.Equal("At Machinery Dealer", garage.Location(bought));
        var field = sim.World.FieldById(2)!;
        sim.Machines.Teleport(bought, field.Shape.Centroid, 0f);
        Assert.Equal("Field 2", garage.Location(bought));
        var gas = sim.World.PoiById("gas")!;
        // North of the gas station, on the grass.
        sim.Machines.Teleport(bought, gas.Position - new Vector2(0f, gas.Def.D * 0.5f + 42f) - bought.Footprint.Center + bought.Position, 0f);
        Assert.Equal("40 m from Gas Station", garage.Location(bought));
    }

    [Fact]
    public void AMachinesValueFallsWithItsAgeHoursAndWear()
    {
        var sim = TestContent.NewSim();
        var t = sim.Shop.Buy(sim.Content.Machines["tractor_95"])!;
        var price = sim.Shop.Price(t.Def);
        // New, it sells for 80% of its price.
        Assert.Equal(MathF.Round(0.8f * price), sim.Garage.Value(t));

        t.AgeMonths = 12;
        Assert.Equal(MathF.Round(0.75f * price), sim.Garage.Value(t));
        t.OperatingHours = 60;
        Assert.Equal(MathF.Round(0.75f * 0.9f * price), sim.Garage.Value(t));
        // Less what repairing and repainting it would cost.
        t.Get<Wearable>()!.Condition = 0.5f;
        t.Get<Wearable>()!.Paint = 0.5f;
        Assert.Equal(MathF.Round(0.75f * 0.9f * price - price * 0.005f - price * 0.01f), sim.Garage.Value(t), 0);
        // Never under 3% of its price.
        t.OperatingHours = 1000;
        Assert.Equal(MathF.Round(0.03f * price), sim.Garage.Value(t));

        // An implement's hours weigh more: to the power 1.3.
        var trailer = sim.Shop.Buy(sim.Content.Machines["trailer_16"])!;
        trailer.AgeMonths = 12;
        trailer.OperatingHours = 60;
        var life = 1.0 - Math.Pow(60, 1.3) / 600.0;
        Assert.Equal(MathF.Round((float)(sim.Shop.Price(trailer.Def) * 0.75 * life)), sim.Garage.Value(trailer), 0);
    }

    [Fact]
    public void MachinesGrowOlderEachMonth()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.All[0];
        Assert.Equal(0, t.AgeMonths);
        TestContent.SkipTo(sim, 11, 1, 1f);
        Assert.Equal(3, t.AgeMonths);
        var loaded = SaveGame.Load(TestContent.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(3, loaded.Machines.ById(t.Id)!.AgeMonths);
    }

    [Fact]
    public void SellingAMachinePaysItsValueAndTakesItAway()
    {
        var sim = TestContent.NewSim();
        var sold = Record<MachineSold>(sim);
        var (t, trailer) = TrailerAt(sim, new Vector2(269f, 300f), "wheat", 0f);
        sim.Player.Enter(t);
        var value = sim.Garage.Value(t);
        var money = sim.Economy.Money;

        Assert.True(sim.Garage.Sell(t));
        Assert.DoesNotContain(t, sim.Machines.All);
        Assert.Null(sim.Player.Vehicle);
        // What it pulled stays, unhitched.
        Assert.Contains(trailer, sim.Machines.All);
        Assert.Null(trailer.Parent);
        Assert.Equal(money + value, sim.Economy.Money);
        Assert.Equal(value, sim.Economy.Ledger.Today[MoneyCategory.Machines]);
        Assert.Equal((t, value), (sold.Single().Machine, sold.Single().Price));
        Assert.Contains(sim.Notifications.Items, n => n.Text == $"Sold the Fieldmaster 95 for ${value:N0}");
    }

    [Fact]
    public void ALeasedMachineIsGivenBackForNothing()
    {
        var sim = TestContent.NewSim();
        var returned = Record<MachineReturned>(sim);
        var t = sim.Shop.Lease(sim.Content.Machines["tractor_125"])!;
        t.OperatingHours = 0.5;
        sim.Tick(Dt);
        var money = sim.Economy.Money;

        Assert.True(sim.Garage.Sell(t));
        Assert.DoesNotContain(t, sim.Machines.All);
        Assert.Equal(money, sim.Economy.Money);
        Assert.Same(t, returned.Single().Machine);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Gave the leased Fieldmaster 125 back: $2,989 for the lease in all");
    }

    [Fact]
    public void SomeMachinesCannotBeSold()
    {
        var sim = TestContent.NewSim();
        var npc = sim.Machines.Spawn("tractor_95", new Vector2(300f, 300f), 0f, Farm.None);
        Assert.Equal("The Fieldmaster 95 is not the farm's", sim.Garage.SellBlocker(npc));
        var contract = sim.Machines.Spawn("tractor_95", new Vector2(320f, 300f), 0f);
        contract.LeaseContract = 7;
        Assert.Equal("It goes back when its contract ends", sim.Garage.SellBlocker(contract));

        var tractor = sim.Machines.All.First(m => m.Def.Id == "tractor_125");
        sim.Machines.Teleport(tractor, sim.World.FieldById(4)!.Shape.Centroid, 0f);
        TestContent.OwnField4(sim);
        sim.Player.Enter(tractor);
        sim.Perform(InputActions.Helper);
        Assert.Equal("The helper is working: dismiss them first", sim.Garage.SellBlocker(tractor));
        Assert.False(sim.Garage.Sell(tractor));
        Assert.Contains(tractor, sim.Machines.All);
    }

    [Fact]
    public void AMechanicComesOutForMoreThanTheWorkshopCharges()
    {
        var sim = TestContent.NewSim();
        var workshop = sim.World.PoiById("workshop")!.Get<Pois.Components.Workshop>()!;
        var t = sim.Machines.All.First(m => m.Def.Id == "tractor_95");
        t.Get<Wearable>()!.Condition = 0.5f;
        t.Get<Wearable>()!.Paint = 0.25f;
        var atWorkshop = (sim.Pois.RepairPrice(workshop, t), sim.Pois.RepaintPrice(workshop, t));
        Assert.Equal((72000f / 100f * 0.5f, 72000f * 0.02f * 0.75f), atWorkshop);
        Assert.Equal((workshop, 1.2f), sim.Garage.Service(t));
        Assert.Equal(1.2f * atWorkshop.Item1, sim.Garage.RepairPrice(t), 2);
        Assert.Equal(1.2f * atWorkshop.Item2, sim.Garage.RepaintPrice(t), 2);

        var money = sim.Economy.Money;
        var repaired = Record<MachineRepaired>(sim);
        Assert.True(sim.Garage.Repair(t));
        Assert.True(sim.Garage.Repaint(t));
        Assert.Equal((1f, 1f), (t.Get<Wearable>()!.Condition, t.Get<Wearable>()!.Paint));
        Assert.Equal(money - 1.2f * (atWorkshop.Item1 + atWorkshop.Item2), sim.Economy.Money, 1);
        Assert.Equal(-1.2f * (atWorkshop.Item1 + atWorkshop.Item2), sim.Economy.Ledger.Today[MoneyCategory.Maintenance], 1);
        Assert.Same(workshop.Poi, repaired.Single().Poi);
        Assert.Contains(sim.Notifications.Items, n => n.Text.StartsWith("Repainted Fieldmaster 95 for $"));
        Assert.Equal("Nothing to repair", sim.Garage.RepairBlocker(t));
        Assert.False(sim.Garage.Repaint(t));
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Nothing to repaint");

        // Options too; in the workshop's bay, at its own prices.
        var dual = t.Def.Configure(Options(("wheels", "dual")));
        Assert.Equal(1.2f * sim.Pois.ConfigurePrice(workshop, t, dual), sim.Garage.ConfigurePrice(t, dual), 2);
        sim.Machines.Teleport(t, workshop.Bay.Area.Center, 0f);
        Assert.Equal((workshop, 1f), sim.Garage.Service(t));
        Assert.Equal(sim.Pois.ConfigurePrice(workshop, t, dual), sim.Garage.ConfigurePrice(t, dual));
        Assert.True(sim.Garage.Configure(t, Options(("wheels", "dual"))));
        Assert.Equal("dual", t.Def.Choices["wheels"]);

        // Only while the workshop is open.
        sim.Machines.Teleport(t, new Vector2(269f, 300f), 0f);
        t.Get<Wearable>()!.Condition = 0.5f;
        sim.SkipHours(14);
        Assert.Equal("Workshop is closed: open 7:00–20:00", sim.Garage.RepairBlocker(t));
        Assert.Equal("Workshop is closed: open 7:00–20:00", sim.Garage.ConfigureBlocker(t, t.Def.Configure(Options(("color", "red")))));
    }

    [Fact]
    public void PaintWearsAsTheMachineDrives()
    {
        var sim = TestContent.NewSim();
        // On the road, heading east.
        var t = sim.Machines.Spawn("tractor_125", new Vector2(60f, 248f), MathF.PI / 2f);
        t.Get<Drivable>()!.Controller = new ManualController { Input = new VehicleInput { Throttle = 0.3f } };
        var moving = 0f;
        for (var s = 0f; s < 60f; s += Dt)
        {
            sim.Tick(Dt);
            if (MathF.Abs(t.Speed) > 0.05f) moving += Dt;
        }
        // Worn through in 60 hours on the road.
        Assert.Equal(moving / (60f * 3600f), t.Get<Wearable>()!.PaintWear, 6);

        var loaded = SaveGame.Load(TestContent.Content, SaveGame.Capture(sim, "test")).Sim;
        Assert.Equal(t.Get<Wearable>()!.Paint, loaded.Machines.ById(t.Id)!.Get<Wearable>()!.Paint);
    }
}
