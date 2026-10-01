using Headland.Game.Common;
using Headland.Game.Components;
using Headland.Core.Objects.Components;

namespace Headland.Game.Objects.Components;

/// <summary>A bale: its model's fill materials take the color of what it's made of (grass, hay, straw).</summary>
public partial class BaleView : ComponentView
{
    private string? _color;

    public Bale Bale { get; init; } = null!;

    public override void _Process(double delta)
    {
        if (Bale.Content.FillType is not { } ft || ft == _color || !Sim.Content.FillTypes.TryGetValue(ft, out var def)) return;
        _color = ft;
        Rig.SetFillColor(Conv.Hex(def.Color));
    }
}
