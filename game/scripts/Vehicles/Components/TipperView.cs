using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// The tipping bed (tipper), hinged at its rear: its front lifts by the tipper's angle, or it tilts as the side it tips
/// to says, about that side's pivot.
/// </summary>
public partial class TipperView : MachineComponentView
{
    public Tipper Tipper { get; init; } = null!;

    public override void _Process(double delta)
    {
        if (Rig.Part("tipper") is not { } bed) return;
        var side = Tipper.Side;
        var tipped = side?.RotationDeg is { } r ? Vec(r) : new Vector3(-Tipper.Def.AngleDeg, 0f, 0f);
        var turn = Basis.FromEuler(tipped * (Mathf.DegToRad(1f) * Ease(Tipper.Anim)));
        var rest = Basis.FromEuler(bed.Rotation);
        var pivot = side != null ? Vec(side.Pivot) / Rig.Scale : Vector3.Zero;
        // Turned about the side's pivot: what the turn moves the pivot by is taken back.
        bed.Node.Basis = rest * turn;
        bed.Node.Scale = bed.Scale;
        bed.Node.Position = bed.Position + rest * (pivot - turn * pivot);
    }
}
