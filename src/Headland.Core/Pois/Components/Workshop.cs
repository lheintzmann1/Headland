using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Machines;

namespace Headland.Core.Pois.Components;

/// <summary>Fitting options: what the new ones cost more than those they replace, times a factor, and the work.</summary>
public sealed class ConfigureDef
{
    /// <summary>Multiplies the price of the options fitted.</summary>
    public float PriceFactor { get; set; } = 1f;
    /// <summary>The price of the work for each option changed.</summary>
    public float Price { get; set; }
}

/// <summary>
/// Repairs the machine chain parked in its bay (FS: workshop), for 1% of each machine's price for each 100% of wear
/// times its factor, and changes their options when it fits them.
/// </summary>
public sealed class WorkshopDef : StationDef
{
    public AreaDef Trigger { get; set; } = new();
    /// <summary>Multiplies the standard price of repairs.</summary>
    public float RepairPriceFactor { get; set; } = 1f;
    /// <summary>Fits options too (none: it only repairs).</summary>
    public ConfigureDef? Configure { get; set; }

    public override string Verb => "repairs";

    internal override IEnumerable<(string type, AreaDef area)> Triggers => [("repair", Trigger)];

    internal override IEnumerable<string> StationErrors(PoiDef poi, ContentDatabase content)
    {
        if (RepairPriceFactor <= 0f || Configure?.PriceFactor <= 0f) yield return "price factors must be > 0";
        if (Configure?.Price < 0f) yield return "configure.price must be >= 0";
    }

    internal override Component Create(Poi poi) => new Workshop(poi, this);
}

/// <summary>Machines in a workshop's bay have options to change: the game opens the workshop's screen for them.</summary>
public sealed record WorkshopMenu(Machine Vehicle, Workshop Workshop) : ActivationMenu;

public sealed class Workshop(Poi poi, WorkshopDef def) : PoiComponent<WorkshopDef>(poi, def), IActivatable
{
    /// <summary>Where machines park to be repaired or refitted.</summary>
    public PoiTrigger Bay => Trigger("repair")!;

    /// <summary>
    /// The use key repairs the chain in its bay (driven in, or the farm's machines there when the farmer walks in), or
    /// opens its screen where it fits options too.
    /// </summary>
    public IEnumerable<Activation> Activations(ActivationUser user, Simulation sim)
    {
        if (user.InBay(Bay.Area, sim.Machines.All) is not var (root, distance)) yield break;
        var chain = root.Chain().ToList();
        var cost = chain.Sum(m => sim.Pois.RepairPrice(this, m));
        var options = Def.Configure != null && chain.Any(m => m.Def.Configurations.Count > 0);
        var what = new List<string>();
        if (cost > 0.5f) what.Add($"Repair (${cost:N0})");
        if (options) what.Add("Change options…");
        yield return new Activation(what.Count > 0 ? Activation.Join(what) : "Repair", this, distance)
        {
            Run = () => sim.Pois.Repair(chain, this),
            Blocked = sim.Pois.Closed(Poi, Def) ?? (what.Count == 0 ? "Nothing to repair" : null),
            Menu = options ? new WorkshopMenu(root, this) : null,
        };
    }
}
