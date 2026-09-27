using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The unloading pipe (pipe): rests folded backward and swings 90° out to the left; grain pours while it unloads.</summary>
public partial class PipeView : MachineComponentView
{
    private MeshInstance3D _stream = null!;

    public Pipe Pipe { get; init; } = null!;

    public override void _Ready()
    {
        var s = Machine.Def.Size;
        // The grain falls where the pipe unloads, from about the machine's height.
        _stream = new MeshInstance3D
        {
            Name = "Stream",
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 3.3f, 0.22f) },
            Position = new Vector3(Pipe.Def.X, s.Height - 1.8f, Pipe.Def.Z),
            MaterialOverride = Materials.Get(new Color(0.8f, 0.68f, 0.4f), 0.75f),
            Visible = false,
        };
        AddChild(_stream);
    }

    public override void _Process(double delta)
    {
        Turn("pipe", new Vector3(0f, -Mathf.Pi / 2f * Ease(Pipe.Anim), 0f));
        _stream.Visible = Pipe.Flowing;
    }
}
