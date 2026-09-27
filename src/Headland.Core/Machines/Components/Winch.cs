using System.Numerics;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

/// <summary>
/// A rope winch with a hook: the rope pays out from <see cref="Offset"/>, on the machine or at the end of a crane
/// joint (<see cref="Joint"/>), and the hook hangs below it.
/// </summary>
public sealed class WinchDef : ComponentDef
{
    /// <summary>Crane joint the rope leaves from (its frame); none: the machine.</summary>
    public string? Joint { get; set; }
    /// <summary>Where the rope leaves [x, y, z], in the joint's space or the machine's.</summary>
    public float[] Offset { get; set; } = [0f, 1f, 0f];
    public float MinLength { get; set; } = 0.5f;
    public float MaxLength { get; set; } = 10f;
    /// <summary>Rope reeled in or out per second (m).</summary>
    public float Speed { get; set; } = 1f;

    public override IEnumerable<string> Roles => ["hook"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (MinLength < 0f || MaxLength <= MinLength || Speed <= 0f) yield return "needs 0 <= minLength < maxLength and speed > 0";
        if (Offset.Length != 3) yield return "offset is [x, y, z]";
        if (Joint != null && machine.Get<CraneArmDef>()?.Joints.Any(j => j.Id == Joint) != true) yield return $"crane joint '{Joint}' missing";
    }

    internal override MachineComponent Create(Machine machine) => new Winch(machine, this);
}

public sealed class WinchSave
{
    public float Length { get; set; }
    public float Target { get; set; }
}

public sealed class Winch(Machine machine, WinchDef def) : MachineComponent<WinchDef, WinchSave>(machine, def)
{
    /// <summary>Rope paid out, and the length it's heading for.</summary>
    public float Length { get; private set; } = def.MinLength;
    public float Target { get; private set; } = def.MinLength;

    /// <summary>Reels the rope out to <paramref name="length"/> (or in), within its limits.</summary>
    public void ReelTo(float length) => Target = Math.Clamp(length, Def.MinLength, Def.MaxLength);

    /// <summary>Where the rope leaves, in the machine's space (x left, y up, z forward).</summary>
    public Vector3 Anchor
    {
        get
        {
            var o = Def.Offset.Length == 3 ? new Vector3(Def.Offset[0], Def.Offset[1], Def.Offset[2]) : Vector3.Zero;
            return Machine.Get<CraneArm>() is { } crane ? crane.ToMachine(Def.Joint, o) : o;
        }
    }

    /// <summary>Where the hook hangs, in the machine's space.</summary>
    public Vector3 Hook => Anchor - Vector3.UnitY * Length;

    internal override void Update(Simulation sim, float dt) => Length = MathUtil.MoveToward(Length, Target, Def.Speed * dt);

    protected override WinchSave Capture(ContentDatabase content) => new() { Length = Length, Target = Target };

    protected override void Restore(WinchSave save, SaveContext context)
    {
        Length = Math.Clamp(save.Length, Def.MinLength, Def.MaxLength);
        Target = Math.Clamp(save.Target, Def.MinLength, Def.MaxLength);
    }
}
