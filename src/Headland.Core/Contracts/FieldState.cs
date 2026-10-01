using Headland.Core.Content;
using Headland.Core.World;

namespace Headland.Core.Contracts;

/// <summary>A <see cref="FieldStateDef"/> compiled to test cells with.</summary>
internal readonly struct FieldState
{
    // Bits in FieldStateDef.CropStates order.
    private const int None = 1, Dead = 2, Sown = 4, Growing = 8, Harvestable = 16;

    /// <summary>A bit per <see cref="GroundType"/>, per crop state and per <see cref="WeedState"/>; 0 allows any.</summary>
    private readonly int _grounds;
    private readonly int _crops;
    private readonly int _weeds;
    private readonly bool? _fertilized;
    private readonly bool? _windrow;

    public FieldState(FieldStateDef def)
    {
        foreach (var g in def.Ground) _grounds |= 1 << (int)WorldGen.ParseGround(g);
        foreach (var c in def.Crop) _crops |= 1 << Array.IndexOf(FieldStateDef.CropStates, c);
        foreach (var w in def.Weeds) _weeds |= 1 << Array.IndexOf(WeedState.Names, w);
        _fertilized = def.Fertilized;
        _windrow = def.Windrow;
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
        if (_weeds != 0 && (_weeds & (1 << L.Weeds[i])) == 0) return false;
        if (_fertilized is { } fertilized && L.Fertilized[i] > 0 != fertilized) return false;
        if (_windrow is { } windrow && Windrows.Has(L, i) != windrow) return false;
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
