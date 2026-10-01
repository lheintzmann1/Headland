using Headland.Game.Common;
using Headland.Core.Machines.Components;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.Vehicles.Components;

/// <summary>A bucket (shovel): what it pours out falls from its edge straight down, to the ground or what's under it.</summary>
public partial class ShovelView : MachineComponentView
{
    private MeshInstance3D _stream = null!;
    private string? _color;

    public Shovel Shovel { get; init; } = null!;

    public override void _Ready()
    {
        _stream = new MeshInstance3D
        {
            Name = "Stream",
            Mesh = new BoxMesh { Size = new Vector3(Shovel.Def.Edge.Width * 0.7f, 1f, 0.18f) },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_stream);
    }

    public override void _Process(double delta)
    {
        _stream.Visible = Shovel.Dumping;
        if (!Shovel.Dumping) return;
        if (Shovel.Bucket.FillType is { } ft && ft != _color && Sim.Content.FillTypes.TryGetValue(ft, out var def))
        {
            _color = ft;
            _stream.MaterialOverride = Materials.Get(Color.FromHtml(def.Color), 0.9f);
        }
        // From its edge down to whatever is under it (a trailer's load, the ground), upright whatever the bucket's tilt.
        var edge = GlobalTransform * new Vector3(0f, Shovel.Def.Edge.Y, Shovel.Def.Edge.Z);
        var below = Sim.World.SurfaceAt(new NVec2(edge.X, edge.Z));
        var fall = Mathf.Max(0.1f, edge.Y - below);
        _stream.GlobalTransform = new Transform3D(new Basis(Vector3.Up, GlobalRotation.Y).Scaled(new Vector3(1f, fall, 1f)),
            edge - new Vector3(0f, fall * 0.5f, 0f));
    }
}
