using Headland.Core.Content;

namespace Headland.Core.Pois;

/// <summary>Goods kept at a POI, each fill type with its own room. They belong to the POI's farm.</summary>
public sealed class PoiStorage(PoiStorageDef def)
{
    private readonly Dictionary<string, float> _levels = new();

    public PoiStorageDef Def { get; } = def;
    /// <summary>What is in store, by fill type (fill types never stored are missing).</summary>
    public IReadOnlyDictionary<string, float> Levels => _levels;

    public bool Keeps(string fillType) => Def.FillTypes.Contains(fillType);
    public float Level(string fillType) => _levels.GetValueOrDefault(fillType);
    public float Capacity(string fillType) => Keeps(fillType) ? Def.CapacityOf(fillType) : 0f;
    public float Free(string fillType) => MathF.Max(0f, Capacity(fillType) - Level(fillType));

    /// <summary>Stores up to <paramref name="amount"/>; returns what fitted.</summary>
    public float Add(string fillType, float amount)
    {
        var added = MathF.Min(amount, Free(fillType));
        if (added > 0f) _levels[fillType] = Level(fillType) + added;
        return MathF.Max(0f, added);
    }

    /// <summary>Takes out up to <paramref name="amount"/>; returns what there was.</summary>
    public float Remove(string fillType, float amount)
    {
        var removed = MathF.Min(amount, Level(fillType));
        if (removed > 0f) _levels[fillType] = Level(fillType) - removed;
        return MathF.Max(0f, removed);
    }

    internal void Set(string fillType, float level) => _levels[fillType] = Math.Clamp(level, 0f, Capacity(fillType));
}
