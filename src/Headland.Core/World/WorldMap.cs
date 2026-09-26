using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Ownership;
using Headland.Core.Pois;

namespace Headland.Core.World;

public enum GroundType : byte
{
    Grass = 0,
    Cultivated = 1,
    Seeded = 2,
    Stubble = 3,
    Plowed = 4,
    Road = 5,
    Yard = 6,
    Dirt = 7,
    Forest = 8,
    Water = 9,
}

/// <summary>Crop stage value marking a dead (withered/frozen) crop.</summary>
public static class CropStage
{
    public const byte Dead = 255;
}

/// <summary>Terrain heights on a 1 m grid ((size+1)² samples).</summary>
public sealed class HeightMap
{
    public HeightMap(int size)
    {
        Size = size;
        Stride = size + 1;
        Heights = new float[Stride * Stride];
    }

    public int Size { get; }
    public int Stride { get; }
    public float[] Heights { get; }

    public float At(int x, int z) => Heights[Math.Clamp(z, 0, Size) * Stride + Math.Clamp(x, 0, Size)];

    public float Sample(float x, float z)
    {
        x = Math.Clamp(x, 0f, Size - 0.0001f);
        z = Math.Clamp(z, 0f, Size - 0.0001f);
        var x0 = (int)x;
        var z0 = (int)z;
        var tx = x - x0;
        var tz = z - z0;
        var a = Heights[z0 * Stride + x0];
        var b = Heights[z0 * Stride + x0 + 1];
        var c = Heights[(z0 + 1) * Stride + x0];
        var d = Heights[(z0 + 1) * Stride + x0 + 1];
        return MathUtil.Lerp(MathUtil.Lerp(a, b, tx), MathUtil.Lerp(c, d, tx), tz);
    }

    public Vector3 Normal(float x, float z)
    {
        const float e = 0.5f;
        var dx = Sample(x + e, z) - Sample(x - e, z);
        var dz = Sample(x, z + e) - Sample(x, z - e);
        return Vector3.Normalize(new Vector3(-dx, 2f * e, -dz));
    }
}

/// <summary>Struct-of-arrays per-cell state (0.5 m cells).</summary>
public sealed class FieldLayers
{
    public FieldLayers(int count)
    {
        Ground = new byte[count];
        Soil = new byte[count];
        FarmlandId = new ushort[count];
        FieldId = new ushort[count];
        Crop = new byte[count];
        Stage = new byte[count];
        Progress = new float[count];
        Moisture = new byte[count];
        Nitrogen = new byte[count];
        Health = new byte[count];
        WorkAngle = new byte[count];
        Chill = new byte[count];
    }

    public byte[] Ground { get; }
    public byte[] Soil { get; }
    /// <summary>0 = outside every farmland (roads, towns: land that can't be bought).</summary>
    public ushort[] FarmlandId { get; }
    /// <summary>0 = not part of a field.</summary>
    public ushort[] FieldId { get; }
    /// <summary>Crop index + 1 (0 = none).</summary>
    public byte[] Crop { get; }
    public byte[] Stage { get; }
    /// <summary>Degree-days accumulated inside the current stage.</summary>
    public float[] Progress { get; }
    /// <summary>Relative soil moisture 0..255 (0..1 of saturation).</summary>
    public byte[] Moisture { get; }
    /// <summary>Plant-available nitrogen in kg/ha (0..255).</summary>
    public byte[] Nitrogen { get; }
    /// <summary>Crop health 0..255.</summary>
    public byte[] Health { get; }
    /// <summary>Last work direction, 0..255 over [0, π).</summary>
    public byte[] WorkAngle { get; }
    /// <summary>Vernalization chill accumulated by the crop, in real days.</summary>
    public byte[] Chill { get; }
}

public enum ObstacleShape { Circle, Box }

public sealed class Obstacle
{
    public ObstacleShape Shape { get; init; }
    public Vector2 Center { get; init; }
    public float Radius { get; init; }
    public Vector2 HalfExtents { get; init; }
    public float Heading { get; init; }
    public string Kind { get; init; } = "";

    public float BoundingRadius => Shape == ObstacleShape.Circle ? Radius : HalfExtents.Length();
}

public sealed class TreeInstance
{
    public Vector2 Position { get; init; }
    public float Height { get; init; }
    public float Radius { get; init; }
    /// <summary>0 = broadleaf, 1 = conifer.</summary>
    public int Species { get; init; }
    public float Seed { get; init; }
}

public sealed class NetworkTile
{
    public int X { get; init; }
    public int Z { get; init; }
    /// <summary>Connection bits: 1 = north(-z), 2 = east(+x), 4 = south(+z), 8 = west(-x).</summary>
    public int Mask { get; set; }
    /// <summary>For dead ends and lone tiles: true if the run is along x (east-west).</summary>
    public bool AlongX { get; set; }
}

