using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Pois;

namespace Headland.Core.World;

/// <summary>Builds a <see cref="WorldMap"/> from a hand-authored layout plus procedural hills, soils and trees.</summary>
public static class WorldGen
{
    public static WorldMap Generate(MapDef map, ContentDatabase content)
    {
        var world = new WorldMap(map.Size) { TileSize = map.TileSize };
        var rng = new Rng((ulong)map.Seed * 7919UL + 17UL);

        BuildNetworks(world, map);
        BuildHeights(world, map);
        BuildGround(world, map, content);
        BuildPois(world, map, content);
        BuildTrees(world, map, rng);
        world.MarkAllDirty(crop: true);
        return world;
    }

    private static void BuildNetworks(WorldMap world, MapDef map)
    {
        var maxTile = (int)(map.Size / map.TileSize);
        foreach (var def in map.Networks)
        {
            var tiles = new Dictionary<(int, int), NetworkTile>();
            foreach (var r in def.Runs)
            {
                var x0 = Math.Min(r.X0, r.X1);
                var x1 = Math.Max(r.X0, r.X1);
                var z0 = Math.Min(r.Z0, r.Z1);
                var z1 = Math.Max(r.Z0, r.Z1);
                for (var z = z0; z <= z1; z++)
                for (var x = x0; x <= x1; x++)
                    tiles.TryAdd((x, z), new NetworkTile { X = x, Z = z, AlongX = x1 > x0 });
            }

            foreach (var t in tiles.Values)
            {
                var mask = 0;
                // Connected to a neighbor tile, or to the map edge when the run leaves the map.
                if (tiles.ContainsKey((t.X, t.Z - 1)) || t.Z == 0) mask |= 1;
                if (tiles.ContainsKey((t.X + 1, t.Z)) || t.X == maxTile - 1) mask |= 2;
                if (tiles.ContainsKey((t.X, t.Z + 1)) || t.Z == maxTile - 1) mask |= 4;
                if (tiles.ContainsKey((t.X - 1, t.Z)) || t.X == 0) mask |= 8;
                t.Mask = mask;
            }

            var network = new TileNetwork { Id = def.Id, Style = def.Style, Ground = ParseGround(def.Ground), Width = def.Width };
            network.Tiles.AddRange(tiles.Values.OrderBy(t => t.Z).ThenBy(t => t.X));
            world.Networks.Add(network);
        }
    }

    /// <summary>
    /// The drawn band of a tile: a center square plus one arm per connection. Dead ends and lone tiles
    /// are drawn as straights, so their band spans the whole tile along the run.
    /// </summary>
    public static int EffectiveMask(NetworkTile t)
    {
        if (t.Mask is 2 or 8) return 2 | 8;
        if (t.Mask is 1 or 4) return 1 | 4;
        if (t.Mask == 0) return t.AlongX ? 2 | 8 : 1 | 4;
        return t.Mask;
    }

    public static bool InBand(Vector2 local, float halfWidth, int mask)
    {
        var alongZ = MathF.Abs(local.X) <= halfWidth;
        var alongX = MathF.Abs(local.Y) <= halfWidth;
        return (alongZ && alongX)
               || (alongZ && local.Y < 0 && (mask & 1) != 0)
               || (alongX && local.X > 0 && (mask & 2) != 0)
               || (alongZ && local.Y > 0 && (mask & 4) != 0)
               || (alongX && local.X < 0 && (mask & 8) != 0);
    }

    private static void BuildHeights(WorldMap world, MapDef map)
    {
        var hm = world.Height;
        var noise = new ValueNoise(map.Seed);
        var n = hm.Stride;
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
            hm.Heights[z * n + x] = map.HillAmplitude * noise.Fbm(x / 170f, z / 170f, 4, 2.1f, 0.45f);

        var blurred = Blur(hm.Heights, n, 14);
        var weight = new float[n * n];

        // Networks (roads, tracks, streams): smoothed along their band, with a soft shoulder.
        var carve = new float[n * n];
        for (var k = 0; k < world.Networks.Count; k++)
        {
            var def = map.Networks[k];
            foreach (var t in world.Networks[k].Tiles)
            foreach (var (rx, rz, rw, rh) in BandRects(t, map.TileSize, def.Width))
            {
                Stamp(weight, n, rx, rz, rw, rh, 6f, def.Flatten);
                if (def.Carve > 0f) Stamp(carve, n, rx, rz, rw, rh, 2.5f, def.Carve);
            }
        }
        // Fields: gently smoothed so work areas stay even.
        foreach (var f in map.Fields)
            StampShape(weight, n, f.Shape(), 10f, 0.55f);

        for (var i = 0; i < weight.Length; i++)
            hm.Heights[i] = MathUtil.Lerp(hm.Heights[i], blurred[i], weight[i]) - carve[i];

        // Yards: flat at their mean height.
        foreach (var y in map.Yards)
        {
            double sum = 0;
            var count = 0;
            for (var z = (int)y.Z; z <= (int)(y.Z + y.H); z++)
            for (var x = (int)y.X; x <= (int)(y.X + y.W); x++)
            {
                sum += hm.At(x, z);
                count++;
            }
            var target = (float)(sum / Math.Max(1, count));
            var yardWeight = new float[n * n];
            Stamp(yardWeight, n, y.X, y.Z, y.W, y.H, 10f, 1f);
            for (var i = 0; i < yardWeight.Length; i++)
                hm.Heights[i] = MathUtil.Lerp(hm.Heights[i], target, yardWeight[i]);
        }
    }

