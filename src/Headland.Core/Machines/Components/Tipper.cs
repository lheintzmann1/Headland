using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>A tipping bed: tips a fill unit into the POI unloading area the machine stands in, hinged at its rear.</summary>
public sealed class TipperDef : MachineComponentDef
{
    public string FillUnit { get; set; } = "main";
    public float RatePerSecond { get; set; } = 400f;
    /// <summary>How far the bed tilts up.</summary>
    public float AngleDeg { get; set; } = 42f;

    public override IEnumerable<string> Roles => ["tipper"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
        if (RatePerSecond <= 0f || AngleDeg is <= 0f or > 90f) yield return "ratePerSecond must be > 0 and angleDeg in (0, 90]";
    }

    internal override Component Create(Machine machine) => new Tipper(machine, this);
}

public sealed class TipperSave
{
    public bool Tipping { get; set; }
    public float Anim { get; set; }
}

public sealed class Tipper(Machine machine, TipperDef def) : MachineComponent<TipperDef, TipperSave>(machine, def), IActionSource
{
    /// <summary>Up in about 3 s; the load starts to slide at 60%.</summary>
    private const float TipRate = 0.35f;

    public bool Tipping { get; set; }
    /// <summary>0 down … 1 up, smoothed.</summary>
    public float Anim { get; set; }
    /// <summary>The load slid out on the last tick.</summary>
    public bool Flowing { get; private set; }

    public FillUnit Load => Machine.Unit(Def.FillUnit)!;

    /// <summary>
    /// The unload key tips it, or stops it. Its hint shows in an unloading area, with what the load sells for there or
    /// the contract it goes to.
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        var pit = sim.Pois.TriggerAt(Machine.Footprint.Center, "unload");
        var label = "Tip";
        if (pit != null)
        {
            var ft = Load.FillType;
            var price = ft != null ? sim.Pois.SalePrice(Machine, pit, ft) : null;
            var at = price is { } p ? $" at ${p:0.00}/{sim.Content.FillTypes[ft!].Unit}" : "";
            if (ft != null && sim.Contracts.Taking(Machine.FarmId, pit.Poi, ft) is { } job) at = $" for the contract ({job.Owed:N0} {job.Goods!.Unit} to go)";
            label = $"Tip into {pit.Poi.Name}{at}";
        }
        actions.Toggle(InputActions.Unload, Tipping, label, "Stop tipping", tip =>
        {
            if (!tip) Tipping = false;
            else if (Start(sim) is { } why) sim.Notifications.Post(why, Load.IsEmpty ? Severity.Info : Severity.Warning);
            return null;
        }, hinted: pit != null || Tipping);
    }

    /// <summary>Starts tipping, or says why it can't: empty, or not in an unloading area that takes its load.</summary>
    internal string? Start(Simulation sim)
    {
        var unit = Load;
        var pit = sim.Pois.TriggerAt(Machine.Footprint.Center, "unload");
        if (unit.IsEmpty) return $"{Machine.Def.Name} is empty";
        if (pit == null) return "Drive the trailer into an unloading area to tip";
        if (sim.Pois.UnloadBlocker(Machine, pit, unit.FillType!, unit.Level) is { } why) return why;
        Tipping = true;
        return null;
    }

    internal override void Update(Simulation sim, float dt)
    {
        Anim = MathUtil.MoveToward(Anim, Tipping ? 1f : 0f, dt * TipRate);
        Flowing = false;
        if (!Tipping) return;
        var unit = Load;
        var pit = sim.Pois.TriggerAt(Machine.Footprint.Center, "unload");
        if (unit.IsEmpty || pit == null || sim.Pois.UnloadBlocker(Machine, pit, unit.FillType!, unit.Level) != null)
        {
            Tipping = false;
            return;
        }
        if (Anim < 0.6f) return;
        Flowing = unit.Remove(sim.Pois.Unload(Machine, pit, unit.FillType!, MathF.Min(Def.RatePerSecond * dt, unit.Level))) > 0f;
    }

    internal override void OnDetached() => Tipping = false;

    protected override TipperSave Capture(ContentDatabase content) => new() { Tipping = Tipping, Anim = Anim };

    protected override void Restore(TipperSave save, SaveContext context)
    {
        Tipping = save.Tipping;
        Anim = Math.Clamp(save.Anim, 0f, 1f);
    }
}
