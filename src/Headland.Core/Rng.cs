namespace Headland.Core;

/// <summary>
/// Small deterministic RNG (SplitMix64). Stable across .NET versions so seeds and saves stay reproducible.
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed;

    public ulong State
    {
        get => _state;
        set => _state = value;
    }

    public ulong NextU64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextU64() >> 40) * (1f / (1UL << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public int Range(int minInclusive, int maxExclusive) =>
        minInclusive + (int)(NextU64() % (ulong)Math.Max(1, maxExclusive - minInclusive));

    public bool Chance(float p) => NextFloat() < p;

    /// <summary>Standard normal via Box–Muller.</summary>
    public float NextGaussian()
    {
        var u1 = MathF.Max(NextFloat(), 1e-7f);
        var u2 = NextFloat();
        return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(MathF.Tau * u2);
    }

    /// <summary>Exponential distribution with the given mean.</summary>
    public float NextExponential(float mean) => -mean * MathF.Log(MathF.Max(1f - NextFloat(), 1e-7f));

    /// <summary>Stateless hash to [0,1) for per-cell/per-instance variation.</summary>
    public static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)seed * 0x27D4EB2Du;
            h ^= (uint)x * 0x85EBCA6Bu;
            h = (h << 13) | (h >> 19);
            h ^= (uint)y * 0xC2B2AE35u;
            h *= 0x165667B1u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            return (h >> 8) * (1f / (1u << 24));
        }
    }
}
