using Headland.Game.Common;
using Headland.Game.Components;
using Headland.Core;
using Headland.Core.Objects;
using Godot;

namespace Headland.Game.Objects;

/// <summary>
/// Draws a Core object (a bale) by its glTF model: lying on the ground, or where the machine carrying it holds it, in
/// that machine's space so it rides with the machine's tilt. A view per component colors it (a bale by what it's made of).
/// </summary>
public partial class ObjectView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public WorldObject Object { get; init; } = null!;
    /// <summary>The view of a machine, by its id (the one carrying the object).</summary>
    public Func<int, Node3D?> MachineView { get; init; } = _ => null;

    public override void _Ready()
    {
        Name = $"{Object.Def.Id}_{Object.Id}";
        var rig = Rig.Model(Object.Def);
        AddChild(rig.Root);
        foreach (var c in Object.Components)
            if (ComponentView.For(c, Sim, rig) is { } view)
                AddChild(view);
    }

    public override void _Process(double delta)
    {
        var o = Object;
        if (o.Holder is { } holder && MachineView(holder.Machine.Id) is { } carrier)
        {
            var (at, yaw) = holder.PoseOf(o);
            GlobalTransform = carrier.GlobalTransform * new Transform3D(new Basis(Vector3.Up, yaw), new Vector3(at.X, at.Y, at.Z));
            return;
        }
        var ground = Sim.World.OnGround(o.Position);
        GlobalTransform = new Transform3D(new Basis(Vector3.Up, o.Heading), ground + Vector3.Up * o.Elevation);
    }
}
