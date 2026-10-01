using System.Text.Json.Serialization;
using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Pois.Components;

/// <summary>An amount of a fill type (production inputs).</summary>
public class FillAmountDef
{
    public string FillType { get; set; } = "";
    public float Amount { get; set; }
}

/// <summary>What a production cycle makes. It goes into the POI's fill units first.</summary>
public sealed class ProductionOutputDef : FillAmountDef
{
    public static readonly string[] Modes = ["store", "sell", "pallet"];

    /// <summary>
    /// "store": kept for the owner's trailers at a silo's spout. "sell": sold every hour, for the owner. "pallet": put on
    /// pallets of type <see cref="Pallet"/> in the production point's pallet area, for the owner to carry away.
    /// </summary>
    public string Mode { get; set; } = "store";
    /// <summary>"pallet" mode: the pallets it goes on, an object type (objects/) with a pallet.</summary>
    public string? Pallet { get; set; }
}

/// <summary>One production: what a cycle takes from the POI's fill units and puts into them, and when it runs.</summary>
public sealed class ProductionDef : IConditions, IPriced
{
    /// <summary>Unique on the POI; saves refer to it.</summary>
    public string Id { get; set; } = "";
    /// <summary>Game hours per cycle (below 1 for several cycles an hour).</summary>
    public float CycleHours { get; set; } = 1f;
    public FillAmountDef[] Inputs { get; set; } = [];
    public ProductionOutputDef[] Outputs { get; set; } = [];
    /// <summary>What the owner pays for each hour it runs.</summary>
    public float RunningCost { get; set; }
    /// <summary>On the market price of the outputs it sells.</summary>
    public float PriceFactor { get; set; } = 1f;
    public Dictionary<string, float> PriceFactors { get; set; } = new();
    public float[]? OpenHours { get; set; }
    public int[] Months { get; set; } = [];

    [JsonIgnore]
    public string Verb => "works";
}

/// <summary>
/// Turns goods into others every hour (FS: productionPoint): a mill's wheat into flour. Its inputs come from the POI's
/// fill units (filled by a selling station or a silo) and its outputs go there, to be sold or loaded. A production short
/// of inputs or room starts its cycle over; a closed one waits with its cycle half done.
/// </summary>
public sealed class ProductionPointDef : PoiComponentDef
{
    public ProductionDef[] Productions { get; set; } = [];
    /// <summary>Where the outputs in "pallet" mode come out on pallets (FS: a pallet spawner), in rows from its front.</summary>
    public AreaDef? Pallets { get; set; }

    internal override IEnumerable<(string type, AreaDef area)> Triggers => Pallets != null ? [("pallets", Pallets)] : [];

    internal override IEnumerable<string> Errors(PoiDef poi, ContentDatabase content)
    {
        if (Productions.Length == 0) yield return "needs productions";
        foreach (var id in Productions.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"production '{id}' is defined more than once";
        var units = poi.Get<FillUnitsDef>();
        if (units == null) yield return "needs the POI's fillUnits to keep its inputs and outputs";
        foreach (var p in Productions)
        {
            var what = $"production '{p.Id}'";
            if (string.IsNullOrWhiteSpace(p.Id)) yield return "a production has no id";
            if (p.Inputs.Length == 0 || p.Outputs.Length == 0) yield return $"{what}: needs inputs and outputs";
            if (p.CycleHours <= 0 || p.RunningCost < 0) yield return $"{what}: cycleHours must be > 0 and runningCost >= 0";
            FillAmountDef[] io = [.. p.Inputs, .. p.Outputs];
            foreach (var e in Check(p, content, io.Select(x => x.FillType))) yield return $"{what}: {e}";
            foreach (var e in Priced.Errors(p, p.Outputs.Select(o => o.FillType))) yield return $"{what}: {e}";
            foreach (var x in io)
            {
                if (x.Amount <= 0) yield return $"{what}: amounts must be > 0";
                if (units != null && !units.Units.Any(u => u.FillTypes.Contains(x.FillType))) yield return $"{what}: the POI's fillUnits do not keep '{x.FillType}'";
            }
            foreach (var o in p.Outputs.Where(o => !ProductionOutputDef.Modes.Contains(o.Mode)))
                yield return $"{what}: output '{o.FillType}' mode must be {string.Join(", ", ProductionOutputDef.Modes)}";
            foreach (var o in p.Outputs.Where(o => o.Mode == "pallet"))
            {
                if (Pallets == null) yield return $"{what}: output '{o.FillType}' goes on pallets: the production point needs a pallets area";
                if (content.Objects.GetValueOrDefault(o.Pallet ?? "") is not { } pallet || pallet.Get<Objects.Components.PalletDef>() == null)
                    yield return $"{what}: output '{o.FillType}' needs the pallet it goes on (objects/, with a pallet)";
                else if (pallet.Get<FillUnitsDef>()?.Units is [var unit] && !unit.FillTypes.Contains(o.FillType))
                    yield return $"{what}: a '{o.Pallet}' pallet doesn't take '{o.FillType}'";
            }
        }
        if (Pallets?.Error() is { } error) yield return $"pallets: {error}";
    }

    internal override Component Create(Poi poi) => new ProductionPoint(poi, this);
}

public sealed class ProductionPointSave
{
    /// <summary>The part of a cycle done so far, by production id.</summary>
    public Dictionary<string, float> Progress { get; set; } = new();
}

public sealed class ProductionPoint(Poi poi, ProductionPointDef def) : PoiComponent<ProductionPointDef, ProductionPointSave>(poi, def)
{
    /// <summary>The part of a cycle done so far, by production id.</summary>
    internal Dictionary<string, float> Progress { get; } = new();

    /// <summary>Where its goods are: the POI's fill units.</summary>
    public FillUnits Storage => Poi.Get<FillUnits>()!;

    protected override ProductionPointSave Capture(ContentDatabase content) => new() { Progress = Progress.Where(kv => kv.Value > 0f).ToDictionary() };

    protected override void Restore(ProductionPointSave save, SaveContext context)
    {
        foreach (var (id, progress) in save.Progress)
            if (Def.Productions.Any(p => p.Id == id)) Progress[id] = Math.Clamp(progress, 0f, 1f);
    }
}
