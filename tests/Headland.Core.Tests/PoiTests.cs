using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Ownership;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class PoiTests
{
    [Fact]
    public void PoisArePlacedFromTheMap()
    {
        var sim = TestContent.NewSim();
        var elevator = sim.World.PoiById("elevator")!;
        Assert.Equal(("grain_elevator", "Grain Elevator", Farm.None), (elevator.Def.Id, elevator.Name, elevator.FarmId));
        Assert.Equal(Farm.PlayerId, sim.World.PoiById("silo")!.FarmId);

        // Solid parts block the way: the farm silo's two bins are round, the shop's pallets can be driven over.
        var bins = sim.World.Obstacles.Where(o => o.Kind == "silo" && Vector2.Distance(o.Center, new Vector2(180f, 185f)) < 6f);
        Assert.Equal([new Vector2(175f, 185f), new Vector2(185f, 185f)], bins.Select(o => o.Center).Order(new ByX()));
        Assert.All(bins, o => Assert.Equal((ObstacleShape.Circle, 4f), (o.Shape, o.Radius)));
        Assert.DoesNotContain(sim.World.Obstacles, o => o.Kind == "pallets");
    }

    [Fact]
    public void PartsTurnWithThePoi()
    {
        var sim = SimWith(new PoiPlacementDef { Id = "silo", Type = "farm_silo", X = 30, Z = 30, HeadingDeg = 90 });
        // Facing east, the POI's left (+x) is north.
        var poi = sim.World.PoiById("silo")!;
        var centers = poi.Def.Parts.Select(p => poi.PartBox(p).Center).ToList();
        Assert.Equal(new Vector2(30f, 35f), centers[0], new Near());
        Assert.Equal(new Vector2(30f, 25f), centers[1], new Near());
        Assert.True(poi.Footprint.Contains(new Vector2(34f, 39f)) && !poi.Footprint.Contains(new Vector2(36f, 30f)));
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

    /// <summary>The small test world with these POIs placed.</summary>
    internal static Simulation SimWith(params PoiPlacementDef[] pois)
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
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
