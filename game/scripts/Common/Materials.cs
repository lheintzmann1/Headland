using Godot;

namespace Headland.Game.Common;

/// <summary>Shared StandardMaterial3D cache for the meshes the game builds (trees, a rope, falling grain), keyed by look.</summary>
public static class Materials
{
    private static readonly Dictionary<(Color, float, float, bool), StandardMaterial3D> Cache = new();

    public static StandardMaterial3D Get(Color color, float roughness = 0.8f, float metallic = 0f, bool vertexColor = false)
    {
        var key = (color, roughness, metallic, vertexColor);
        if (Cache.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
            VertexColorUseAsAlbedo = vertexColor,
        };
        Cache[key] = m;
        return m;
    }

    public static readonly Color DarkSteel = new(0.18f, 0.19f, 0.2f);
}
