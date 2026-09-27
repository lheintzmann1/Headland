using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>Placeholder consoles on both sides, where a front loader arm pivots.</summary>
public partial class FrontLoaderBracketView : ComponentView
{
    public FrontLoaderBracket Bracket { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var d = Bracket.Def;
        foreach (var side in new[] { 0.5f, -0.5f })
            PlaceholderBuilder.Box(Rig.Root, new Vector3(0.12f, 0.6f, 0.7f), new Vector3(d.X + side * d.Width, d.Y - 0.1f, d.Z), Body * 0.7f);
    }
}
