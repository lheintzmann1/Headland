using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Helpers;
using Headland.Core.Machines;
using Headland.Core.World;

namespace Headland.Core.Tests;

public class PolygonTests
{
    // An L: a 20 x 20 square with its top-right 10 x 10 quarter missing.
    private static readonly Polygon L = new([new(0, 0), new(10, 0), new(10, 10), new(20, 10), new(20, 20), new(0, 20)]);

    [Fact]
    public void MeasuresAreaCentroidAndBounds()
    {
        Assert.Equal(300f, L.Area, 3);
        Assert.Equal(new Vector2(0, 0), L.Min);
        Assert.Equal(new Vector2(20, 20), L.Max);
        // Centroid of the L: (100·(5,5) + 200·(10,15)) / 300.
        Assert.Equal(25f / 3f, L.Centroid.X, 3);
        Assert.Equal(35f / 3f, L.Centroid.Y, 3);
    }

    [Fact]
    public void ContainsAndDistanceFollowTheNotch()
    {
        Assert.True(L.Contains(new Vector2(5, 5)));
        Assert.False(L.Contains(new Vector2(15, 5)));
        Assert.True(L.Contains(new Vector2(15, 15)));
        Assert.Equal(0f, L.Distance(new Vector2(15, 15)));
        Assert.Equal(2f, L.Distance(new Vector2(15, 8)), 4);
        Assert.Equal(5f, L.Distance(new Vector2(-5, 10)), 4);
    }

    [Fact]
    public void RasterizationMatchesContains()
    {
        var cells = new HashSet<(int, int)>();
        L.Rasterize(0.5f, 60, 60, (x, z) => Assert.True(cells.Add((x, z))));
        Assert.Equal(300 * 4, cells.Count);
        for (var z = 0; z < 60; z++)
        for (var x = 0; x < 60; x++)
            Assert.Equal(L.Contains(new Vector2((x + 0.5f) * 0.5f, (z + 0.5f) * 0.5f)), cells.Contains((x, z)));
    }

    [Fact]
    public void ExtentCoversTheWholeStrip()
    {
        Assert.Equal((0f, 20f), L.Extent(alongZ: true, 2f, 4f));
        Assert.Equal((10f, 20f), L.Extent(alongZ: true, 12f, 14f));
        // A strip straddling the notch reaches the lowest point under any part of it.
        Assert.Equal((0f, 20f), L.Extent(alongZ: true, 8f, 12f));
        Assert.Equal((0f, 10f), L.Extent(alongZ: false, 2f, 4f));
        Assert.Null(L.Extent(alongZ: true, 25f, 30f));
    }
}

public class FarmlandTests
{
    [Fact]
    public void DefaultMapHasParcelsHoldingItsFields()
    {
        var world = WorldGen.Generate(TestContent.Content.Map, TestContent.Content);
        Assert.Equal(7, world.Farmlands.Count);
        Assert.All(world.Fields, f => Assert.Contains(f, world.FarmlandById(f.FarmlandId)!.Fields));

        // Field 6 is a polygon inside the polygon farmland 7.
        var f6 = world.FieldById(6)!;
        Assert.Equal(7, f6.FarmlandId);
        Assert.Equal(6, f6.Shape.Points.Count);
        var cells = world.Layers.FieldId.Count(id => id == 6);
        Assert.Equal(f6.Shape.Area / WorldMap.CellArea, cells, cells * 0.01);

        Assert.Same(world.FarmlandById(1), world.FarmlandAt(new Vector2(150f, 200f)));
        Assert.Null(world.FarmlandAt(new Vector2(200f, 248f))); // the crossroads
        Assert.Equal(0, world.Layers.FieldId[world.CellIndex(4, 4)]);
    }

