using System.Numerics;

namespace Headland.Core.World;

/// <summary>
/// A simple polygon on the ground plane (fields, farmland). Any winding; convex or concave. A point is inside by
/// the even-odd rule, which cell rasterization uses too, so both always agree.
/// </summary>
public sealed class Polygon
{
    private readonly Vector2[] _points;

    public Polygon(IEnumerable<Vector2> points)
    {
        _points = points.ToArray();
        if (_points.Length < 3) throw new ArgumentException("A polygon needs at least 3 points", nameof(points));
        Min = _points.Aggregate(Vector2.Min);
        Max = _points.Aggregate(Vector2.Max);
        double a = 0, cx = 0, cz = 0;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            var p = _points[j];
            var q = _points[i];
            var cross = (double)p.X * q.Y - (double)q.X * p.Y;
            a += cross;
            cx += (p.X + q.X) * cross;
            cz += (p.Y + q.Y) * cross;
        }
        Area = (float)Math.Abs(a * 0.5);
        Centroid = Math.Abs(a) > 1e-9 ? new Vector2((float)(cx / (3 * a)), (float)(cz / (3 * a))) : (Min + Max) * 0.5f;
    }

    public static Polygon Rect(float x, float z, float w, float h) =>
        new([new Vector2(x, z), new Vector2(x + w, z), new Vector2(x + w, z + h), new Vector2(x, z + h)]);

    public IReadOnlyList<Vector2> Points => _points;
    public Vector2 Min { get; }
    public Vector2 Max { get; }
    public Vector2 Size => Max - Min;
    /// <summary>Area in m².</summary>
    public float Area { get; }
    public Vector2 Centroid { get; }

    public bool Contains(Vector2 p)
    {
        if (p.X < Min.X || p.Y < Min.Y || p.X > Max.X || p.Y > Max.Y) return false;
        var inside = false;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            var a = _points[j];
            var b = _points[i];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X)) inside = !inside;
        }
        return inside;
    }

    /// <summary>Distance to the polygon: 0 inside, else to the nearest edge.</summary>
    public float Distance(Vector2 p)
    {
        if (Contains(p)) return 0f;
        var best = float.MaxValue;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            var a = _points[j];
            var ab = _points[i] - a;
            var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-9f), 0f, 1f);
            best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    /// <summary>
    /// Extent of the polygon inside a strip, for planning lanes: with <paramref name="alongZ"/> the strip is
    /// <c>u1 ≤ x ≤ u2</c> and the result is the z range covered, else the strip is on z and the result on x.
    /// Null when the strip misses the polygon.
    /// </summary>
    public (float min, float max)? Extent(bool alongZ, float u1, float u2)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            var (au, av) = alongZ ? (_points[j].X, _points[j].Y) : (_points[j].Y, _points[j].X);
            var (bu, bv) = alongZ ? (_points[i].X, _points[i].Y) : (_points[i].Y, _points[i].X);
            if (au > bu) (au, av, bu, bv) = (bu, bv, au, av);
            if (bu < u1 || au > u2) continue;
            var t1 = MathF.Max(u1, au);
            var t2 = MathF.Min(u2, bu);
            var span = bu - au;
            var v1 = span < 1e-6f ? av : av + (bv - av) * (t1 - au) / span;
            var v2 = span < 1e-6f ? bv : av + (bv - av) * (t2 - au) / span;
            lo = MathF.Min(lo, MathF.Min(v1, v2));
            hi = MathF.Max(hi, MathF.Max(v1, v2));
        }
        return lo <= hi ? (lo, hi) : null;
    }

    /// <summary>Visits every cell (x, z) whose center lies inside, row by row (scanline).</summary>
    public void Rasterize(float cellSize, int cellsX, int cellsZ, Action<int, int> visit)
    {
        var cz0 = Math.Max(0, (int)MathF.Floor(Min.Y / cellSize));
        var cz1 = Math.Min(cellsZ - 1, (int)MathF.Floor(Max.Y / cellSize));
        var xs = new List<float>();
        for (var cz = cz0; cz <= cz1; cz++)
        {
            var y = (cz + 0.5f) * cellSize;
            xs.Clear();
            for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
            {
                var a = _points[j];
                var b = _points[i];
                if ((a.Y > y) != (b.Y > y)) xs.Add(a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            xs.Sort();
            for (var k = 0; k + 1 < xs.Count; k += 2)
            {
                // Cells whose center x lies in [xs[k], xs[k + 1]).
                var cx0 = Math.Max(0, (int)MathF.Ceiling(xs[k] / cellSize - 0.5f));
                var cx1 = Math.Min(cellsX - 1, (int)MathF.Ceiling(xs[k + 1] / cellSize - 0.5f) - 1);
                for (var cx = cx0; cx <= cx1; cx++) visit(cx, cz);
            }
        }
    }
}
