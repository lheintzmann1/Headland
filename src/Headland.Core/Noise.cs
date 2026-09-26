namespace Headland.Core;

/// <summary>Seeded 2D value noise with fractal Brownian motion. Output roughly in [-1, 1].</summary>
public sealed class ValueNoise
{
    private readonly int _seed;

    public ValueNoise(int seed) => _seed = seed;

    public float Sample(float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = x - x0;
        var ty = y - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);

        var a = Rng.Hash01(x0, y0, _seed);
        var b = Rng.Hash01(x0 + 1, y0, _seed);
        var c = Rng.Hash01(x0, y0 + 1, _seed);
        var d = Rng.Hash01(x0 + 1, y0 + 1, _seed);

        var v = MathUtil.Lerp(MathUtil.Lerp(a, b, tx), MathUtil.Lerp(c, d, tx), ty);
        return v * 2f - 1f;
    }

    public float Fbm(float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += Sample(x + i * 17.31f, y - i * 9.73f) * amp;
            norm += amp;
            x *= lacunarity;
            y *= lacunarity;
            amp *= gain;
        }
        return sum / norm;
    }
}
