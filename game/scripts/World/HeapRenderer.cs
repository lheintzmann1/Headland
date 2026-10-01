using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.World;

/// <summary>
/// Heaps on the ground (<see cref="Heaps"/>) as one surface per 32 m chunk: a vertex at each corner of the 0.5 m cells,
/// as high as the heaps of the four cells around it, in their fill type's color, and sunk just under the ground at its
/// edges. Chunks rebuild when the simulation flags their heaps dirty.
/// </summary>
public partial class HeapRenderer : Node3D
{
    private const int Corners = WorldMap.ChunkCells + 1;
    private const int RebuildsPerFrame = 8;
    /// <summary>How far a corner with no heap around it sinks under the ground, out of sight.</summary>
    private const float Sink = 0.03f;

    public Simulation Sim { get; init; } = null!;
    private MeshInstance3D[] _chunks = [];
    private Color[] _colors = [];
    private ShaderMaterial _material = null!;
    private readonly Vector3[] _vertices = new Vector3[Corners * Corners];
    private readonly Vector3[] _normals = new Vector3[Corners * Corners];
    private readonly Color[] _vertexColors = new Color[Corners * Corners];
    private readonly float[] _lift = new float[Corners * Corners];
    private readonly List<int> _indices = [];
    private int _cursor;

    public override void _Ready()
    {
        var w = Sim.World;
        _colors = Sim.Content.FillTypeList.Select(f => Color.FromHtml(f.Color)).ToArray();
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/heap.gdshader") };
        _chunks = new MeshInstance3D[w.ChunksX * w.ChunksZ];
        for (var cz = 0; cz < w.ChunksZ; cz++)
        for (var cx = 0; cx < w.ChunksX; cx++)
        {
            var inst = new MeshInstance3D { Name = $"Heaps_{cx}_{cz}", MaterialOverride = _material, Visible = false };
            _chunks[w.ChunkIndex(cx, cz)] = inst;
            AddChild(inst);
        }
    }

    public override void _Process(double delta)
    {
        var w = Sim.World;
        var rebuilt = 0;
        for (var k = 0; k < _chunks.Length && rebuilt < RebuildsPerFrame; k++)
        {
            var i = (_cursor + k) % _chunks.Length;
            if (!w.HeapDirty[i]) continue;
            w.HeapDirty[i] = false;
            Rebuild(i);
            rebuilt++;
            _cursor = (i + 1) % _chunks.Length;
        }
    }

    private void Rebuild(int chunk)
    {
        var w = Sim.World;
        var L = w.Layers;
        var (chx, chz) = (chunk % w.ChunksX, chunk / w.ChunksX);
        var (x0, z0) = (chx * WorldMap.ChunkCells, chz * WorldMap.ChunkCells);

        // Each corner: the mean of the heaps on the four cells around it, colored by the highest of them.
        var any = false;
        for (var kz = 0; kz < Corners; kz++)
        for (var kx = 0; kx < Corners; kx++)
        {
            var (sum, top, fill) = (0f, 0f, 0);
            for (var dz = -1; dz <= 0; dz++)
            for (var dx = -1; dx <= 0; dx++)
            {
                var (cx, cz) = (x0 + kx + dx, z0 + kz + dz);
                if (!w.InBounds(cx, cz)) continue;
                var i = w.CellIndex(cx, cz);
                if (!Sim.Heaps.Has(i)) continue;
                sum += L.Heap[i];
                if (L.Heap[i] <= top) continue;
                (top, fill) = (L.Heap[i], L.HeapFill[i]);
            }
            var k = kz * Corners + kx;
            _lift[k] = sum * 0.25f;
            any |= sum > 0f;
            var (px, pz) = ((x0 + kx) * WorldMap.CellSize, (z0 + kz) * WorldMap.CellSize);
            _vertices[k] = new Vector3(px, w.Height.Sample(px, pz) + (sum > 0f ? _lift[k] : -Sink), pz);
            _vertexColors[k] = fill > 0 && fill <= _colors.Length ? _colors[fill - 1] : Colors.White;
        }
        var inst = _chunks[chunk];
        if (!any)
        {
            inst.Visible = false;
            inst.Mesh = null;
            return;
        }

        // A cell is drawn where a heap reaches any of its corners: the heap's cells and the ring sloping down around them.
        _indices.Clear();
        for (var lz = 0; lz < WorldMap.ChunkCells; lz++)
        for (var lx = 0; lx < WorldMap.ChunkCells; lx++)
        {
            var (a, b, c, d) = (lz * Corners + lx, lz * Corners + lx + 1, (lz + 1) * Corners + lx, (lz + 1) * Corners + lx + 1);
            if (_lift[a] + _lift[b] + _lift[c] + _lift[d] <= 0f) continue;
            _indices.AddRange([a, c, b, b, c, d]);
        }
        for (var kz = 0; kz < Corners; kz++)
        for (var kx = 0; kx < Corners; kx++)
        {
            Vector3 At(int x, int z) => _vertices[Math.Clamp(z, 0, Corners - 1) * Corners + Math.Clamp(x, 0, Corners - 1)];
            var dx = At(kx + 1, kz).Y - At(kx - 1, kz).Y;
            var dz = At(kx, kz + 1).Y - At(kx, kz - 1).Y;
            _normals[kz * Corners + kx] = new Vector3(-dx, 2f * WorldMap.CellSize, -dz).Normalized();
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _vertices;
        arrays[(int)Mesh.ArrayType.Normal] = _normals;
        arrays[(int)Mesh.ArrayType.Color] = _vertexColors;
        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        inst.Mesh = mesh;
        inst.Visible = true;
    }
}
