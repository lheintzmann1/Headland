using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Hitches to a joint of its type: mounted (carried rigidly, lifted by the linkage) or trailed (pulled by its drawbar
/// eye or kingpin at x, z, following the hitch, with a share of its weight on it). A lowerable one is lowered and
/// raised with the lower key; its work areas only work lowered.
/// </summary>
public sealed class AttachableDef : MachineComponentDef, ISpecSource
{
    /// <summary>The joint type (jointtypes.json) it hitches to.</summary>
    public string Type { get; set; } = "threePoint";
    /// <summary>"mounted" or "trailed".</summary>
    public string Mode { get; set; } = "mounted";
    public float X { get; set; }
    public float Z { get; set; }
    public float MaxArticulationDeg { get; set; } = 80f;
    /// <summary>Lowered and raised with the lower key.</summary>
    public bool Lowerable { get; set; }
    /// <summary>Mounted: how high the linkage lifts it off the ground when raised.</summary>
    public float Lift { get; set; } = 0.45f;
    /// <summary>Trailed: the share of its weight (with its load) resting on the hitch, a semi-trailer's on the fifth wheel.</summary>
    public float HitchLoad { get; set; }
    /// <summary>On a joint that uses them, it switches the vehicle to its top lights (FS: useTopLights).</summary>
    public bool UseTopLights { get; set; } = true;

    /// <summary>What it hitches to, and how: "Drawbar, trailed".</summary>
    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content) =>
        [new("Hitch", $"{content.JointTypes.GetValueOrDefault(Type)?.Name ?? Type}, {Mode}")];

    public override IEnumerable<string> Toggles => Lowerable ? [InputActions.Lower] : [];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (AttacherJointDef.TypeError(Type, content) is { } error) yield return error;
        if (Mode is not ("mounted" or "trailed")) yield return "mode must be mounted or trailed";
        if (Mode == "trailed" && Z <= 0.1f) yield return "a trailed one needs z > 0 (drawbar length)";
        if (Mode == "trailed" && machine.Get<RunningGearDef>() == null) yield return "a trailed one needs a runningGear";
        if (Lift < 0f) yield return "lift must be >= 0";
        if (HitchLoad is < 0f or >= 1f) yield return "hitchLoad must be in [0, 1)";
        else if (HitchLoad > 0f && Mode != "trailed") yield return "hitchLoad is for trailed machines: a mounted one is carried whole";
    }

    internal override Component Create(Machine machine) => new Attachable(machine, this);
}

public sealed class AttachableSave
{
    public bool Lowered { get; set; }
    public float LowerAnim { get; set; }
}

public sealed class Attachable(Machine machine, AttachableDef def) : MachineComponent<AttachableDef, AttachableSave>(machine, def), IActionSource, IReadoutSource
{
    /// <summary>Lowered and raised in about 0.6 s: the time a helper lowers ahead of the field.</summary>
    private const float LowerRate = 1.5f;

    public bool Lowered { get; set; }
    /// <summary>0 raised … 1 lowered, smoothed.</summary>
    public float LowerAnim { get; set; }

    /// <summary>How far down (<see cref="LowerAnim"/>) it works: most of the way.</summary>
    internal const float WorkingDepth = 0.9f;

    /// <summary>
    /// How high it's lifted now: mounted on a three-point linkage, or lowerable (a header), by its <c>lift</c> while
    /// raised; on a joint that doesn't lift (a loader's consoles, its tool carrier), not at all.
    /// </summary>
    public float Lift => Def.Mode == "mounted" && Machine.ParentJointDef is { } j && j.Crane == null && (Def.Lowerable || j.TypeDef?.Linkage == true)
        ? (1f - LowerAnim) * Def.Lift
        : 0f;

    /// <summary>The seconds it takes to go down to work once lowered: the linkage's, or its parts' going down.</summary>
    public float LowerSeconds => MathF.Max(WorkingDepth / LowerRate, Machine.Def.Get<AnimatedPartsDef>()?.LowerSeconds ?? 0f);

    internal override void Update(Simulation sim, float dt)
    {
        // A folded implement unfolds before it goes down.
        var down = Lowered && Machine.Get<AnimatedParts>() is not { Unfolded: false };
        LowerAnim = MathUtil.MoveToward(LowerAnim, down ? 1f : 0f, dt * LowerRate);
    }

    /// <summary>
    /// The lower key, once hitched: "Lower cultivator", "Lift cultivator", or its own words. A folded implement stays up:
    /// it's unfolded first, with the fold key.
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (!Def.Lowerable || Machine.Parent == null) return;
        var (lowerIt, liftIt) = Def.WordsFor(InputActions.Lower, Machine.Def.Named("Lower"), Machine.Def.Named("Lift"));
        actions.Toggle(InputActions.Lower, Lowered, lowerIt, liftIt, lower =>
        {
            if (lower && Machine.Get<AnimatedParts>() is { Folded: true }) return $"Unfold the {Machine.Def.Name} first";
            Lowered = lower;
            return null;
        });
    }

    /// <summary>Lowered or raised, once hitched.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (Def.Lowerable && Machine.Parent != null) yield return Lowered ? new Status("Lowered", Tone.Good) : new Status("Raised", Tone.Dim);
    }

    internal override void OnHitched()
    {
        Lowered = false;
        LowerAnim = 0f;
    }

    internal override void OnDetached() => Lowered = false;

    protected override AttachableSave Capture(ContentDatabase content) => new() { Lowered = Lowered, LowerAnim = LowerAnim };

    protected override void Restore(AttachableSave save, SaveContext context)
    {
        Lowered = save.Lowered && Def.Lowerable;
        LowerAnim = Math.Clamp(save.LowerAnim, 0f, 1f);
    }
}
