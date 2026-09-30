using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Input;
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
        Assert.True(poi.Trigger("unload")!.Contains(new Vector2(34f, 35f)));
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
    public void BadStationsAreReported()
    {
        var errors = Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "bad", "name": "Bad", "components": {
                 "sellingStation": { "trigger": { "w": 0 }, "minAmount": -1, "priceFactors": { "corn": 2 } },
                 "buyingStation": { "fillTypes": ["gold"], "openHours": [8] },
                 "silo": { "fillTypes": ["wheat"], "loadRate": 0 },
                 "productionPoint": { "productions": [
                   { "id": "mill", "inputs": [ { "fillType": "wheat", "amount": 0 } ], "outputs": [ { "fillType": "flour", "amount": 1, "mode": "burn" } ] },
                   { "id": "mill" } ] },
                 "workshop": { "months": [13], "configure": { "price": -5 } },
                 "washingStation": { "price": -1 } } }]
            """)).Message;
        foreach (var error in (string[])
                 [
                     "sellingStation: unload trigger: needs w and d > 0", "sellingStation: needs fillTypes",
                     "sellingStation: minAmount must be >= 0", "sellingStation: price factor for 'corn', which it does not trade",
                     "buyingStation: unknown fill type 'gold'", "buyingStation: openHours needs [from, to] hours, 0..24 and different",
                     "silo: needs an unloadTrigger, a loadTrigger or both", "silo: loadRate must be > 0",
                     "silo: needs the POI's fillUnits to keep the goods", "productionPoint: production 'mill' is defined more than once",
                     "productionPoint: needs the POI's fillUnits to keep its inputs and outputs",
                     "productionPoint: production 'mill': amounts must be > 0",
                     "productionPoint: production 'mill': output 'flour' mode must be store or sell",
                     "productionPoint: production 'mill': needs inputs and outputs", "workshop: months must be 1..12",
                     "workshop: configure.price must be >= 0", "washingStation: price must be >= 0",
                 ])
            Assert.Contains($"poi 'bad' {error}", errors);

        // What POIs do goes on POIs only, and the format from before components is refused.
        Assert.Contains("machine 'x' silo: goes on POIs only",
            Assert.Throws<ContentException>(() => TestContent.WithMachines("""[{ "id": "x", "components": { "silo": {} } }]""")).Message);
        Assert.Contains("'triggers'", Assert.Throws<ContentException>(() => TestContent.WithPois("""
            [{ "id": "old", "name": "Old", "triggers": [ { "id": "pit", "type": "unload" } ] }]
            """)).Message);
    }

    [Fact]
    public void SeedIsBoughtInTheShopsFillArea()
    {
        var sim = TestContent.NewSim();
        var bought = Record<FillBought>(sim);
        var yard = sim.World.PoiById("supplies")!.Trigger("fill")!;
        var seeder = sim.Machines.Spawn("seeder_3", yard.Area.Center + new Vector2(30f, 0f), 0f);
        var tank = seeder.Unit("seed")!;
        tank.Remove(tank.Level);
        var money = sim.Economy.Money;

        sim.Activate(seeder);
        Assert.True(tank.IsEmpty);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Park in a marked area first: a shop, silo, gas station, workshop or wash bay");

        sim.Machines.Teleport(seeder, yard.Area.Center, 0f);
        sim.Tick(Dt);
        // Parked in the fill area, its lid opened by itself.
        Assert.Equal(1, seeder.Get<Cover>()!.State);
        sim.Activate(seeder);
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
        sim.Perform(InputActions.Unload);
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
        sim.Perform(InputActions.Unload);
        Assert.False(trailer.Get<Tipper>()!.Tipping);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Grain Elevator does not buy Seeds");
    }

    [Fact]
    public void ATrailerTipsToTheSideItsKeySetsIntoThePitUnderThatSide()
    {
        var sim = TestContent.NewSim();
        var pit = sim.World.PoiById("elevator")!.Trigger("unload")!.Area;
        // Alongside the pit, outside it: only its left side reaches in.
        var left = pit.Center - pit.AxisX * (pit.HalfExtents.X - 0.5f);
        var center = left - MathUtil.LocalToWorld(Vector2.Zero, pit.Heading, new Vector2(1.9f, 0.6f));
        var t = sim.Machines.Spawn("tractor_95", center + MathUtil.Forward(pit.Heading) * 6f, pit.Heading);
        var trailer = sim.Machines.Spawn("trailer_16", center, pit.Heading);
        Assert.True(sim.Machines.Attach(t, "drawbar", trailer));
        trailer.Unit("main")!.Add("wheat", 2000f);
        sim.Player.Enter(t);
        var bed = trailer.Get<Tipper>()!;
        string? Hint(string action) => sim.Offers().Of(action)?.Label;

        // To the back, its load would fall outside the pit: the key says which side it tips to, and steps to the left.
        Assert.Equal(("Tip side (back)", "Tip"), (Hint(InputActions.TipSide), Hint(InputActions.Unload)));
        sim.Perform(InputActions.Unload);
        Assert.False(bed.Tipping);
        Assert.Contains(sim.Notifications.Items, n => n.Text == "Park with the trailer's back over an unloading area to tip");
        sim.Perform(InputActions.TipSide);
        Assert.Equal("Tip side (left)", Hint(InputActions.TipSide));
        Assert.StartsWith("Tip into Grain Elevator", Hint(InputActions.Unload));
        sim.Perform(InputActions.Unload);
        Assert.True(bed.Tipping);

        // Not while it tips.
        sim.Tick(Dt);
        Assert.Null(Hint(InputActions.TipSide));
        for (var s = 0f; s < 30f && !bed.Load.IsEmpty; s += Dt) sim.Tick(Dt);
        Assert.True(bed.Load.IsEmpty);

        var loaded = SaveGame.Load(sim.Content, SaveGame.Capture(sim, "test")).Sim.Machines.ById(trailer.Id)!;
        Assert.Equal("left", loaded.Get<Tipper>()!.Side?.Name);
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
        return SimWith(db, pois);
    }

    /// <summary>The small test world of <paramref name="db"/> (its own content) with these POIs placed.</summary>
    internal static Simulation SimWith(ContentDatabase db, params PoiPlacementDef[] pois)
    {
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
