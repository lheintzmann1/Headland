using Headland.Core.Content;
using Headland.Core.World;

namespace Headland.Core.Contracts;

/// <summary>A <see cref="FieldStateDef"/> compiled to test cells with.</summary>
internal readonly struct FieldState
{
    // Bits in FieldStateDef.CropStates order.
    private const int None = 1, Dead = 2, Sown = 4, Growing = 8, Harvestable = 16;

    /// <summary>A bit per <see cref="GroundType"/> and per crop state; 0 allows any.</summary>
    private readonly int _grounds;
    private readonly int _crops;

    public FieldState(FieldStateDef def)
    {
        foreach (var g in def.Ground) _grounds |= 1 << (int)WorldGen.ParseGround(g);
        foreach (var c in def.Crop) _crops |= 1 << Array.IndexOf(FieldStateDef.CropStates, c);
    }

    /// <summary>True when the state is about a living crop, which for a job is its own crop.</summary>
    public bool NamesCrop => (_crops & (Sown | Growing | Harvestable)) != 0;

    /// <summary>
    /// Whether cell <paramref name="i"/> is in this state. A living crop only counts when it is <paramref name="crop"/>
    /// (crop index + 1; 0 = any).
    /// </summary>
    public bool Matches(FieldLayers L, IReadOnlyList<CropDef> crops, int i, int crop)
    {
        if (_grounds != 0 && (_grounds & (1 << L.Ground[i])) == 0) return false;
        return _crops == 0 || (_crops & CropStates(L, crops, i, crop)) != 0;
    }

    private static int CropStates(FieldLayers L, IReadOnlyList<CropDef> crops, int i, int crop)
    {
        var id = L.Crop[i];
        if (id == 0) return None;
        if (L.Stage[i] == CropStage.Dead) return Dead;
        if (crop != 0 && id != crop) return 0;
        return Sown | (crops[id - 1].Stages[L.Stage[i]].Harvestable ? Harvestable : Growing);
    }
}
