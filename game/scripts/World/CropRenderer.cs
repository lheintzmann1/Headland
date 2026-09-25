using FarmSim.Core;
using FarmSim.Core.Content;
using FarmSim.Core.World;
using Godot;

namespace FarmSim.Game.World;

/// <summary>
/// Crops as MultiMesh clumps: one instance of three crossed alpha cards per 1 m block (2x2 cells),
/// sized by the growth stage. Chunks rebuild when the simulation flags their crops dirty.
/// </summary>
public partial class CropRenderer : Node3D
{
    /// <summary>Card width in meters; must match tools/gen_crop_cards.py CARD_WIDTH.</summary>
    public const float CardWidth = 1.1f;
    private const int BlocksPerChunk = WorldMap.ChunkCells / 2;
    private const int MaxInstances = BlocksPerChunk * BlocksPerChunk;
    private const int Stride = 12 + 4; // transform + custom data
    private const int RebuildsPerFrame = 16;

    public Simulation Sim { get; init; } = null!;
    private MultiMeshInstance3D[] _chunks = [];
    private readonly float[] _buffer = new float[MaxInstances * Stride];
    private int _cursor;

    public override void _Ready()
    {
        var w = Sim.World;
        _chunks = new MultiMeshInstance3D[w.ChunksX * w.ChunksZ];
        var mesh = BuildCardMesh();
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crop.gdshader") };
        mat.SetShaderParameter("atlas", GD.Load<Texture2D>("res://assets/textures/crops/crop_atlas.png"));

        for (var cz = 0; cz < w.ChunksZ; cz++)
        for (var cx = 0; cx < w.ChunksX; cx++)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = MaxInstances,
                VisibleInstanceCount = 0,
            };
            var inst = new MultiMeshInstance3D
            {
                Name = $"Crops_{cx}_{cz}",
                Multimesh = mm,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                CustomAabb = new Aabb(new Vector3(cx * WorldMap.ChunkSize, -40f, cz * WorldMap.ChunkSize),
                    new Vector3(WorldMap.ChunkSize, 90f, WorldMap.ChunkSize)),
                Visible = false,
            };
            // Instances are placed in world space; keep the node at the origin.
            _chunks[w.ChunkIndex(cx, cz)] = inst;
            AddChild(inst);
        }
    }

    private static ArrayMesh BuildCardMesh()
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        const float hw = CardWidth * 0.5f;
        for (var k = 0; k < 3; k++)
        {
            var a = k * Mathf.Pi / 3f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var n = new Vector3(-dir.Z, 0f, dir.X);
            var b = verts.Count;
            verts.AddRange([-dir * hw, dir * hw, dir * hw + Vector3.Up, -dir * hw + Vector3.Up]);
            normals.AddRange([n, n, n, n]);
            uvs.AddRange([new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0)]);
            indices.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    public override void _Process(double delta)
    {
        var w = Sim.World;
        var rebuilt = 0;
        for (var k = 0; k < _chunks.Length && rebuilt < RebuildsPerFrame; k++)
        {
            var i = (_cursor + k) % _chunks.Length;
            if (!w.CropDirty[i]) continue;
            w.CropDirty[i] = false;
            Rebuild(i);
            rebuilt++;
            _cursor = (i + 1) % _chunks.Length;
        }
    }

    private void Rebuild(int chunk)
    {
        var w = Sim.World;
        var L = w.Layers;
        var crops = Sim.Content.Crops;
        var chx = chunk % w.ChunksX;
        var chz = chunk / w.ChunksX;
        var n = 0;
        for (var bz = 0; bz < BlocksPerChunk; bz++)
        for (var bx = 0; bx < BlocksPerChunk; bx++)
        {
            var cx = chx * WorldMap.ChunkCells + bx * 2;
            var cz = chz * WorldMap.ChunkCells + bz * 2;
            var i = w.CellIndex(cx, cz);
            var cropId = L.Crop[i];
            if (cropId == 0) continue;
            var stage = L.Stage[i];
            var def = crops[cropId - 1];
            float height;
            int col;
            if (stage == CropStage.Dead)
            {
                height = MaxHeight(def) * 0.4f;
                col = 7;
            }
            else
            {
                // Grow smoothly from the previous stage's height through this stage.
                var st = def.Stages[stage];
                var from = stage > 0 ? def.Stages[stage - 1].Height : 0f;
                var frac = st.Gdd > 0f ? Mathf.Clamp(L.Progress[i] / st.Gdd, 0f, 1f) : 1f;
                height = Mathf.Lerp(from, st.Height, frac);
                col = def.CardOf(stage);
            }
            if (height <= 0.01f) continue;

            var r1 = Hash(cx, cz, 1);
            var r2 = Hash(cx, cz, 2);
            var r3 = Hash(cx, cz, 3);
            var px = cx * WorldMap.CellSize + 0.5f + (r1 - 0.5f) * 0.35f;
            var pz = cz * WorldMap.CellSize + 0.5f + (r2 - 0.5f) * 0.35f;
            var y = w.Height.Sample(px, pz) - 0.03f;
            var sx = 0.9f + 0.25f * r3;
            var basis = new Basis(Vector3.Up, r1 * Mathf.Tau) * Basis.FromScale(new Vector3(sx, height * (0.88f + 0.24f * r2), sx));

            var o = n * Stride;
            _buffer[o + 0] = basis.X.X; _buffer[o + 1] = basis.Y.X; _buffer[o + 2] = basis.Z.X; _buffer[o + 3] = px;
            _buffer[o + 4] = basis.X.Y; _buffer[o + 5] = basis.Y.Y; _buffer[o + 6] = basis.Z.Y; _buffer[o + 7] = y;
            _buffer[o + 8] = basis.X.Z; _buffer[o + 9] = basis.Y.Z; _buffer[o + 10] = basis.Z.Z; _buffer[o + 11] = pz;
            _buffer[o + 12] = col;
            _buffer[o + 13] = def.AtlasRow;
            _buffer[o + 14] = L.Health[i] / 255f;
            _buffer[o + 15] = r3;
            n++;
        }

        var inst = _chunks[chunk];
        var mm = inst.Multimesh;
        if (n > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), _buffer);
        mm.VisibleInstanceCount = n;
        inst.Visible = n > 0;
    }

    private static float MaxHeight(CropDef def) => def.Stages.Max(s => s.Height);

    private static float Hash(int x, int z, int seed) => FarmSim.Core.Rng.Hash01(x, z, seed);
}
