using Headland.Core.Components;
using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>
/// An unloading pipe emptying a fill unit from its outlet at x, z: into a machine under it, else into a POI's unloading
/// area. It rests folded backward and swings out to the left.
/// </summary>
public sealed class PipeDef : MachineComponentDef, ISpecSource
{
    public string FillUnit { get; set; } = "tank";
    public float X { get; set; } = 4f;
    public float Z { get; set; } = 1f;
    public float RatePerSecond { get; set; } = 150f;

    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content) =>
        [new("Unloading", $"{RatePerSecond:N0} {Spec.UnitOf(owner.Get<FillUnitsDef>()?.Units.FirstOrDefault(u => u.Id == FillUnit), content)}/s")];

    public override IEnumerable<string> Roles => ["pipe"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!HasUnit(machine, FillUnit)) yield return $"fill unit '{FillUnit}' missing";
        if (RatePerSecond <= 0f) yield return "ratePerSecond must be > 0";
    }

    internal override Component Create(Machine machine) => new Pipe(machine, this);
}

public sealed class PipeSave
{
    public bool Out { get; set; }
    public float Anim { get; set; }
}

public sealed class Pipe(Machine machine, PipeDef def) : MachineComponent<PipeDef, PipeSave>(machine, def), IActionSource
{
    /// <summary>Unfolded in 2.5 s.</summary>
    private const float FoldRate = 0.4f;

    /// <summary>Unfolded (or unfolding).</summary>
    public bool Out { get; set; }
    /// <summary>0 folded … 1 out, smoothed.</summary>
    public float Anim { get; set; }
    /// <summary>Grain ran out of it on the last tick.</summary>
    public bool Flowing { get; private set; }

    public FillUnit Tank => Machine.Unit(Def.FillUnit)!;
    public Vector2 Outlet => Machine.LocalToWorld(Def.X, Def.Z);

    /// <summary>The unload key swings it out and back.</summary>
    public void AddActions(ActionList actions, Simulation sim) =>
        actions.Toggle(InputActions.Unload, Out, "Unfold pipe", "Fold pipe", unfold =>
        {
            Out = unfold;
            return null;
        });

    internal override void Update(Simulation sim, float dt)
    {
        Anim = MathUtil.MoveToward(Anim, Out ? 1f : 0f, dt * FoldRate);
        Flowing = false;
        var tank = Tank;
        if (!Out || Anim < 0.95f || tank.IsEmpty) return;
        var outlet = Outlet;
        var ft = tank.FillType!;
        var amount = MathF.Min(Def.RatePerSecond * dt, tank.Level);
        // A machine under the pipe first, else a POI's unloading area.
        var moved = 0f;
        if (sim.Machines.FindReceiver(outlet, ft, Machine) is { } target) moved = tank.Remove(target.Add(ft, amount));
        else if (sim.Pois.TriggerAt(outlet, "unload") is { } pit && sim.Pois.UnloadBlocker(Machine, pit, ft, tank.Level) == null)
            moved = tank.Remove(sim.Pois.Unload(Machine, pit, ft, amount));
        Flowing = moved > 0f;
    }

    protected override PipeSave Capture(ContentDatabase content) => new() { Out = Out, Anim = Anim };

    protected override void Restore(PipeSave save, SaveContext context)
    {
        Out = save.Out;
        Anim = Math.Clamp(save.Anim, 0f, 1f);
    }
}
