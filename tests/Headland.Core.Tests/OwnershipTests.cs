using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Economics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Ownership;

namespace Headland.Core.Tests;

public class OwnershipTests
{
    [Fact]
    public void TheFarmStartsWithItsYardAndThreeFields()
    {
        var sim = TestContent.NewSim();
        Assert.Equal("Brookfield Farm", sim.Farms.Player.Name);
        Assert.Equal([1, 2, 3, 4], sim.Farms.FarmlandOf(Farm.PlayerId).Select(l => l.Id));
        Assert.Equal([1, 2, 3], sim.Farms.FarmlandOf(Farm.PlayerId).SelectMany(l => l.Fields).Select(f => f.Id).Order());
        Assert.All(sim.Machines.All, m => Assert.Equal(Farm.PlayerId, m.FarmId));

        var neighbor = sim.World.FarmlandById(7)!;
        Assert.Equal(Farm.None, neighbor.FarmId);
        Assert.Equal("Jonas Brandt", sim.Farms.OwnerName(neighbor));
        Assert.True(sim.Farms.Owns(Farm.PlayerId, new Vector2(150f, 200f)));
        Assert.False(sim.Farms.Owns(Farm.PlayerId, new Vector2(260f, 300f)));
        Assert.False(sim.Farms.Owns(Farm.None, new Vector2(260f, 300f)));
    }

    [Fact]
    public void ParcelsChangeHandsWithAnEvent()
    {
        var sim = TestContent.NewSim();
        var changes = new List<FarmlandOwnerChanged>();
        sim.Events.Subscribe<FarmlandOwnerChanged>(changes.Add);
        var land = sim.World.FarmlandById(5)!;

        sim.Farms.SetOwner(land, Farm.PlayerId);
        sim.Farms.SetOwner(land, Farm.PlayerId);
        Assert.Equal([new FarmlandOwnerChanged(land, Farm.None, Farm.PlayerId)], changes);
        Assert.Equal("Brookfield Farm", sim.Farms.OwnerName(land));
        Assert.True(sim.Farms.Owns(Farm.PlayerId, new Vector2(260f, 300f)));
        Assert.Throws<ArgumentException>(() => sim.Farms.SetOwner(land, 9));

        sim.Farms.SetOwner(land, Farm.None);
        Assert.Equal("Tom Aldridge", sim.Farms.OwnerName(land));
    }

    [Fact]
    public void ParcelsAreBoughtAndSoldBackForTheirPrice()
    {
        var sim = TestContent.NewSim();
        var bought = PoiTests.Record<FarmlandBought>(sim);
        var land = sim.World.FarmlandById(5)!;
        var money = sim.Economy.Money;
        // 126 x 114 m: 1.4364 ha at $20,000.
        Assert.Equal(28_728f, sim.Farms.Price(land));

        Assert.True(sim.Farms.Buy(land));
        Assert.Equal((Farm.PlayerId, money - 28_728f), (land.FarmId, sim.Economy.Money));
        Assert.Equal(-28_728f, sim.Economy.Ledger.Today[MoneyCategory.Land]);
        Assert.Equal([new FarmlandBought(land, Farm.PlayerId, 28_728f)], bought);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Bought Farmland 5 (1.44 ha) for $28,728");
        Assert.False(sim.Farms.Buy(land));
        Assert.Equal("Farmland 5 is yours already", sim.Farms.BuyBlocker(land));

        Assert.True(sim.Farms.Sell(land));
        Assert.Equal((Farm.None, money), (land.FarmId, sim.Economy.Money));
        Assert.Equal("Tom Aldridge", sim.Farms.OwnerName(land));
    }

    [Fact]
    public void LandNeedsTheMoneyAndTheFarmKeepsTheGroundItsBuildingsStandOn()
    {
        var sim = TestContent.NewSim();
        var neighbor = sim.World.FarmlandById(7)!;
        sim.Economy.Spend(sim.Economy.Money - 1000f, MoneyCategory.Other);
        Assert.Equal("Not enough money", sim.Farms.BuyBlocker(neighbor));
        Assert.False(sim.Farms.Buy(neighbor));
        Assert.Equal("Farmland 7 is not yours", sim.Farms.SellBlocker(neighbor));

        Assert.Equal("Farmhouse stands on Farmland 1", sim.Farms.SellBlocker(sim.World.FarmlandById(1)!));
        Assert.False(sim.Farms.Sell(sim.World.FarmlandById(1)!));
        Assert.Null(sim.Farms.SellBlocker(sim.World.FarmlandById(2)!));
    }

    [Fact]
    public void OnlyTheFarmsOwnVehiclesCanBeDrivenOrSwitchedTo()
    {
        var sim = TestContent.NewSim();
        var own = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        var npc = sim.Machines.Spawn("tractor_95", new Vector2(275f, 300f), 0f, Farm.None);

        sim.Player.Position = npc.LocalToWorld(-2f, 1f);
        Assert.Null(sim.Player.NearestEnterable(sim));
        Assert.False(sim.Player.Enter(npc));

        sim.Player.Enter(own);
        var visited = new List<Machine>();
        for (var i = 0; i < 6; i++)
        {
            sim.SwitchVehicle(1);
            visited.Add(sim.Player.Vehicle!);
        }
        Assert.DoesNotContain(npc, visited);
        Assert.Contains(own, visited);
    }

    [Fact]
    public void ImplementsOfAnotherFarmDoNotHitch()
    {
        var sim = TestContent.NewSim();
        var t = sim.Machines.Spawn("tractor_125", new Vector2(269f, 300f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(269f, 300f - 1.2f - 0.5f), 0f, Farm.None);
        Assert.Null(sim.Machines.FindAttachable(t));
        c.FarmId = Farm.PlayerId;
        Assert.Same(c, sim.Machines.FindAttachable(t)?.child);
    }

    [Fact]
    public void OwnersAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["owners"] = new MapDef
        {
            Id = "owners",
            Farmlands = [new FarmlandDef { Id = 1, Npc = "nobody", Farm = 2, W = 50, H = 50, PriceFactor = 0 }],
            Machines =
            [
                new MachineSpawnDef { Def = "tractor_125", Farm = 3 },
                new MachineSpawnDef { Def = "cultivator_3", Farm = 0, AttachToIndex = 0 },
            ],
        };
        var errors = db.Validate();
        Assert.Contains("map 'owners' farmland 1: unknown npc 'nobody'", errors);
        Assert.Contains("map 'owners' farmland 1: farm must be 0 (an NPC's) or 1 (the player's farm)", errors);
        Assert.Contains("map 'owners' farmland 1: priceFactor must be > 0", errors);
        Assert.Contains("map 'owners' machine 0: farm must be 0 (an NPC's) or 1 (the player's farm)", errors);
        Assert.Contains("map 'owners': machine 1 must belong to the same farm as the machine it attaches to", errors);
    }
}
