using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>
/// A store for its owner's goods (FS: silo): loads tipped or piped into its unloading pit go into the POI's fill
/// units, and the owner's trailers parked under its loading spout fill up from them.
/// </summary>
public sealed class SiloDef : StationDef
{
    /// <summary>Where loads are tipped or piped in (none: it only loads).</summary>
    public AreaDef? UnloadTrigger { get; set; }
    /// <summary>Where trailers are loaded (none: it only stores).</summary>
    public AreaDef? LoadTrigger { get; set; }
    /// <summary>Units per second poured into a trailer at the spout.</summary>
    public float LoadRate { get; set; } = 400f;
    /// <summary>What it stores and loads: these fill types and those of <see cref="FillTypeCategories"/> (both empty: all its fill units keep).</summary>
    public string[] FillTypes { get; set; } = [];
    public string[] FillTypeCategories { get; set; } = [];
    /// <summary>Smallest load it takes.</summary>
    public float MinAmount { get; set; }

    public override string Verb => "stores";

    internal override IEnumerable<(string type, AreaDef area)> Triggers =>
        new[] { ("unload", UnloadTrigger), ("load", LoadTrigger) }.Where(t => t.Item2 != null).Select(t => (t.Item1, t.Item2!));

    internal override IEnumerable<string> FillTypesOf(PoiDef poi) => FillTypes;

    internal override void Link(ContentDatabase content) => FillTypes = content.WithCategories(FillTypes, FillTypeCategories);

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        foreach (var error in content.CategoryErrors(FillTypeCategories)) yield return error;
        if (UnloadTrigger == null && LoadTrigger == null) yield return "needs an unloadTrigger, a loadTrigger or both";
        if (LoadRate <= 0f) yield return "loadRate must be > 0";
        if (MinAmount < 0f) yield return "minAmount must be >= 0";
        if (poi.Get<FillUnitsDef>() is not { } units) yield return "needs the POI's fillUnits to keep the goods";
        else
            foreach (var ft in FillTypes.Where(f => !units.Units.Any(u => u.FillTypes.Contains(f))))
                yield return $"the POI's fillUnits do not keep '{ft}'";
    }

    internal override Component Create(Poi poi) => new Silo(poi, this);
}

public sealed class Silo(Poi poi, SiloDef def) : PoiComponent<SiloDef>(poi, def)
{
    /// <summary>Where its goods are: the POI's fill units.</summary>
    public FillUnits Storage => Poi.Get<FillUnits>()!;

    /// <summary>What it stores and loads.</summary>
    public IReadOnlyList<string> FillTypes => Def.FillTypes.Length > 0
        ? Def.FillTypes
        : Storage.Units.SelectMany(u => u.Def.FillTypes).Distinct().ToArray();
}
