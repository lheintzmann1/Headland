using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The unloading pipe (pipe): rests folded backward and swings 90° out to the left; grain pours while it unloads.</summary>
public partial class PipeView : ComponentView
{
    private Node3D? _stream;

    public Pipe Pipe { get; init; } = null!;

    public override void _Ready()
    {
        if (!Rig.IsPlaceholder) return;
        var s = Machine.Def.Size;
        var pivotX = s.Width * 0.36f;
        var pivot = new Node3D { Name = "Pipe", Position = new Vector3(pivotX, s.Height, Pipe.Def.Z), Rotation = new Vector3(0f, Mathf.Pi, 0f) };
        Rig.Root.AddChild(pivot);
        var reach = Pipe.Def.X - pivotX;
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = reach, RadialSegments = 10 },
            Position = new Vector3(0, 0.25f, reach * 0.5f),
            Rotation = new Vector3(Mathf.Pi / 2f, 0, 0),
            MaterialOverride = Materials.Get(Body, 0.6f, 0.2f),
        });
        PlaceholderBuilder.Box(pivot, new Vector3(0.35f, 0.55f, 0.35f), new Vector3(0, 0.05f, reach), Body * 0.8f);
        _stream = new Node3D { Name = "Stream", Position = new Vector3(0, -1.8f, reach), Visible = false };
        PlaceholderBuilder.Box(_stream, new Vector3(0.22f, 3.3f, 0.22f), Vector3.Zero, new Color(0.8f, 0.68f, 0.4f));
        pivot.AddChild(_stream);
        Rig.Add("pipe", pivot);
    }

    public override void _Process(double delta)
    {
        Turn("pipe", new Vector3(0f, -Mathf.Pi / 2f * Ease(Pipe.Anim), 0f));
        if (_stream != null) _stream.Visible = Pipe.Flowing;
    }
}
