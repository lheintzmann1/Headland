using System.Numerics;
using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>Oriented box on the ground plane. HalfExtents.X is along Left, HalfExtents.Y along Forward.</summary>
public readonly record struct Obb(Vector2 Center, Vector2 HalfExtents, float Heading)
{
    public Vector2 AxisX => MathUtil.Left(Heading);
    public Vector2 AxisY => MathUtil.Forward(Heading);

    public bool Contains(Vector2 p, float margin = 0f)
    {
        var d = p - Center;
        return MathF.Abs(Vector2.Dot(d, AxisX)) <= HalfExtents.X + margin &&
               MathF.Abs(Vector2.Dot(d, AxisY)) <= HalfExtents.Y + margin;
    }

    public Vector2 ClosestPoint(Vector2 p)
    {
        var d = p - Center;
        var x = Math.Clamp(Vector2.Dot(d, AxisX), -HalfExtents.X, HalfExtents.X);
        var y = Math.Clamp(Vector2.Dot(d, AxisY), -HalfExtents.Y, HalfExtents.Y);
        return Center + AxisX * x + AxisY * y;
    }

    public float Distance(Vector2 p) => Vector2.Distance(ClosestPoint(p), p);

    public float BoundingRadius => HalfExtents.Length();
}

public static class Geometry
{
    public static bool Overlaps(in Obb a, in Obb b)
    {
        if (Vector2.DistanceSquared(a.Center, b.Center) > MathF.Pow(a.BoundingRadius + b.BoundingRadius, 2)) return false;
        Span<Vector2> axes = [a.AxisX, a.AxisY, b.AxisX, b.AxisY];
        var d = b.Center - a.Center;
        foreach (var axis in axes)
        {
            var ra = a.HalfExtents.X * MathF.Abs(Vector2.Dot(a.AxisX, axis)) + a.HalfExtents.Y * MathF.Abs(Vector2.Dot(a.AxisY, axis));
            var rb = b.HalfExtents.X * MathF.Abs(Vector2.Dot(b.AxisX, axis)) + b.HalfExtents.Y * MathF.Abs(Vector2.Dot(b.AxisY, axis));
            if (MathF.Abs(Vector2.Dot(d, axis)) > ra + rb) return false;
        }
        return true;
    }

    public static bool Overlaps(in Obb box, Vector2 circle, float radius) => box.Distance(circle) < radius;

    public static bool Overlaps(in Obb box, Obstacle o) => o.Shape == ObstacleShape.Circle
        ? Overlaps(box, o.Center, o.Radius)
        : Overlaps(box, new Obb(o.Center, o.HalfExtents, o.Heading));

    /// <summary>Andrew's monotone chain. Writes the hull (CCW) into <paramref name="hull"/> and returns its length.</summary>
    public static int ConvexHull(Span<Vector2> points, Span<Vector2> hull)
    {
        var n = points.Length;
        if (n < 3)
        {
            points.CopyTo(hull);
            return n;
        }
        points.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }
        for (int i = n - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }
        return k - 1;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    public static bool InsideConvex(ReadOnlySpan<Vector2> hullCcw, Vector2 p)
    {
        var n = hullCcw.Length;
        if (n < 3) return false;
        for (var i = 0; i < n; i++)
            if (Cross(hullCcw[i], hullCcw[(i + 1) % n], p) < -1e-5f) return false;
        return true;
    }

    /// <summary>
    /// Collects cells whose centers lie inside the convex hull of the given points (e.g. the corners of a work
    /// area at its previous and current pose, which covers the swept region without gaps).
    /// </summary>
    public static void RasterizeConvex(ReadOnlySpan<Vector2> points, float cellSize, int cellsX, int cellsZ, List<int> cellIndices)
    {
        Span<Vector2> pts = stackalloc Vector2[points.Length];
        points.CopyTo(pts);
        Span<Vector2> hull = stackalloc Vector2[points.Length + 1];
        var n = ConvexHull(pts, hull);
        if (n < 3) return;
        var poly = hull[..n];

        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var p in poly)
        {
            minX = MathF.Min(minX, p.X);
            minZ = MathF.Min(minZ, p.Y);
            maxX = MathF.Max(maxX, p.X);
            maxZ = MathF.Max(maxZ, p.Y);
        }
        var cx0 = Math.Max(0, (int)MathF.Floor(minX / cellSize));
        var cz0 = Math.Max(0, (int)MathF.Floor(minZ / cellSize));
        var cx1 = Math.Min(cellsX - 1, (int)MathF.Floor(maxX / cellSize));
        var cz1 = Math.Min(cellsZ - 1, (int)MathF.Floor(maxZ / cellSize));
        for (var cz = cz0; cz <= cz1; cz++)
        for (var cx = cx0; cx <= cx1; cx++)
        {
            var c = new Vector2((cx + 0.5f) * cellSize, (cz + 0.5f) * cellSize);
            if (InsideConvex(poly, c)) cellIndices.Add(cz * cellsX + cx);
        }
    }
}
