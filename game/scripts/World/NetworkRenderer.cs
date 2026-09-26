using System.Text.Json;
using Headland.Core;
using Headland.Core.World;
using Godot;

namespace Headland.Game.World;

/// <summary>
/// Draws every tile network (roads, tracks, streams) as terrain-hugging decals using the converted atlases.
/// The atlas cell for a tile is picked from its connection mask (assets/textures/tiles/manifest.json).
/// </summary>
public partial class NetworkRenderer : Node3D
{
    private const string TileDir = "res://assets/textures/tiles";
    public Simulation Sim { get; init; } = null!;

    public override void _Ready()
    {
        var manifest = LoadManifest();
        var shader = GD.Load<Shader>("res://shaders/tile_decal.gdshader");
        foreach (var net in Sim.World.Networks)
        {
            if (!manifest.TryGetValue(net.Style, out var masks))
            {
                GD.PushWarning($"Tile style '{net.Style}' missing from {TileDir}/manifest.json; network '{net.Id}' not drawn");
                continue;
            }
            var mat = new ShaderMaterial { Shader = shader, RenderPriority = net.Ground == GroundType.Water ? -1 : 0 };
            mat.SetShaderParameter("atlas", GD.Load<Texture2D>($"{TileDir}/{net.Style}_atlas.png"));
            mat.SetShaderParameter("kind", net.Ground switch { GroundType.Road => 0, GroundType.Water => 2, _ => 1 });
            var lift = net.Ground == GroundType.Road ? 0.05f : 0.035f;
            AddChild(new MeshInstance3D
            {
                Name = net.Id,
                Mesh = BuildMesh(net, masks, lift),
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private static Dictionary<string, Dictionary<int, int>> LoadManifest()
    {
        using var f = FileAccess.Open($"{TileDir}/manifest.json", FileAccess.ModeFlags.Read);
        using var doc = JsonDocument.Parse(f.GetAsText());
        var result = new Dictionary<string, Dictionary<int, int>>();
        foreach (var set in doc.RootElement.GetProperty("sets").EnumerateObject())
            result[set.Name] = set.Value.GetProperty("masks").EnumerateObject()
                .ToDictionary(m => int.Parse(m.Name), m => m.Value.GetInt32());
        return result;
    }

    private ArrayMesh BuildMesh(TileNetwork net, Dictionary<int, int> masks, float lift)
    {
        var world = Sim.World;
        var ts = world.TileSize;
        var sub = (int)ts; // 1 m subdivisions follow the 1 m terrain grid
        const float cell = 0.25f;
        const float inset = 1f / 1024f;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        foreach (var t in net.Tiles)
        {
            var mask = WorldGen.EffectiveMask(t);
            if (!masks.TryGetValue(mask, out var idx)) idx = masks.GetValueOrDefault(5);
            var uv0 = new Vector2(idx % 4, idx / 4) * cell;
            var baseIndex = verts.Count;
            for (var z = 0; z <= sub; z++)
            for (var x = 0; x <= sub; x++)
            {
                var wx = t.X * ts + x * ts / sub;
                var wz = t.Z * ts + z * ts / sub;
                verts.Add(new Vector3(wx, world.Height.Sample(wx, wz) + lift, wz));
                var n = world.Height.Normal(wx, wz);
                normals.Add(new Vector3(n.X, n.Y, n.Z));
                var u = Mathf.Lerp(inset, cell - inset, x / (float)sub);
                var v = Mathf.Lerp(inset, cell - inset, z / (float)sub);
                uvs.Add(uv0 + new Vector2(u, v));
            }
            for (var z = 0; z < sub; z++)
            for (var x = 0; x < sub; x++)
            {
                var a = baseIndex + z * (sub + 1) + x;
                var b = a + 1;
                var c = a + sub + 1;
                var d = c + 1;
                indices.AddRange([a, b, c, b, d, c]);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var st = new SurfaceTool();
        st.CreateFromArrays(arrays);
        st.GenerateTangents();
        return st.Commit();
    }
}