/// <summary>Roads, farm tracks, paths or streams: tiles on the shared grid drawn with one atlas style.</summary>
public sealed class TileNetwork
{
    public string Id { get; init; } = "";
    public string Style { get; init; } = "";
    public GroundType Ground { get; init; }
    public float Width { get; init; }
    public List<NetworkTile> Tiles { get; } = [];
}

/// <summary>A parcel of land bought and sold as a whole; the fields inside it are its crop areas.</summary>
public sealed class Farmland : IOwnable
{
    /// <summary>Parcel number (1..65535, stored in <see cref="FieldLayers.FarmlandId"/>).</summary>
    public int Id { get; init; }
    public required Polygon Shape { get; init; }
    /// <summary>Owns the parcel whenever no farm does.</summary>
    public required NpcDef Npc { get; init; }
    /// <summary>Owning farm (<see cref="Farm.None"/> = its NPC). Changed through <see cref="Farms.SetOwner"/>.</summary>
    public int FarmId { get; internal set; }
    /// <summary>What the parcel costs, and sells back for: its area at the map's price per hectare.</summary>
    public float Price { get; init; }
    public float AreaHa => Shape.Area / 10000f;
    public List<FieldInfo> Fields { get; } = [];
    public string Label => $"Farmland {Id}";
}

public sealed class FieldInfo
{
    /// <summary>Field number shown to the player (1..65535, stored in <see cref="FieldLayers.FieldId"/>).</summary>
    public int Id { get; init; }
    public required Polygon Shape { get; init; }
    /// <summary>The farmland holding the field (0 = none).</summary>
    public int FarmlandId { get; init; }
    public float AreaHa => Shape.Area / 10000f;
    public Vector2 Center => Shape.Centroid;
    public string Label => $"Field {Id}";

    public bool Contains(Vector2 p) => Shape.Contains(p);

    public static FieldInfo Rect(int id, float x, float z, float w, float h) => new() { Id = id, Shape = Polygon.Rect(x, z, w, h) };
}

public sealed class WorldMap
{
    public const float CellSize = 0.5f;
    public const float CellArea = CellSize * CellSize;
    public const int ChunkCells = 64;
    public const float ChunkSize = ChunkCells * CellSize;

    public WorldMap(int sizeMeters)
    {
        Size = sizeMeters;
        CellsX = (int)(sizeMeters / CellSize);
        CellsZ = CellsX;
        ChunksX = CellsX / ChunkCells;
        ChunksZ = CellsZ / ChunkCells;
        Height = new HeightMap(sizeMeters);
        Layers = new FieldLayers(CellsX * CellsZ);
        GroundDirty = new bool[ChunksX * ChunksZ];
        CropDirty = new bool[ChunksX * ChunksZ];
        Array.Fill(GroundDirty, true);
        Array.Fill(CropDirty, true);
    }

    public int Size { get; }
    public int CellsX { get; }
    public int CellsZ { get; }
    public int ChunksX { get; }
    public int ChunksZ { get; }
    public HeightMap Height { get; }
    public FieldLayers Layers { get; }

    public List<Obstacle> Obstacles { get; } = [];
    public List<TreeInstance> Trees { get; } = [];
    public List<TileNetwork> Networks { get; } = [];
    /// <summary>Edge of a network tile in meters (shared by every network).</summary>
    public float TileSize { get; set; } = 16f;
    public List<Farmland> Farmlands { get; } = [];
    public List<FieldInfo> Fields { get; } = [];
    public List<Poi> Pois { get; } = [];

    /// <summary>Chunk needs its ground data texture re-uploaded.</summary>
    public bool[] GroundDirty { get; }
    /// <summary>Chunk needs its crop instances rebuilt.</summary>
    public bool[] CropDirty { get; }

    public int CellIndex(int cx, int cz) => cz * CellsX + cx;

    public bool InBounds(int cx, int cz) => (uint)cx < (uint)CellsX && (uint)cz < (uint)CellsZ;

