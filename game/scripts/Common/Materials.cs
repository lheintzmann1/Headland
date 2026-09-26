using Godot;

namespace Headland.Game.Common;

/// <summary>Shared StandardMaterial3D cache for placeholder meshes, keyed by look.</summary>
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

    public static StandardMaterial3D Glass { get; } = new()
    {
        AlbedoColor = new Color(0.16f, 0.2f, 0.22f, 0.55f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.1f,
        Metallic = 0.2f,
    };

    public static readonly Color Tire = new(0.09f, 0.09f, 0.09f);
    public static readonly Color Steel = new(0.33f, 0.34f, 0.35f);
    public static readonly Color DarkSteel = new(0.18f, 0.19f, 0.2f);
    public static readonly Color Rim = new(0.62f, 0.6f, 0.55f);
}
