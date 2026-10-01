using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Input;
using Headland.Core.Objects;
using Headland.Core.Objects.Components;

namespace Headland.Core.Machines.Components;

/// <summary>
/// A baler's chamber (FS: Baler): its pickup (a work area of type baler) fills a fill unit with what lies cut, and each
/// time it's full it drops a bale of type <see cref="Bale"/> behind it, at x, z, and goes on. The unload key drops what
/// it holds as a smaller bale (at the end of a field).
/// </summary>
public sealed class BalerDef : MachineComponentDef, ISpecSource
{
    /// <summary>The chamber: the fill unit the pickup fills.</summary>
    public string FillUnit { get; set; } = "chamber";
    /// <summary>The bales it makes: an object type (objects/) with a bale.</summary>
    public string Bale { get; set; } = "";
    /// <summary>Where the bales land, in its space.</summary>
    public float X { get; set; }
    public float Z { get; set; } = -2f;

    /// <summary>The bales it makes, and how much goes into one.</summary>
    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content)
    {
        if (content.Objects.GetValueOrDefault(Bale) is not { } bale) yield break;
        var unit = owner.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == FillUnit);
        yield return new Spec("Bales", unit != null ? $"{bale.Name}, {unit.Capacity:N0} {Spec.UnitOf(unit, content)}" : bale.Name);
    }

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        var unit = machine.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == FillUnit);
        if (unit == null) yield return $"fill unit '{FillUnit}' missing";
        if (content.Objects.GetValueOrDefault(Bale) is not { } bale) yield return $"unknown bale '{Bale}' (objects/)";
        else if (bale.Get<BaleDef>() == null || bale.Get<FillUnitsDef>()?.Units is not [var made]) yield return $"'{Bale}' is not a bale";
        else if (unit != null && (made.Capacity < unit.Capacity || unit.FillTypes.Any(ft => !made.FillTypes.Contains(ft))))
            yield return $"a bale of '{Bale}' can't hold what the chamber does: room for {unit.Capacity:N0}, and its fill types";
        if (machine.Get<WorkAreasDef>()?.Areas.Any(a => a.Type == "baler") != true) yield return "needs a work area of type baler: its pickup";
    }

    internal override Component Create(Machine machine) => new Baler(machine, this);
}

public sealed class Baler(Machine machine, BalerDef def) : MachineComponent<BalerDef>(machine, def), IActionSource, IConditionSource
{
    public FillUnit Chamber => Machine.Unit(Def.FillUnit)!;

    /// <summary>Why the pickup left something lying, until it picks something up again: the chamber holds something else.</summary>
    internal MachineCondition? Refused { get; set; }

    /// <summary>The chamber holds something else than what lies under the pickup, until its bale is dropped.</summary>
    public IEnumerable<MachineCondition> Conditions => Refused is BalerHolds h && Chamber.FillType == h.FillType.Id ? [Refused] : [];

    /// <summary>The unload key drops what the chamber holds as a bale.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Chamber.IsEmpty) return;
        actions.Add(InputActions.Unload, $"Drop bale ({Chamber.Fraction * 100f:0}%)", () => Drop(sim));
    }

    /// <summary>Drops what the chamber holds as a bale behind it (null when it's empty); the chamber is empty after.</summary>
    public WorldObject? Drop(Simulation sim)
    {
        var chamber = Chamber;
        if (chamber.IsEmpty)
        {
            chamber.Remove(chamber.Level);
            return null;
        }
        var bale = sim.Objects.Spawn(Def.Bale, Machine.LocalToWorld(Def.X, Def.Z), Machine.Heading, Machine.FarmId);
        bale.Content!.Add(chamber.FillType!, chamber.Level);
        chamber.Remove(chamber.Level);
        sim.Events.Publish(new BaleMade(Machine, bale));
        return bale;
    }

    internal override void OnDetached() => Refused = null;
}
