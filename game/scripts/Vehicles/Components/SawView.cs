using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The saw blade (saw), spinning about its own Y axis while turned on. The placeholder hangs it on its crane joint.</summary>
public partial class SawView : ComponentView
{
    private float _angle;

    public Saw Saw { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var parent = Saw.Def.Joint != null && Rig.Part(Saw.Def.Joint) is { } joint ? joint.Node : Rig.Root;
        var radius = Saw.Def.Diameter * 0.5f;
        var blade = new Node3D { Name = "Saw", Position = Vec(Saw.Def.Offset) };
        blade.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.03f, RadialSegments = 20 },
            MaterialOverride = Materials.Get(Materials.Steel, 0.4f, 0.6f),
        });
        // A tooth shows it turn.
        PlaceholderBuilder.Box(blade, new Vector3(0.06f, 0.04f, 0.06f), new Vector3(0f, 0f, radius), Materials.DarkSteel);
        parent.AddChild(blade);
        Rig.Add("saw", blade);
    }

    public override void _Process(double delta)
    {
        if (!Saw.On) return;
        _angle = (_angle + (float)delta * 12f) % Mathf.Tau;
        Turn("saw", new Vector3(0f, _angle, 0f));
    }
}
