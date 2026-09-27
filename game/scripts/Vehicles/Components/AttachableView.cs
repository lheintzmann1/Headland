using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>A mounted implement rides up on the linkage when raised.</summary>
public partial class AttachableView : MachineComponentView
{
    public Attachable Hitch { get; init; } = null!;

    public override void _Process(double delta)
    {
        var lift = Hitch.Def.Mode == "mounted" && Machine.Parent != null ? (1f - Hitch.LowerAnim) * Hitch.Def.Lift : 0f;
        Rig.Root.Position = new Vector3(0f, lift, 0f);
    }
}