    /// <summary>World-space rectangles (x, z, w, h) covering a tile's band: center square plus arms.</summary>
    public static IEnumerable<(float x, float z, float w, float h)> BandRects(NetworkTile t, float tileSize, float width)
    {
        var mask = EffectiveMask(t);
        var half = width * 0.5f;
        var cx = (t.X + 0.5f) * tileSize;
        var cz = (t.Z + 0.5f) * tileSize;
        var x0 = t.X * tileSize;
        var z0 = t.Z * tileSize;
        var arm = tileSize * 0.5f - half;
        yield return (cx - half, cz - half, width, width);
        if ((mask & 1) != 0) yield return (cx - half, z0, width, arm);
        if ((mask & 2) != 0) yield return (cx + half, cz - half, arm, width);
        if ((mask & 4) != 0) yield return (cx - half, cz + half, width, arm);
        if ((mask & 8) != 0) yield return (x0, cz - half, arm, width);
    }

    /// <summary>Writes max(weight) for vertices inside a rect with a smooth falloff margin.</summary>
    private static void Stamp(float[] weight, int n, float rx, float rz, float rw, float rh, float margin, float strength)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(rx - margin));
        var x1 = Math.Min(n - 1, (int)MathF.Ceiling(rx + rw + margin));
        var z0 = Math.Max(0, (int)MathF.Floor(rz - margin));
        var z1 = Math.Min(n - 1, (int)MathF.Ceiling(rz + rh + margin));
        for (var z = z0; z <= z1; z++)
        for (var x = x0; x <= x1; x++)
        {
            var dx = MathF.Max(0f, MathF.Max(rx - x, x - (rx + rw)));
            var dz = MathF.Max(0f, MathF.Max(rz - z, z - (rz + rh)));
            var d = MathF.Sqrt(dx * dx + dz * dz);
            var w = strength * (1f - MathUtil.SmoothStep(0f, margin, d));
            var i = z * n + x;
            if (w > weight[i]) weight[i] = w;
        }
    }

    /// <summary><see cref="Stamp"/> for any polygon: full weight inside, easing out over the margin.</summary>
    private static void StampShape(float[] weight, int n, Polygon shape, float margin, float strength)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(shape.Min.X - margin));
        var x1 = Math.Min(n - 1, (int)MathF.Ceiling(shape.Max.X + margin));
        var z0 = Math.Max(0, (int)MathF.Floor(shape.Min.Y - margin));
        var z1 = Math.Min(n - 1, (int)MathF.Ceiling(shape.Max.Y + margin));
        for (var z = z0; z <= z1; z++)
        for (var x = x0; x <= x1; x++)
        {
            var w = strength * (1f - MathUtil.SmoothStep(0f, margin, shape.Distance(new Vector2(x, z))));
            var i = z * n + x;
            if (w > weight[i]) weight[i] = w;
        }
    }

    private static float[] Blur(float[] src, int n, int radius)
    {
        var a = (float[])src.Clone();
        var b = new float[a.Length];
        for (var pass = 0; pass < 2; pass++)
        {
            BoxPass(a, b, n, radius, horizontal: true);
            BoxPass(b, a, n, radius, horizontal: false);
        }
        return a;
    }

    private static void BoxPass(float[] src, float[] dst, int n, int r, bool horizontal)
    {
        for (var line = 0; line < n; line++)
        {
            float sum = 0;
            var count = 0;
            for (var k = -r; k <= r; k++)
            {
                var idx = Math.Clamp(k, 0, n - 1);
                sum += horizontal ? src[line * n + idx] : src[idx * n + line];
                count++;
            }
            for (var i = 0; i < n; i++)
            {
                if (horizontal) dst[line * n + i] = sum / count;
                else dst[i * n + line] = sum / count;
                var outIdx = Math.Clamp(i - r, 0, n - 1);
                var inIdx = Math.Clamp(i + r + 1, 0, n - 1);
                sum += horizontal ? src[line * n + inIdx] - src[line * n + outIdx] : src[inIdx * n + line] - src[outIdx * n + line];
            }
        }
    }

    private static void BuildGround(WorldMap world, MapDef map, ContentDatabase content)
    {
        var L = world.Layers;
        var soilNoise = new ValueNoise(map.Seed + 101);
        var varNoise = new ValueNoise(map.Seed + 202);
        var loam = Math.Max(0, content.SoilIndex("loam"));
        var clay = content.SoilIndex("clay") is var c and >= 0 ? c : loam;
        var sand = content.SoilIndex("sand") is var s and >= 0 ? s : loam;

        for (var cz = 0; cz < world.CellsZ; cz++)
        for (var cx = 0; cx < world.CellsX; cx++)
        {
            var i = world.CellIndex(cx, cz);
            var p = world.CellCenter(cx, cz);
            var sn = soilNoise.Fbm(p.X / 150f, p.Y / 150f, 3);
            var soil = sn < -0.22f ? sand : sn > 0.25f ? clay : loam;
            var sd = content.Soils[soil];
            var v = varNoise.Fbm(p.X / 40f, p.Y / 40f, 2);
            L.Soil[i] = (byte)soil;
            L.Ground[i] = (byte)GroundType.Grass;
            L.Moisture[i] = ToByte(sd.InitialMoisture + v * 0.08f);
            L.Nitrogen[i] = (byte)Math.Clamp(sd.InitialNitrogen + v * 15f, 0f, 255f);
        }

        foreach (var forest in map.Forests)
            FillRect(world, forest.X, forest.Z, forest.W, forest.H, (i, _, _) => L.Ground[i] = (byte)GroundType.Forest);

        foreach (var y in map.Yards)
            FillRect(world, y.X, y.Z, y.W, y.H, (i, _, _) => L.Ground[i] = (byte)GroundType.Yard);

        // Only each network's band takes its ground type; the rest of the tile keeps what was there.
        var ts = map.TileSize;
        foreach (var net in world.Networks)
        foreach (var t in net.Tiles)
        {
            var center = new Vector2((t.X + 0.5f) * ts, (t.Z + 0.5f) * ts);
            var mask = EffectiveMask(t);
            FillRect(world, t.X * ts, t.Z * ts, ts, ts, (i, cx, cz) =>
            {
                if (InBand(world.CellCenter(cx, cz) - center, net.Width * 0.5f, mask)) L.Ground[i] = (byte)net.Ground;
            });
        }

        foreach (var def in map.Farmlands)
        {
            var shape = def.Shape();
            var farmland = new Farmland
            {
                Id = def.Id, Shape = shape, Npc = content.Npcs[def.Npc], FarmId = def.Farm,
                Price = MathF.Round(shape.Area / 10000f * map.FarmlandPricePerHa * def.PriceFactor),
            };
            world.Farmlands.Add(farmland);
            Fill(world, farmland.Shape, (i, _, _) => L.FarmlandId[i] = (ushort)farmland.Id);
        }

        foreach (var f in map.Fields)
        {
            var shape = f.Shape();
            var farmland = world.Farmlands.LastOrDefault(l => l.Shape.Contains(shape.Centroid));
            var field = new FieldInfo { Id = f.Id, Shape = shape, FarmlandId = farmland?.Id ?? 0 };
            world.Fields.Add(field);
            farmland?.Fields.Add(field);
            var ground = ParseGround(f.Ground);
            var cropIdx = f.Crop != null ? content.CropIndex(f.Crop) : -1;
            var crop = cropIdx >= 0 ? content.Crops[cropIdx] : null;
            byte stage = 0;
            if (crop != null && f.Stage != null)
                stage = f.Stage == "harvestable" ? (byte)crop.HarvestableStage : byte.Parse(f.Stage);
            var angle = AngleToByte(f.AngleDeg * MathUtil.Deg2Rad);

            Fill(world, shape, (i, cx, cz) =>
            {
                L.FieldId[i] = (ushort)f.Id;
                L.Ground[i] = (byte)(crop != null ? GroundType.Seeded : ground);
                L.WorkAngle[i] = angle;
                if (crop == null) return;
                L.Crop[i] = (byte)(cropIdx + 1);
                L.Stage[i] = stage;
                L.Health[i] = (byte)(225 + Rng.Hash01(cx, cz, 5) * 30);
                L.Progress[i] = crop.Stages[stage].Gdd * 0.3f * Rng.Hash01(cx, cz, 9);
            });
        }
    }

    public static byte AngleToByte(float heading)
    {
        var a = heading % MathF.PI;
        if (a < 0) a += MathF.PI;
        return (byte)Math.Clamp((int)(a / MathF.PI * 256f), 0, 255);
    }

    public static byte ToByte(float v01) => (byte)Math.Clamp((int)MathF.Round(v01 * 255f), 0, 255);

    public static GroundType ParseGround(string s) => s.ToLowerInvariant() switch
    {
        "grass" => GroundType.Grass,
        "cultivated" => GroundType.Cultivated,
        "seeded" => GroundType.Seeded,
        "stubble" => GroundType.Stubble,
        "plowed" => GroundType.Plowed,
        "dirt" => GroundType.Dirt,
        "road" => GroundType.Road,
        "yard" => GroundType.Yard,
        "water" => GroundType.Water,
        _ => throw new ContentException($"Unknown ground type '{s}'"),
    };

    private static void Fill(WorldMap world, Polygon shape, Action<int, int, int> set) =>
        shape.Rasterize(WorldMap.CellSize, world.CellsX, world.CellsZ, (cx, cz) => set(world.CellIndex(cx, cz), cx, cz));

    private static void FillRect(WorldMap world, float x, float z, float w, float h, Action<int, int, int> set)
    {
        var cx0 = Math.Max(0, (int)MathF.Round(x / WorldMap.CellSize));
        var cz0 = Math.Max(0, (int)MathF.Round(z / WorldMap.CellSize));
        var cx1 = Math.Min(world.CellsX, (int)MathF.Round((x + w) / WorldMap.CellSize));
        var cz1 = Math.Min(world.CellsZ, (int)MathF.Round((z + h) / WorldMap.CellSize));
        for (var cz = cz0; cz < cz1; cz++)
        for (var cx = cx0; cx < cx1; cx++)
            set(world.CellIndex(cx, cz), cx, cz);
    }

    private static void BuildPois(WorldMap world, MapDef map, ContentDatabase content)
    {
        foreach (var p in map.Pois)
        {
            var poi = new Poi(p.Id, content.Pois[p.Type], new Vector2(p.X, p.Z), p.HeadingDeg * MathUtil.Deg2Rad, p.Farm, p.Name);
            world.Pois.Add(poi);
            foreach (var collider in poi.Def.Colliders)
            {
                var box = poi.ColliderBox(collider);
                world.Obstacles.Add(collider.Round
                    ? new Obstacle { Shape = ObstacleShape.Circle, Center = box.Center, Radius = collider.W * 0.5f, Kind = poi.Def.Id }
                    : new Obstacle
                    {
                        Shape = ObstacleShape.Box, Center = box.Center, HalfExtents = box.HalfExtents, Heading = box.Heading,
                        Kind = poi.Def.Id,
                    });
            }
        }
    }

    private static void BuildTrees(WorldMap world, MapDef map, Rng rng)
    {
        bool Clear(Vector2 p, float r)
        {
            if (p.X < r || p.Y < r || p.X > world.Size - r || p.Y > world.Size - r) return false;
            foreach (var o in (ReadOnlySpan<Vector2>)[new(0, 0), new(r, 0), new(-r, 0), new(0, r), new(0, -r)])
            {
                var g = world.GroundAt(p + o);
                if (g is not (GroundType.Grass or GroundType.Forest)) return false;
                var (cx, cz) = world.WorldToCell(p + o);
                if (world.InBounds(cx, cz) && world.Layers.FieldId[world.CellIndex(cx, cz)] != 0) return false;
            }
            foreach (var ob in world.Obstacles)
                if (Vector2.Distance(ob.Center, p) < ob.BoundingRadius + r) return false;
            foreach (var poi in world.Pois)
                if (poi.Footprint.Distance(p) < r) return false;
            return true;
        }

        void Add(Vector2 p, bool forest)
        {
            var conifer = forest ? rng.Chance(0.45f) : rng.Chance(0.15f);
            var h = conifer ? rng.Range(9f, 17f) : rng.Range(7f, 13f);
            var tree = new TreeInstance
            {
                Position = p, Height = h, Radius = conifer ? h * 0.2f : h * 0.3f,
                Species = conifer ? 1 : 0, Seed = rng.NextFloat(),
            };
            world.Trees.Add(tree);
            world.Obstacles.Add(new Obstacle { Shape = ObstacleShape.Circle, Center = p, Radius = 0.45f, Kind = "tree" });
        }

        foreach (var f in map.Forests)
        {
            var count = (int)(f.W * f.H / 100f * f.Density);
            for (var k = 0; k < count; k++)
            {
                var p = new Vector2(rng.Range(f.X, f.X + f.W), rng.Range(f.Z, f.Z + f.H));
                if (Clear(p, 2.2f)) Add(p, forest: true);
            }
        }

        var scattered = (int)(map.Size * map.Size / 10000f * map.ScatteredTreesPerHa);
        for (var k = 0; k < scattered; k++)
        {
            var p = new Vector2(rng.Range(0f, map.Size), rng.Range(0f, map.Size));
            if (world.GroundAt(p) == GroundType.Grass && Clear(p, 5f)) Add(p, forest: false);
        }
    }
}
