using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Hitches to a joint of its type: mounted (carried rigidly, lifted by the linkage) or trailed (pulled by its drawbar
/// eye at x, z, following the hitch). A lowerable one is lowered and raised with the lower key; its work areas only
/// work lowered.
/// </summary>
public sealed class AttachableDef : ComponentDef
{
    public string Type { get; set; } = "threePoint";
    /// <summary>"mounted" or "trailed".</summary>
    public string Mode { get; set; } = "mounted";
    public float X { get; set; }
    public float Z { get; set; }
    public float MaxArticulationDeg { get; set; } = 80f;
    public bool Lowerable { get; set; }
    /// <summary>Mounted: how high the linkage lifts it off the ground when raised.</summary>
    public float Lift { get; set; } = 0.45f;

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (!AttacherJointDef.Types.Contains(Type)) yield return $"unknown type '{Type}'";
        if (Mode is not ("mounted" or "trailed")) yield return "mode must be mounted or trailed";
        if (Mode == "trailed" && Z <= 0.1f) yield return "a trailed one needs z > 0 (drawbar length)";
        if (Lift < 0f) yield return "lift must be >= 0";
    }

    internal override MachineComponent Create(Machine machine) => new Attachable(machine, this);
}

public sealed class AttachableSave
{
    public bool Lowered { get; set; }
    public float LowerAnim { get; set; }
}

public sealed class Attachable(Machine machine, AttachableDef def) : MachineComponent<AttachableDef, AttachableSave>(machine, def)
{
    /// <summary>Lowered and raised in about 0.6 s: the time a helper lowers ahead of the field.</summary>
    private const float LowerRate = 1.5f;

    public bool Lowered { get; set; }
    /// <summary>0 raised … 1 lowered, smoothed.</summary>
    public float LowerAnim { get; set; }

    internal override void Update(Simulation sim, float dt)
    {
        // A folded implement unfolds before it goes down.
        var down = Lowered && Machine.Get<AnimatedParts>() is not { Unfolded: false };
        LowerAnim = MathUtil.MoveToward(LowerAnim, down ? 1f : 0f, dt * LowerRate);
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
