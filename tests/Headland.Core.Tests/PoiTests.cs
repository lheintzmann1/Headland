using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.Saves;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class PoiTests
{
    private const float Dt = 1f / 60f;

    [Fact]
    public void PoisArePlacedFromTheMap()
    {
        var sim = TestContent.NewSim();
        var elevator = sim.World.PoiById("elevator")!;
        Assert.Equal(("grain_elevator", "Grain Elevator", Farm.None), (elevator.Def.Id, elevator.Name, elevator.FarmId));
        Assert.Equal(Farm.PlayerId, sim.World.PoiById("silo")!.FarmId);

        // Colliders block the way: the farm silo's two bins are round.
        var bins = sim.World.Obstacles.Where(o => o.Kind == "farm_silo");
        Assert.Equal([new Vector2(175f, 185f), new Vector2(185f, 185f)], bins.Select(o => o.Center).Order(new ByX()));
        Assert.All(bins, o => Assert.Equal((ObstacleShape.Circle, 4f), (o.Shape, o.Radius)));
    }

    [Fact]
    public void CollidersTurnWithThePoi()
    {
        var sim = SimWith(new PoiPlacementDef { Id = "silo", Type = "farm_silo", X = 30, Z = 30, HeadingDeg = 90 });
        // Facing east, the POI's left (+x) is north and its back (-z) west.
        var poi = sim.World.PoiById("silo")!;
        var centers = poi.Def.Colliders.Select(c => poi.ColliderBox(c).Center).ToList();
        Assert.Equal(new Vector2(24f, 35f), centers[0], new Near());
        Assert.Equal(new Vector2(24f, 25f), centers[1], new Near());
        Assert.True(poi.Trigger("pit")!.Contains(new Vector2(34f, 35f)));
        Assert.True(poi.Footprint.Contains(new Vector2(39f, 40f)) && !poi.Footprint.Contains(new Vector2(41f, 30f)));
    }

    [Fact]
    public void TreesKeepClearOfPois()
    {
        var sim = TestContent.NewSim();
        Assert.All(sim.World.Trees, t => Assert.All(sim.World.Pois, p => Assert.True(p.Footprint.Distance(t.Position) > 0f)));
    }

    [Fact]
    public void BadPlacementsAreReported()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["bad"] = new MapDef
        {
            Id = "bad", Size = 64,
            Pois =
            [
                new PoiPlacementDef { Id = "a", Type = "castle", X = 10, Z = 10 },
                new PoiPlacementDef { Id = "b", Type = "farmhouse", X = 10, Z = 10, Farm = 7 },
                new PoiPlacementDef { Id = "b", Type = "farmhouse", X = 100, Z = 10 },
            ],
        };
        var errors = db.Validate();
        Assert.Contains("map 'bad' poi 'a': unknown type 'castle'", errors);
        Assert.Contains(errors, e => e.StartsWith("map 'bad' poi 'b': farm must be"));
        Assert.Contains("map 'bad' poi 'b': outside the map", errors);
        Assert.Contains("map 'bad': poi 'b' is defined more than once", errors);
    }

    [Fact]
    public void BadTriggersAndActionsAreReported()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Pois["bad"] = new PoiDef
        {
            Id = "bad",
            Triggers =
            [
                new PoiTriggerDef { Id = "pit", Type = "fill" }, new PoiTriggerDef { Id = "pit", Type = "teleport" },
                new PoiTriggerDef { Id = "spout", Type = "load" },
            ],
            Actions =
            [
                new PoiActionDef { Type = "sell", Trigger = "pit", FillTypes = ["wheat"] },
                new PoiActionDef { Type = "buy", Trigger = "gate", FillTypes = ["gold"] },
                new PoiActionDef { Type = "juggle", Trigger = "pit" },
                new PoiActionDef { Type = "store", Trigger = "pit", FillTypes = ["wheat"], OpenHours = [8], Months = [13] },
                new PoiActionDef
                {
                    Type = "process", Trigger = "pit", Inputs = [new FillAmountDef { FillType = "wheat", Amount = 0 }],
                    Outputs = [new ProcessOutputDef { FillType = "wheat", Amount = 1, Mode = "burn" }],
                },
            ],
        };
        var errors = db.Validate();
        Assert.Contains("poi 'bad': trigger 'pit' is defined more than once", errors);
        Assert.Contains("poi 'bad' trigger 'pit': unknown type 'teleport'", errors);
        Assert.Contains("poi 'bad' trigger 'spout': load triggers need a storage", errors);
        Assert.Contains("poi 'bad' sell action: works at unload triggers, not fill", errors);
        Assert.Contains("poi 'bad' buy action: trigger 'gate' not found", errors);
        Assert.Contains("poi 'bad' buy action: unknown fill type 'gold'", errors);
        Assert.Contains("poi 'bad': unknown action type 'juggle'", errors);
        Assert.Contains("poi 'bad' store action: the poi has no storage", errors);
        Assert.Contains("poi 'bad' store action: openHours needs [from, to] hours, 0..24 and different", errors);
        Assert.Contains("poi 'bad' store action: months must be 1..12", errors);
        Assert.Contains("poi 'bad' process action: works without a trigger", errors);
        Assert.Contains("poi 'bad' process action: output 'wheat' mode must be store or sell", errors);
        Assert.Contains("poi 'bad' process action: amounts must be > 0", errors);
    }

    [Fact]
    public void SeedIsBoughtInTheShopsFillArea()
    {
        var sim = TestContent.NewSim();
        var bought = Record<FillBought>(sim);
        var yard = sim.World.PoiById("supplies")!.Trigger("yard")!;
        var seeder = sim.Machines.Spawn("seeder_3", yard.Area.Center + new Vector2(30f, 0f), 0f);
        var tank = seeder.Unit("seed")!;
        tank.Remove(tank.Level);
        var money = sim.Economy.Money;

        sim.Pois.Use(seeder);
        Assert.True(tank.IsEmpty);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Park in a marked area first: a shop, silo, gas station, workshop or wash bay");

        sim.Machines.Teleport(seeder, yard.Area.Center, 0f);
        sim.Pois.Use(seeder);
        var cost = 1600f * sim.Economy.Price("seeds", sim.Clock.Month);
        Assert.Equal(1600f, tank.Level);
        Assert.Equal(money - cost, sim.Economy.Money, 0);
        Assert.Equal([new FillBought(seeder, yard.Poi, "seeds", 1600f, cost)], bought);
    }

    [Fact]
    public void ALoadTippedAcrossASaveIsSoldOnce()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = TrailerAt(sim, new Vector2(441f, 230f), "wheat", 8000f);
        sim.Player.Enter(t);
        sim.CommandUnload();
        while (trailer.Unit("main")!.Level > 4000f) sim.Tick(Dt);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim;
        var sold = Record<FillSold>(loaded);
        var tipper = loaded.Machines.ById(trailer.Id)!;
        var bed = tipper.Get<Tipper>()!;
        for (var s = 0f; s < 60f && (bed.Tipping || bed.Anim > 0f); s += Dt) loaded.Tick(Dt);

        var sale = Assert.Single(sold);
        Assert.Equal(("elevator", 8000f), (sale.Poi.Id, MathF.Round(sale.Amount)));
        Assert.Equal(SaleIncome(sim, "wheat", 8000f), sale.Income, 0);
    }

    [Fact]
    public void ThePoiSaysWhatItDoesNotBuy()
    {
        var sim = TestContent.NewSim();
        var (t, trailer) = TrailerAt(sim, new Vector2(441f, 230f), "wheat", 1000f);
        trailer.Unit("main")!.FillType = "seeds";
        sim.Player.Enter(t);
        sim.CommandUnload();
        Assert.False(trailer.Get<Tipper>()!.Tipping);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Grain Elevator does not buy Seeds");
    }

    /// <summary>A tractor facing east with a loaded trailer whose center is at <paramref name="trailerCenter"/>.</summary>
    internal static (Machine tractor, Machine trailer) TrailerAt(Simulation sim, Vector2 trailerCenter, string fillType, float amount)
    {
        var t = sim.Machines.Spawn("tractor_95", trailerCenter + new Vector2(6f, 0f), MathF.PI / 2f);
        var trailer = sim.Machines.Spawn("trailer_16", trailerCenter, MathF.PI / 2f);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add(fillType, amount);
        return (t, trailer);
    }

    /// <summary>What a load sells for at the market price, as the default demand drops under it (4% per 100,000 units).</summary>
    internal static float SaleIncome(Simulation sim, string fillType, float amount) =>
        sim.Economy.Price(fillType, sim.Clock.Month) * amount * (1f - 0.04f * amount / 200_000f);

    internal static List<T> Record<T>(Simulation sim) where T : IGameEvent
    {
        var list = new List<T>();
        sim.Events.Subscribe<T>(list.Add);
        return list;
    }

    /// <summary>The small test world with these POIs placed.</summary>
    internal static Simulation SimWith(params PoiPlacementDef[] pois) => SimWith([], pois);

    /// <summary>The small test world with these POI types added and these POIs placed.</summary>
    internal static Simulation SimWith(PoiDef[] types, params PoiPlacementDef[] pois)
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        foreach (var type in types) db.Pois[type.Id] = type;
        db.Maps["pois"] = new MapDef
        {
            Id = "pois", Name = "POIs", Size = 64, Seed = 3, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands = [new FarmlandDef { Id = 1, Npc = "hendricks", Farm = 1, X = 0, Z = 0, W = 64, H = 64 }],
            Pois = pois,
            PlayerX = 2, PlayerZ = 2,
        };
        db.Game.Map = "pois";
        return Simulation.Create(db);
    }

    private sealed class ByX : IComparer<Vector2>
    {
        public int Compare(Vector2 a, Vector2 b) => a.X.CompareTo(b.X);
    }

    private sealed class Near : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;
        public int GetHashCode(Vector2 v) => 0;
    }
}
