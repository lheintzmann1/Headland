using FarmSim.Core;
using FarmSim.Core.World;
using Godot;

namespace FarmSim.Game.World;

/// <summary>
/// One mesh per 32 m chunk (1 m vertex grid) with a padded 66x66 data texture describing its 0.5 m cells.
/// Dirty chunk textures are re-uploaded a few per frame.
/// </summary>
public partial class TerrainRenderer : Node3D
{
    private const int UploadsPerFrame = 24;
    private const int TexSize = WorldMap.ChunkCells + 2;

    public Simulation Sim { get; init; } = null!;
    private Image[] _images = [];
    private ImageTexture[] _textures = [];
    private readonly byte[] _buffer = new byte[TexSize * TexSize * 4];
    private int _cursor;

    public override void _Ready()
    {
        var w = Sim.World;
        _images = new Image[w.ChunksX * w.ChunksZ];
        _textures = new ImageTexture[_images.Length];
        var shader = GD.Load<Shader>("res://shaders/terrain.gdshader");
        var albedo = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_albedo.jpg");
        var normal = GD.Load<TextureLayered>("res://assets/textures/terrain/terrain_normal.jpg");

        for (var cz = 0; cz < w.ChunksZ; cz++)
        for (var cx = 0; cx < w.ChunksX; cx++)
        {
            var i = w.ChunkIndex(cx, cz);
            w.FillChunkTexture(cx, cz, _buffer, Sim.Crops.CoverLut);
            _images[i] = Image.CreateFromData(TexSize, TexSize, false, Image.Format.Rgba8, _buffer);
            _textures[i] = ImageTexture.CreateFromImage(_images[i]);
            w.GroundDirty[i] = false;

            var mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter("data_tex", _textures[i]);
            mat.SetShaderParameter("chunk_origin", new Vector2(cx * WorldMap.ChunkSize, cz * WorldMap.ChunkSize));
            mat.SetShaderParameter("cell_size", WorldMap.CellSize);
            mat.SetShaderParameter("albedo_tex", albedo);
            mat.SetShaderParameter("normal_tex", normal);

            AddChild(new MeshInstance3D
            {
                Name = $"Chunk_{cx}_{cz}",
                Mesh = BuildChunkMesh(w, cx, cz),
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }

        AddChild(BuildSkirt(w));
    }

    private static ArrayMesh BuildChunkMesh(WorldMap w, int chunkX, int chunkZ)
    {
        const int n = (int)WorldMap.ChunkSize; // quads per side (1 m)
        var x0 = chunkX * n;
        var z0 = chunkZ * n;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var normals = new Vector3[verts.Length];
        var uvs = new Vector2[verts.Length];
        for (var z = 0; z <= n; z++)
        for (var x = 0; x <= n; x++)
        {
            var k = z * (n + 1) + x;
            var wx = x0 + x;
            var wz = z0 + z;
            verts[k] = new Vector3(wx, w.Height.At(wx, wz), wz);
            var nn = w.Height.Normal(wx, wz);
            normals[k] = new Vector3(nn.X, nn.Y, nn.Z);
            uvs[k] = new Vector2(wx, wz);
        }
        var indices = new int[n * n * 6];
        var t = 0;
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            var a = z * (n + 1) + x;
            var b = a + 1;
            var c = a + n + 1;
            var d = c + 1;
            // Clockwise seen from above (+Y, with +Z down-screen): Godot's front-face winding.
            indices[t++] = a; indices[t++] = b; indices[t++] = c;
            indices[t++] = b; indices[t++] = d; indices[t++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var st = new SurfaceTool();
        st.CreateFromArrays(arrays);
        st.GenerateTangents();
        return st.Commit();
    }

    /// <summary>A wide low ground plane around the map so the edges don't float in the void.</summary>
    private static MeshInstance3D BuildSkirt(WorldMap w)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.27f, 0.31f, 0.2f),
            Roughness = 1f,
        };
        return new MeshInstance3D
        {
            Name = "Skirt",
            Mesh = new PlaneMesh { Size = new Vector2(w.Size * 5, w.Size * 5) },
            Position = new Vector3(w.Size * 0.5f, w.Height.Heights.Min() - 1.5f, w.Size * 0.5f),
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        var w = Sim.World;
        var uploads = 0;
        var total = _images.Length;
        for (var k = 0; k < total && uploads < UploadsPerFrame; k++)
        {
            var i = (_cursor + k) % total;
            if (!w.GroundDirty[i]) continue;
            w.GroundDirty[i] = false;
            var cx = i % w.ChunksX;
            var cz = i / w.ChunksX;
            w.FillChunkTexture(cx, cz, _buffer, Sim.Crops.CoverLut);
            _images[i].SetData(TexSize, TexSize, false, Image.Format.Rgba8, _buffer);
            _textures[i].Update(_images[i]);
            uploads++;
            _cursor = (i + 1) % total;
        }
    }
}
