using Headland.Core.Components;
using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>
/// A saw blade at <see cref="Offset"/>, on the machine or on a crane joint (a felling head). Turned on, it spins; it cuts
/// trunks up to <see cref="MaxCut"/> thick once trees are more than props.
/// </summary>
public sealed class SawDef : MachineComponentDef
{
    /// <summary>Crane joint carrying it (its frame); none: the machine.</summary>
    public string? Joint { get; set; }
    /// <summary>The blade's center [x, y, z], in the joint's space or the machine's.</summary>
    public float[] Offset { get; set; } = [0f, 0.5f, 0f];
    public float Diameter { get; set; } = 0.75f;
    /// <summary>The thickest trunk it cuts (m).</summary>
    public float MaxCut { get; set; } = 0.6f;

    public override IEnumerable<string> Roles => ["saw"];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Diameter <= 0f || MaxCut <= 0f) yield return "diameter and maxCut must be > 0";
        if (Offset.Length != 3) yield return "offset is [x, y, z]";
        if (Joint != null && machine.Get<CraneArmDef>()?.Joints.Any(j => j.Id == Joint) != true) yield return $"crane joint '{Joint}' missing";
    }

    internal override Component Create(Machine machine) => new Saw(machine, this);
}

public sealed class SawSave
{
    public bool On { get; set; }
}

public sealed class Saw(Machine machine, SawDef def) : MachineComponent<SawDef, SawSave>(machine, def), ISwitchable, IActionSource
{
    public bool CanTurnOn => true;
    public bool On { get; set; }

    public void AddActions(ActionList actions, Simulation sim) => actions.AddSwitch(this);

    /// <summary>The blade's center, in the machine's space (x left, y up, z forward).</summary>
    public Vector3 Blade
    {
        get
        {
            var o = Def.Offset.Length == 3 ? new Vector3(Def.Offset[0], Def.Offset[1], Def.Offset[2]) : Vector3.Zero;
            return Machine.Get<CraneArm>() is { } crane ? crane.ToMachine(Def.Joint, o) : o;
        }
    }

    internal override void OnDetached() => On = false;

    protected override SawSave Capture(ContentDatabase content) => new() { On = On };

    protected override void Restore(SawSave save, SaveContext context) => On = save.On;
}
