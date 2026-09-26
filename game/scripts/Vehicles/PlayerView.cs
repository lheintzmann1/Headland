using Headland.Game.Common;
using Headland.Core;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>Placeholder farmer: body, head, cap, swinging legs; plus a ground ring so it reads when zoomed out.</summary>
public partial class PlayerView : Node3D
{
    private readonly Node3D _body = new() { Name = "Body" };
    private readonly Node3D _legL = new();
    private readonly Node3D _legR = new();
    private MeshInstance3D _ring = null!;
    private float _phase;

    public Simulation Sim { get; init; } = null!;

    public override void _Ready()
    {
        AddChild(_body);
        var jacket = new Color(0.33f, 0.36f, 0.3f);
        var jeans = new Color(0.22f, 0.26f, 0.33f);
        var skin = new Color(0.8f, 0.65f, 0.52f);
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.22f, Height = 0.8f },
            Position = new Vector3(0, 1.22f, 0),
            MaterialOverride = Materials.Get(jacket),
        });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.26f },
            Position = new Vector3(0, 1.73f, 0),
            MaterialOverride = Materials.Get(skin),
        });
        _body.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.13f, Height = 0.08f },
            Position = new Vector3(0, 1.84f, 0.02f),
            MaterialOverride = Materials.Get(new Color(0.55f, 0.2f, 0.15f)),
        });
        foreach (var (leg, x) in new[] { (_legL, 0.1f), (_legR, -0.1f) })
        {
            leg.Position = new Vector3(x, 0.85f, 0);
            leg.AddChild(new MeshInstance3D
            {
                Mesh = new CapsuleMesh { Radius = 0.08f, Height = 0.85f },
                Position = new Vector3(0, -0.42f, 0),
                MaterialOverride = Materials.Get(jeans),
            });
            _body.AddChild(leg);
        }
        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.6f, Rings = 24, RingSegments = 4 },
            Scale = new Vector3(1f, 0.1f, 1f),
            Position = new Vector3(0, 0.05f, 0),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.85f, 0.45f, 0.8f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ring);
    }

    public override void _Process(double delta)
    {
        var p = Sim.Player;
        var inVehicle = p.Vehicle != null;
        _body.Visible = !inVehicle;
        _ring.Visible = !inVehicle;
        if (inVehicle) return;

        GlobalPosition = Sim.World.OnGround(p.Position);
        _body.Rotation = new Vector3(0f, p.Heading, 0f);
        var speed = p.Velocity.Length();
        _phase += (float)delta * speed * 3.2f;
        var swing = Mathf.Sin(_phase) * Mathf.Clamp(speed / 3f, 0f, 0.7f);
        _legL.Rotation = new Vector3(swing, 0, 0);
        _legR.Rotation = new Vector3(-swing, 0, 0);
    }
}
