using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The rope, hanging straight down from where it leaves the machine or crane, and the hook (hook) at its end.</summary>
public partial class WinchView : MachineComponentView
{
    private MeshInstance3D _rope = null!;
    private Node3D? _hook;

    public Winch Winch { get; init; } = null!;

    public override void _Ready()
    {
        _rope = new MeshInstance3D
        {
            Name = "Rope",
            Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 1f, RadialSegments = 6 },
            MaterialOverride = Materials.Get(Materials.DarkSteel, 0.9f),
            TopLevel = true,
        };
        AddChild(_rope);
        if (Rig.Part("hook") is { } part) _hook = part.Node;
    }

    public override void _Process(double delta)
    {
        var anchor = GetParent<Node3D>().ToGlobal(Winch.Anchor.ToGodot());
        var length = Winch.Length;
        _rope.GlobalTransform = new Transform3D(Basis.FromScale(new Vector3(1f, length, 1f)), anchor - new Vector3(0f, length * 0.5f, 0f));
        if (_hook != null) _hook.GlobalPosition = anchor - new Vector3(0f, length, 0f);
    }
}
