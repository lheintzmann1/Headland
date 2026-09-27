using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The tipping bed (tipper), hinged at its rear: its front lifts by the tipper's angle.</summary>
public partial class TipperView : ComponentView
{
    public Tipper Tipper { get; init; } = null!;

    public override void _Process(double delta) =>
        Turn("tipper", new Vector3(-Mathf.DegToRad(Tipper.Def.AngleDeg) * Ease(Tipper.Anim), 0f, 0f));
}
