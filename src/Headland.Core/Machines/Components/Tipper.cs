using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>A side a bed tips to (FS: tipSide): where its load falls, and how the bed tilts.</summary>
public sealed class TipSideDef
{
    /// <summary>What the key hint calls it: "Tip side (left)".</summary>
    public string Name { get; set; } = "";
    /// <summary>Where the load falls, in the machine's space: the unloading area must be under it.</summary>
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>The bed tipped: turned by [x, y, z] degrees; by default its front lifted by the tipper's angleDeg.</summary>
    public float[]? RotationDeg { get; set; }
    /// <summary>What the bed turns about, [x, y, z] from its own pivot (the rear hinge): a side's hinge line.</summary>
    public float[] Pivot { get; set; } = [0f, 0f, 0f];
}

/// <summary>
/// A tipping bed: tips a fill unit into the POI unloading area under the side it tips to, hinged at its rear. With
/// several sides (FS: tip sides), a key steps through them while the bed is down.
/// </summary>
public sealed class TipperDef : MachineComponentDef, ISpecSource
{
    public string FillUnit { get; set; } = "main";
    public float RatePerSecond { get; set; } = 400f;
    /// <summary>How far the bed tilts up.</summary>
    public float AngleDeg { get; set; } = 42f;
    /// <summary>The sides it tips to; none: to the back, into the unloading area the machine stands in.</summary>
    public TipSideDef[] Sides { get; set; } = [];

    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content)
    {
        yield return new Spec("Tipping", $"{RatePerSecond:N0} {Spec.UnitOf(owner.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == FillUnit), content)}/s");
        if (Sides.Length > 1) yield return new Spec("Tips to", string.Join(", ", Sides.Select(s => s.Name)));
    }

    public override IEnumerable<string> Roles => ["tipper"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
        if (RatePerSecond <= 0f || AngleDeg is <= 0f or > 90f) yield return "ratePerSecond must be > 0 and angleDeg in (0, 90]";
        foreach (var name in Sides.GroupBy(s => s.Name).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"side '{name}' is defined more than once";
        foreach (var s in Sides)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) yield return "a side has no name";
            if (s.RotationDeg is { Length: not 3 } || s.Pivot.Length != 3) yield return $"side '{s.Name}': rotationDeg and pivot are [x, y, z]";
        }
    }

    internal override Component Create(Machine machine) => new Tipper(machine, this);
}

public sealed class TipperSave
{
    public bool Tipping { get; set; }
    public float Anim { get; set; }
    /// <summary>The side it tips to, by name.</summary>
    public string? Side { get; set; }
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

    /// <summary>The side it tips to, among its def's (0 with none).</summary>
    public int SideIndex { get; set; }

    /// <summary>The side it tips to, when its def gives sides.</summary>
    public TipSideDef? Side => Def.Sides.Length > 0 ? Def.Sides[SideIndex] : null;

    /// <summary>Where its load falls: under the side it tips to, or where the machine stands.</summary>
    public Vector2 Outlet => Side is { } side ? Machine.LocalToWorld(side.X, side.Z) : Machine.Footprint.Center;

    public FillUnit Load => Machine.Unit(Def.FillUnit)!;

    /// <summary>
    /// The unload key tips it, or stops it. Its hint shows in an unloading area, with what the load sells for there or
    /// the contract it goes to. With several sides, the tip side key steps to the next while the bed is down (FS: "Tip
    /// side (left)").
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        var pit = sim.Pois.TriggerAt(Outlet, "unload");
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
        if (Def.Sides.Length > 1 && !Tipping && Anim == 0f)
            actions.Add(InputActions.TipSide, $"Tip side ({Side!.Name})", () => SideIndex = (SideIndex + 1) % Def.Sides.Length, hinted: pit != null || !Load.IsEmpty);
    }

    /// <summary>Starts tipping, or says why it can't: empty, or the side it tips to not over an unloading area that takes its load.</summary>
    internal string? Start(Simulation sim)
    {
        var unit = Load;
        var pit = sim.Pois.TriggerAt(Outlet, "unload");
        if (unit.IsEmpty) return $"{Machine.Def.Name} is empty";
        if (pit == null) return Side is { } side && Def.Sides.Length > 1 ? $"Park with the trailer's {side.Name} over an unloading area to tip" : "Drive the trailer into an unloading area to tip";
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
        var pit = sim.Pois.TriggerAt(Outlet, "unload");
        if (unit.IsEmpty || pit == null || sim.Pois.UnloadBlocker(Machine, pit, unit.FillType!, unit.Level) != null)
        {
            Tipping = false;
            return;
        }
        if (Anim < 0.6f) return;
        Flowing = unit.Remove(sim.Pois.Unload(Machine, pit, unit.FillType!, MathF.Min(Def.RatePerSecond * dt, unit.Level))) > 0f;
    }

    internal override void OnDetached() => Tipping = false;

    protected override TipperSave Capture(ContentDatabase content) => new() { Tipping = Tipping, Anim = Anim, Side = Side?.Name };

    protected override void Restore(TipperSave save, SaveContext context)
    {
        Tipping = save.Tipping;
        Anim = Math.Clamp(save.Anim, 0f, 1f);
        SideIndex = Math.Max(0, Array.FindIndex(Def.Sides, s => s.Name == save.Side));
    }
}
