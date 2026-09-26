using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;
using NVec3 = System.Numerics.Vector3;

namespace Headland.Game.Common;

/// <summary>Core (System.Numerics, ground plane X/Y) ⇄ Godot (X/Z with Y up).</summary>
public static class Conv
{
    public static Vector3 ToGodot(this NVec2 p, float height) => new(p.X, height, p.Y);
    public static Vector3 ToGodot(this NVec3 p) => new(p.X, p.Y, p.Z);
    public static NVec3 ToCore(this Vector3 p) => new(p.X, p.Y, p.Z);
    public static NVec2 ToGround(this Vector3 p) => new(p.X, p.Z);

    /// <summary>World position on the terrain surface.</summary>
    public static Vector3 OnGround(this WorldMap world, NVec2 p, float lift = 0f) => new(p.X, world.HeightAt(p) + lift, p.Y);

    public static Color Hex(string hex) => Color.FromHtml(hex);
}
