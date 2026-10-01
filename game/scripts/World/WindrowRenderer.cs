using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.World;

/// <summary>
/// What lies cut on the fields (<see cref="Windrows"/>) as MultiMesh ridges: one low mound per 0.5 m cell holding
/// something, along the way it was worked, as high as what lies there and in its fill type's color. Chunks rebuild when
/// the simulation flags their windrows dirty.
/// </summary>
public partial class WindrowRenderer : Node3D
{
    private const int MaxInstances = WorldMap.ChunkCells * WorldMap.ChunkCells;
    private const int Stride = 12 + 4; // transform + custom data
    private const int RebuildsPerFrame = 16;
    /// <summary>Units on a square meter that make a ridge <see cref="RidgeHeight"/> high: a mower's windrow of grass.</summary>
    private const float FullPerSqm = 5f;
    private const float RidgeHeight = 0.3f;

    public Simulation Sim { get; init; } = null!;
    private MultiMeshInstance3D[] _chunks = [];
    private Color[] _colors = [];
    private readonly float[] _buffer = new float[MaxInstances * Stride];
    private int _cursor;

    public override void _Ready()
    {
        var w = Sim.World;
        _colors = Sim.Content.FillTypeList.Select(f => Color.FromHtml(f.Color)).ToArray();
        _chunks = new MultiMeshInstance3D[w.ChunksX * w.ChunksZ];
        var mesh = BuildRidgeMesh();
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/windrow.gdshader") };
        for (var cz = 0; cz < w.ChunksZ; cz++)
        for (var cx = 0; cx < w.ChunksX; cx++)
        {
            var inst = new MultiMeshInstance3D
            {
                Name = $"Windrows_{cx}_{cz}",
                Multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseCustomData = true,
                    Mesh = mesh,
                    InstanceCount = MaxInstances,
                    VisibleInstanceCount = 0,
                },
                MaterialOverride = mat,
                CustomAabb = new Aabb(new Vector3(cx * WorldMap.ChunkSize, -40f, cz * WorldMap.ChunkSize),
                    new Vector3(WorldMap.ChunkSize, 90f, WorldMap.ChunkSize)),
                Visible = false,
            };
            _chunks[w.ChunkIndex(cx, cz)] = inst;
            AddChild(inst);
        }
    }

    /// <summary>
    /// A ridge 1 m wide (x), 1 m high and 0.7 m long (z), rounded across and tapering to its ends, so the cells of a
    /// windrow overlap into one.
    /// </summary>
    private static ArrayMesh BuildRidgeMesh()
    {
        const int across = 8;
        float[] along = [-0.35f, -0.22f, 0.22f, 0.35f];
        float[] scale = [0.35f, 1f, 1f, 0.35f];
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(int k, int j)
        {
            var a = Mathf.Pi * k / across;
            return new Vector3(Mathf.Cos(a) * 0.5f * scale[j], Mathf.Sin(a) * scale[j], along[j]);
        }
        for (var j = 0; j < along.Length - 1; j++)
        for (var k = 0; k < across; k++)
        {
            Vector3 a = At(k, j), b = At(k + 1, j), c = At(k, j + 1), d = At(k + 1, j + 1);
            st.SetUV(new Vector2((float)k / across, j));
            st.AddVertex(a);
            st.AddVertex(c);
            st.AddVertex(b);
            st.AddVertex(b);
            st.AddVertex(c);
            st.AddVertex(d);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    public override void _Process(double delta)
    {
        var w = Sim.World;
        var rebuilt = 0;
        for (var k = 0; k < _chunks.Length && rebuilt < RebuildsPerFrame; k++)
        {
            var i = (_cursor + k) % _chunks.Length;
            if (!w.WindrowDirty[i]) continue;
            w.WindrowDirty[i] = false;
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
        var n = 0;
        for (var lz = 0; lz < WorldMap.ChunkCells; lz++)
        for (var lx = 0; lx < WorldMap.ChunkCells; lx++)
        {
            var (cx, cz) = (chx * WorldMap.ChunkCells + lx, chz * WorldMap.ChunkCells + lz);
            var i = w.CellIndex(cx, cz);
            if (!Windrows.Has(L, i) || L.WindrowFill[i] > _colors.Length) continue;
            var perSqm = L.Windrow[i] / WorldMap.CellArea;
            var height = Mathf.Clamp(RidgeHeight * Mathf.Sqrt(perSqm / FullPerSqm), 0.015f, 0.5f);
            var r = Headland.Core.Rng.Hash01(cx, cz, 7);
            var p = w.CellCenter(cx, cz);
            var px = p.X + (r - 0.5f) * 0.1f;
            var pz = p.Y + (Headland.Core.Rng.Hash01(cx, cz, 8) - 0.5f) * 0.1f;
            // Along the way it was cut or raked, as wide as the cell and a little more.
            var heading = L.WorkAngle[i] / 255f * Mathf.Pi + (r - 0.5f) * 0.3f;
            var basis = new Basis(Vector3.Up, heading) * Basis.FromScale(new Vector3(0.62f + 0.12f * r, height, 1f));
            var y = w.Height.Sample(px, pz) - 0.02f;
            var color = _colors[L.WindrowFill[i] - 1];

            var o = n * Stride;
            _buffer[o + 0] = basis.X.X; _buffer[o + 1] = basis.Y.X; _buffer[o + 2] = basis.Z.X; _buffer[o + 3] = px;
            _buffer[o + 4] = basis.X.Y; _buffer[o + 5] = basis.Y.Y; _buffer[o + 6] = basis.Z.Y; _buffer[o + 7] = y;
            _buffer[o + 8] = basis.X.Z; _buffer[o + 9] = basis.Y.Z; _buffer[o + 10] = basis.Z.Z; _buffer[o + 11] = pz;
            _buffer[o + 12] = color.R;
            _buffer[o + 13] = color.G;
            _buffer[o + 14] = color.B;
            _buffer[o + 15] = r;
            n++;
        }
        var inst = _chunks[chunk];
        if (n > 0) RenderingServer.MultimeshSetBuffer(inst.Multimesh.GetRid(), _buffer);
        inst.Multimesh.VisibleInstanceCount = n;
        inst.Visible = n > 0;
    }
}
