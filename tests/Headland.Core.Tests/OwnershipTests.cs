using System.Numerics;
using Headland.Core.Content;
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
            Farmlands = [new FarmlandDef { Id = 1, Npc = "nobody", Farm = 2, W = 50, H = 50 }],
            Machines =
            [
                new MachineSpawnDef { Def = "tractor_125", Farm = 3 },
                new MachineSpawnDef { Def = "cultivator_3", Farm = 0, AttachToIndex = 0 },
            ],
        };
        var errors = db.Validate();
        Assert.Contains("map 'owners' farmland 1: unknown npc 'nobody'", errors);
        Assert.Contains("map 'owners' farmland 1: farm must be 0 (an NPC's) or 1 (the player's farm)", errors);
        Assert.Contains("map 'owners' machine 0: farm must be 0 (an NPC's) or 1 (the player's farm)", errors);
        Assert.Contains("map 'owners': machine 1 must belong to the same farm as the machine it attaches to", errors);
    }
}