    public (int cx, int cz) WorldToCell(Vector2 p) => ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize));

    public Vector2 CellCenter(int cx, int cz) => new((cx + 0.5f) * CellSize, (cz + 0.5f) * CellSize);

    public int ChunkIndex(int chunkX, int chunkZ) => chunkZ * ChunksX + chunkX;

    /// <summary>Marks the cell's chunk dirty, plus neighbors when the cell lies on a chunk border (textures are padded).</summary>
    public void MarkCellDirty(int cx, int cz, bool crop)
    {
        var chx = cx / ChunkCells;
        var chz = cz / ChunkCells;
        var lx = cx - chx * ChunkCells;
        var lz = cz - chz * ChunkCells;
        MarkChunk(chx, chz, crop);
        if (lx == 0) MarkChunk(chx - 1, chz, crop);
        else if (lx == ChunkCells - 1) MarkChunk(chx + 1, chz, crop);
        if (lz == 0) MarkChunk(chx, chz - 1, crop);
        else if (lz == ChunkCells - 1) MarkChunk(chx, chz + 1, crop);
    }

    private void MarkChunk(int chx, int chz, bool crop)
    {
        if ((uint)chx >= (uint)ChunksX || (uint)chz >= (uint)ChunksZ) return;
        var i = ChunkIndex(chx, chz);
        GroundDirty[i] = true;
        if (crop) CropDirty[i] = true;
    }

    public void MarkAllDirty(bool crop)
    {
        Array.Fill(GroundDirty, true);
        if (crop) Array.Fill(CropDirty, true);
    }

    public float HeightAt(Vector2 p) => Height.Sample(p.X, p.Y);

    public GroundType GroundAt(Vector2 p)
    {
        var (cx, cz) = WorldToCell(p);
        return InBounds(cx, cz) ? (GroundType)Layers.Ground[CellIndex(cx, cz)] : GroundType.Grass;
    }

    public bool IsWorkable(GroundType g) => g is GroundType.Grass or GroundType.Cultivated or GroundType.Seeded
        or GroundType.Stubble or GroundType.Plowed;

    /// <summary>Ground whose moisture never changes (paved, gravel, open water).</summary>
    public static bool IsSealed(GroundType g) => g is GroundType.Road or GroundType.Yard or GroundType.Water;

    public FieldInfo? FieldById(int id) => Fields.Find(f => f.Id == id);
    public Poi? PoiById(string id) => Pois.Find(p => p.Id == id);
    public Farmland? FarmlandById(int id) => Farmlands.Find(f => f.Id == id);

    public Farmland? FarmlandAt(Vector2 p)
    {
        var (cx, cz) = WorldToCell(p);
        return InBounds(cx, cz) && Layers.FarmlandId[CellIndex(cx, cz)] is var id and > 0 ? FarmlandById(id) : null;
    }

    /// <summary>
    /// Fills a (64+2)² RGBA8 data texture for a chunk, including a 1-cell border from neighbors.
    /// R = ground | soil &lt;&lt; 4, G = moisture, B = work angle, A = crop cover (0..255).
    /// <paramref name="coverLut"/> is indexed by (crop &lt;&lt; 8) | stage.
    /// </summary>
    public void FillChunkTexture(int chunkX, int chunkZ, Span<byte> rgba, ReadOnlySpan<byte> coverLut)
    {
        const int n = ChunkCells + 2;
        var baseX = chunkX * ChunkCells - 1;
        var baseZ = chunkZ * ChunkCells - 1;
        for (var z = 0; z < n; z++)
        {
            var cz = Math.Clamp(baseZ + z, 0, CellsZ - 1);
            for (var x = 0; x < n; x++)
            {
                var cx = Math.Clamp(baseX + x, 0, CellsX - 1);
                var i = CellIndex(cx, cz);
                var o = (z * n + x) * 4;
                rgba[o] = (byte)(Layers.Ground[i] | (Layers.Soil[i] << 4));
                rgba[o + 1] = Layers.Moisture[i];
                rgba[o + 2] = Layers.WorkAngle[i];
                rgba[o + 3] = coverLut[(Layers.Crop[i] << 8) | Layers.Stage[i]];
            }
        }
    }

    /// <summary>Ray vs heightmap. Returns false if the ray misses the map.</summary>
    public bool Raycast(Vector3 origin, Vector3 dir, out Vector3 hit, float maxDist = 2000f)
    {
        hit = default;
        dir = Vector3.Normalize(dir);
        if (MathF.Abs(dir.Y) < 1e-4f) return false;
        // Start where the ray enters the plausible height band to keep the march short.
        const float top = 60f, bottom = -30f;
        var tStart = dir.Y < 0 ? MathF.Max(0f, (top - origin.Y) / dir.Y) : 0f;
        var tEnd = dir.Y < 0 ? MathF.Min(maxDist, (bottom - origin.Y) / dir.Y) : maxDist;
        const float step = 0.25f;
        var prevT = tStart;
        for (var t = tStart; t <= tEnd; t += step)
        {
            var p = origin + dir * t;
            var diff = p.Y - Height.Sample(p.X, p.Z);
            if (diff <= 0f)
            {
                // Refine between prev and t.
                var a = prevT;
                var b = t;
                for (var k = 0; k < 8; k++)
                {
                    var m = (a + b) * 0.5f;
                    var pm = origin + dir * m;
                    if (pm.Y - Height.Sample(pm.X, pm.Z) > 0) a = m; else b = m;
                }
                hit = origin + dir * b;
                return hit.X >= 0 && hit.Z >= 0 && hit.X <= Size && hit.Z <= Size;
            }
            prevT = t;
        }
        return false;
    }
}
