using Headland.Game.Common;
using Headland.Core;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// The farmer, drawn by the player model of game.json (docs/MODELING.md): turned the way they walk, legs (legL, legR)
/// swinging with their pace; plus a ground ring so they read when zoomed out.
/// </summary>
public partial class PlayerView : Node3D
{
    private readonly Node3D _body = new() { Name = "Body" };
    private RigPart? _legL;
    private RigPart? _legR;
    private MeshInstance3D _ring = null!;
    private float _phase;

    public Simulation Sim { get; init; } = null!;

    public override void _Ready()
    {
        AddChild(_body);
        if (Models.Load(Sim.Content.Game.Player, "player") is { } model)
        {
            _body.AddChild(model);
            _legL = Leg(model, "legL");
            _legR = Leg(model, "legR");
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
        if (_legL is { } l) l.Node.Rotation = l.Rotation + new Vector3(swing, 0f, 0f);
        if (_legR is { } r) r.Node.Rotation = r.Rotation + new Vector3(-swing, 0f, 0f);
    }

    /// <summary>A leg of the model, hinged at the hip, with its rest pose.</summary>
    private static RigPart? Leg(Node model, string name) =>
        model.FindChild(name, recursive: true, owned: false) is Node3D n ? new RigPart(n, n.Position, n.Rotation, n.Scale) : null;
}
