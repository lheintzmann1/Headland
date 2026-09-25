using System.Numerics;

namespace FarmSim.Core;

/// <summary>
/// Ground-plane math. Core works in 2D (X, Y) where Y maps to Godot's Z axis.
/// Local machine space follows glTF/Godot model space: +Z (here +Y) forward, +X left.
/// Heading θ: forward = (sin θ, cos θ), which equals Godot's rotation.y = θ.
/// </summary>
public static class MathUtil
{
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
    public const float KmhToMs = 1f / 3.6f;

    public static Vector2 Forward(float heading) => new(MathF.Sin(heading), MathF.Cos(heading));

    /// <summary>Local +X (the machine's left side when looking forward).</summary>
    public static Vector2 Left(float heading) => new(MathF.Cos(heading), -MathF.Sin(heading));

    public static Vector2 LocalToWorld(Vector2 origin, float heading, Vector2 local) =>
        origin + Left(heading) * local.X + Forward(heading) * local.Y;

    public static Vector2 WorldToLocal(Vector2 origin, float heading, Vector2 world)
    {
        var d = world - origin;
        return new Vector2(Vector2.Dot(d, Left(heading)), Vector2.Dot(d, Forward(heading)));
    }

    public static float HeadingOf(Vector2 dir) => MathF.Atan2(dir.X, dir.Y);

    /// <summary>Wraps an angle to (-π, π].</summary>
    public static float WrapAngle(float a)
    {
        a %= MathF.Tau;
        if (a <= -MathF.PI) a += MathF.Tau;
        else if (a > MathF.PI) a -= MathF.Tau;
        return a;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float InverseLerp(float a, float b, float v) => MathF.Abs(b - a) < 1e-6f ? 0f : (v - a) / (b - a);

    public static float Saturate(float v) => Math.Clamp(v, 0f, 1f);

    public static float SmoothStep(float e0, float e1, float x)
    {
        var t = Saturate((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    public static float MoveToward(float from, float to, float maxDelta) =>
        MathF.Abs(to - from) <= maxDelta ? to : from + MathF.Sign(to - from) * maxDelta;

    /// <summary>Four corners (CCW order irrelevant for convex tests) of an oriented rectangle.</summary>
    public static void RectCorners(Vector2 center, float heading, float halfWidth, float halfLength, Span<Vector2> out4)
    {
        var f = Forward(heading) * halfLength;
        var l = Left(heading) * halfWidth;
        out4[0] = center + f + l;
        out4[1] = center + f - l;
        out4[2] = center - f - l;
        out4[3] = center - f + l;
    }
}
