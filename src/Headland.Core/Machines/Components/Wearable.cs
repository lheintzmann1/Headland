using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Events;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Wear (FS: wearable): the machine's condition drops while it moves or works, faster on a field and much faster
/// working, and a workshop brings it back. A worn machine does worse, the more so the more worn: an engine loses power and
/// burns more, an implement works slower and uses more of what it spreads or sows. Its paint wears too as it drives,
/// faster on a field, until a workshop repaints it: that only shows, and takes from what it sells for.
/// </summary>
public sealed class WearableDef : MachineComponentDef
{
    /// <summary>Operating hours (real time, moving on a road) from 100% to 0%.</summary>
    public float Hours { get; set; } = 16f;
    /// <summary>How much faster it wears on a field.</summary>
    public float FieldFactor { get; set; } = 2f;
    /// <summary>How much faster again it wears working (a header cutting, a cultivator in the ground, a thresher on).</summary>
    public float WorkFactor { get; set; } = 5f;
    /// <summary>At 0%: the share of its engine's power lost.</summary>
    public float PowerLoss { get; set; } = 0.3f;
    /// <summary>At 0%: the share of an implement's work speed lost.</summary>
    public float SpeedLoss { get; set; } = 0.3f;
    /// <summary>At 0%: how much more fuel it burns, and seed, fertilizer or herbicide it uses (0.3 = 30% more).</summary>
    public float UsageIncrease { get; set; } = 0.3f;
    /// <summary>Hours of driving (real time, on a road) its paint lasts; <see cref="FieldFactor"/> times faster on a field.</summary>
    public float PaintHours { get; set; } = 60f;

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Hours <= 0f || FieldFactor < 1f || WorkFactor < 1f) yield return "hours must be > 0, fieldFactor and workFactor >= 1";
        if (PaintHours <= 0f) yield return "paintHours must be > 0";
        if (PowerLoss is < 0f or >= 1f || SpeedLoss is < 0f or >= 1f || UsageIncrease < 0f)
            yield return "powerLoss and speedLoss must be in [0, 1), usageIncrease >= 0";
    }

    internal override Component Create(Machine machine) => new Wearable(machine, this);
}

public sealed class WearableSave
{
    /// <summary>Saved to the last digit, so a loaded game wears on exactly as the one saved.</summary>
    public double Condition { get; set; } = 1.0;
    public double Paint { get; set; } = 1.0;
}

public sealed class Wearable(Machine machine, WearableDef def) : MachineComponent<WearableDef, WearableSave>(machine, def), IConditionSource, IReadoutSource
{
    /// <summary>Below this condition the machine is worn: the farmer is told to have it repaired.</summary>
    public const float WornBelow = 0.2f;

    /// <summary>Kept in doubles: a tick's wear is too little for a float's precision near 100%.</summary>
    private double _condition = 1.0, _paint = 1.0;

    /// <summary>1 = like new, 0 = worn out.</summary>
    public float Condition
    {
        get => (float)_condition;
        set => _condition = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>1 − <see cref="Condition"/>: what repairs make good.</summary>
    public float Wear => 1f - Condition;

    /// <summary>1 = fresh paint, 0 = worn through.</summary>
    public float Paint
    {
        get => (float)_paint;
        set => _paint = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>1 − <see cref="Paint"/>: what a repaint makes good.</summary>
    public float PaintWear => 1f - Paint;

    /// <summary>The share of its engine's power it has.</summary>
    public float PowerFactor => 1f - Def.PowerLoss * Wear;
    /// <summary>The share of its work areas' speed it works at.</summary>
    public float SpeedFactor => 1f - Def.SpeedLoss * Wear;
    /// <summary>What it burns and spreads, relative to a new one.</summary>
    public float UsageFactor => 1f + Def.UsageIncrease * Wear;

    /// <summary>Its condition (FS: the damage gauge), worn below <see cref="WornBelow"/>.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim) =>
        [new Gauge("condition", "Condition", Condition, $"{Condition * 100f:0}%", Condition < WornBelow ? Tone.Warning : Tone.Normal)];

    public IEnumerable<MachineCondition> Conditions => Condition < WornBelow ? [new Worn(Condition)] : [];

    internal override void Update(Simulation sim, float dt)
    {
        var m = Machine;
        var moving = MathF.Abs(m.Root.Speed) > 0.05f;
        var working = Working(m, moving) || m.Get<Motor>() != null && m.Chain().Skip(1).Any(c => Working(c, moving));
        if (!moving && !working) return;
        var (cx, cz) = sim.World.WorldToCell(m.Position);
        var field = sim.World.InBounds(cx, cz) && sim.World.Layers.FieldId[sim.World.CellIndex(cx, cz)] > 0 ? Def.FieldFactor : 1.0;
        if (moving) _paint = Math.Max(0.0, _paint - dt / (Def.PaintHours * 3600.0) * field);
        var factor = working ? field * Def.WorkFactor : field;
        var before = Condition;
        _condition = Math.Max(0.0, _condition - dt / (Def.Hours * 3600.0) * factor);
        if (before >= WornBelow && Condition < WornBelow) sim.Events.Publish(new MachineWorn(m));
    }

    /// <summary>A machine works when its work areas work the ground as it moves, or its thresher threshes.</summary>
    private static bool Working(Machine m, bool moving) =>
        m.Get<Thresher>() is { On: true } || moving && m.Get<WorkAreas>() is { } w && w.Def.Areas.Any(w.Working);

    protected override WearableSave Capture(ContentDatabase content) => new() { Condition = _condition, Paint = _paint };

    protected override void Restore(WearableSave save, SaveContext context)
    {
        _condition = Math.Clamp(save.Condition, 0.0, 1.0);
        _paint = Math.Clamp(save.Paint, 0.0, 1.0);
    }
}

/// <summary>Wear extends to what a machine does: how its engine pulls and its implements work.</summary>
public static class WearEffects
{
    /// <summary>The share of its engine's power <paramref name="m"/> has (1 without wear).</summary>
    public static float PowerFactor(this Machine m) => m.Get<Wearable>()?.PowerFactor ?? 1f;

    /// <summary>The share of its work areas' speed <paramref name="m"/> works at (1 without wear).</summary>
    public static float SpeedFactor(this Machine m) => m.Get<Wearable>()?.SpeedFactor ?? 1f;

    /// <summary>What <paramref name="m"/> burns and spreads, relative to a new one (1 without wear).</summary>
    public static float UsageFactor(this Machine m) => m.Get<Wearable>()?.UsageFactor ?? 1f;
}
