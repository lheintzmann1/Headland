using Headland.Game.Common;
using Headland.Game.Vehicles.Components;
using Headland.Core;
using Headland.Core.Machines;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// Follows a Core machine on the terrain (with pitch and roll) and draws it: the procedural placeholder, or a glTF
/// model (e.g. from Blockbench). A view per component moves its parts: wheels, pipe, tipper, lights…
/// </summary>
public partial class MachineView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public Machine Machine { get; init; } = null!;

    public override void _Ready()
    {
        Name = $"{Machine.Def.Id}_{Machine.Id}";
        var rig = string.IsNullOrEmpty(Machine.Def.Visual.Model) ? PlaceholderBuilder.Build(Machine.Def) : MachineRig.Model(Machine.Def);
        AddChild(rig.Root);
        foreach (var c in Machine.Components)
            if (ComponentView.For(c, Sim, rig) is { } view)
                AddChild(view);
    }

    public override void _Process(double delta)
    {
        var m = Machine;
        var world = Sim.World;
        var s = m.Def.Size;

        // Terrain tilt from four samples around the footprint.
        var halfL = s.Length * 0.4f;
        var halfW = s.Width * 0.4f;
        var hf = world.HeightAt(m.LocalToWorld(0f, s.CenterZ + halfL));
        var hb = world.HeightAt(m.LocalToWorld(0f, s.CenterZ - halfL));
        var hl = world.HeightAt(m.LocalToWorld(halfW, s.CenterZ));
        var hr = world.HeightAt(m.LocalToWorld(-halfW, s.CenterZ));
        var pitch = Mathf.Atan2(hb - hf, halfL * 2f);
        var roll = Mathf.Atan2(hl - hr, halfW * 2f);
        var basis = new Basis(Vector3.Up, m.Heading) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);
        GlobalTransform = new Transform3D(basis, world.OnGround(m.Position));
    }
}
