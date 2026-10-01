using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>A mounted implement rides up on the linkage when raised, a header as the feeder house lifts it.</summary>
public partial class AttachableView : MachineComponentView
{
    public Attachable Hitch { get; init; } = null!;

    public override void _Process(double delta) => Rig.Root.Position = new Vector3(0f, Hitch.Lift, 0f);
}