    [Fact]
    public void FieldNumbersGoPast255()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["big"] = new MapDef
        {
            Id = "big", Size = 64, HillAmplitude = 0f, ScatteredTreesPerHa = 0f,
            Farmlands = [new FarmlandDef { Id = 1000, Npc = "hendricks", X = 0, Z = 0, W = 64, H = 64 }],
            Fields = [new FieldDef { Id = 300, X = 8, Z = 8, W = 20, H = 20 }],
        };
        Assert.Empty(db.Validate());
        var world = WorldGen.Generate(db.Maps["big"], db);
        Assert.Equal(300, world.Layers.FieldId[world.CellIndex(20, 20)]);
        Assert.Equal(1000, world.Fields.Single().FarmlandId);
    }

    [Fact]
    public void ShapesAndParcelsAreValidated()
    {
        var db = ContentDatabase.Load(new FileSystemContentSource(TestContent.DataDir));
        db.Maps["bad"] = new MapDef
        {
            Id = "bad",
            Farmlands =
            [
                new FarmlandDef { Id = 1, Npc = "hendricks", W = 50, H = 50 },
                new FarmlandDef { Id = 2, Npc = "hendricks", Polygon = [[0, 0], [10, 0]] },
            ],
            Fields =
            [
                new FieldDef { Id = 1, X = 10, Z = 10, W = 10, H = 10 },
                new FieldDef { Id = 2, X = 100, Z = 100, W = 10, H = 10 },
                new FieldDef { Id = 3, Polygon = [[0, 0], [1, 1], [2, 2]] },
            ],
        };
        var errors = db.Validate();
        Assert.Contains("map 'bad' farmland 2: polygon needs at least 3 points", errors);
        Assert.Contains("map 'bad' field 2: not inside a farmland", errors);
        Assert.Contains("map 'bad' field 3: polygon has no area", errors);
        Assert.DoesNotContain(errors, e => e.Contains("field 1"));
    }

    // A trapezoid inside field 4 (grass): narrower at the far end, with a slanted right edge.
    private static readonly FieldInfo Trapezoid = new()
    {
        Id = 4, Shape = new Polygon([new(240, 290), new(270, 290), new(262, 330), new(244, 326)]),
    };

    [Fact]
    public void HelperWorksAFieldThatIsNotARectangle()
    {
        var sim = TestContent.NewSim();
        TestContent.OwnField4(sim);
        var t = sim.Machines.Spawn("tractor_125", new Vector2(242f, 280f), 0f);
        var c = sim.Machines.Spawn("cultivator_3", new Vector2(242f, 278f), 0f);
        sim.Machines.Attach(t, "rear", c);
        var helper = sim.HireHelper(t, Trapezoid);
        for (var s = 0f; s < 900f && !helper.Finished; s += 1f / 60f) sim.Tick(1f / 60f);
        Assert.True(helper.Finished, $"helper stuck at waypoint {helper.FieldWork!.Driver.Index}/{helper.FieldWork!.Path.Points.Count}");

        int inside = 0, insideDone = 0, outsideDone = 0;
        for (var cz = 0; cz < sim.World.CellsZ; cz++)
        for (var cx = 0; cx < sim.World.CellsX; cx++)
        {
            var p = sim.World.CellCenter(cx, cz);
            var cultivated = sim.World.Layers.Ground[sim.World.CellIndex(cx, cz)] == (byte)GroundType.Cultivated;
            var d = Trapezoid.Shape.Distance(p);
            if (d == 0f)
            {
                inside++;
                if (cultivated) insideDone++;
            }
            else if (d < 20f && cultivated) outsideDone++;
        }
        Assert.True(insideDone > inside * 0.99f, $"coverage {insideDone * 100f / inside:F1}%");
        Assert.Equal(0, outsideDone);
    }

    [Fact]
    public void LanesOfAPolygonEndPastItsEdgeUnderTheWholeSwath()
    {
        var path = FieldPlanner.Lanes(Trapezoid, new LanePlan(3f, 4f, 2f));
        Assert.Equal(10, path.LaneCount);
        for (var k = 1; k < path.Points.Count; k++)
        {
            if (path.Segments[k] != PathSegment.Work) continue;
            // Every working lane starts and ends outside the field.
            Assert.False(Trapezoid.Contains(path.Points[k - 1]));
            Assert.False(Trapezoid.Contains(path.Points[k]));
        }
        // Lanes closer than a turning circle, no reversing: bulb turns, out 1 + √3 turning radii at most.
        Assert.True(path.Points.Max(Trapezoid.Shape.Distance) <= 2f + (1f + MathF.Sqrt(3f)) * 4f + 0.5f);
    }
}
