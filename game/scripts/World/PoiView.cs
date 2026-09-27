using Headland.Game.Components;
using Headland.Core;
using Headland.Core.Machines;
using Headland.Core.Pois;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.World;

/// <summary>
/// A POI in its local space (+Z its front), drawn by its model (nothing if that doesn't load) on the lowest ground of
/// its footprint, so slopes never show a gap under the walls; a view per component moves its parts and lights its lamps.
/// </summary>
public partial class PoiView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public Poi Poi { get; init; } = null!;

    public override void _Ready()
    {
        Name = Poi.Id;
        Position = new Vector3(Poi.Position.X, LowestGround(Poi.Footprint), Poi.Position.Y);
        Rotation = new Vector3(0f, Poi.Heading, 0f);
        var rig = Rig.Model(Poi.Def);
        AddChild(rig.Root);
        foreach (var c in Poi.Components)
            if (ComponentView.For(c, Sim, rig) is { } view)
                AddChild(view);
    }

    /// <summary>Height of the lowest corner of a box.</summary>
    private float LowestGround(Obb box)
    {
        Span<NVec2> corners = stackalloc NVec2[4];
        MathUtil.RectCorners(box.Center, box.Heading, box.HalfExtents.X, box.HalfExtents.Y, corners);
        var y = float.MaxValue;
        foreach (var c in corners) y = Mathf.Min(y, Sim.World.HeightAt(c));
        return y;
    }
}
