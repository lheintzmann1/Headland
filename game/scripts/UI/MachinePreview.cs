using Headland.Game.Components;
using Headland.Core.Content;
using Godot;

namespace Headland.Game.UI;

/// <summary>
/// The shop's preview: a machine turning slowly on a turntable, in a world of its own, with the options of
/// <see cref="Def"/> (their parts and the paint) and built again as they change.
/// </summary>
public partial class MachinePreview : SubViewportContainer
{
    private const float Fov = 28f;
    /// <summary>Radians a second the turntable turns.</summary>
    private const float Spin = 0.35f;

    private Node3D _turntable = null!;
    private Camera3D _camera = null!;
    private CylinderMesh _ground = null!;
    private Node3D? _model;
    private MachineDef? _def;

    public MachineDef? Def
    {
        get => _def;
        set
        {
            if (value == _def) return;
            _def = value;
            if (IsNodeReady()) Rebuild();
        }
    }

    public override void _Ready()
    {
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        var viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.66f, 0.68f, 0.64f),
                AmbientLightEnergy = 0.75f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55f, -35f, 0f), LightEnergy = 1.1f, ShadowEnabled = true });
        _turntable = new Node3D { RotationDegrees = new Vector3(0f, 35f, 0f) };
        viewport.AddChild(_turntable);
        _ground = new CylinderMesh { Height = 0.04f, RadialSegments = 48 };
        _turntable.AddChild(new MeshInstance3D
        {
            Mesh = _ground,
            Position = new Vector3(0f, -0.02f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.23f, 0.21f), Roughness = 1f },
        });
        _camera = new Camera3D { Fov = Fov };
        viewport.AddChild(_camera);
        Rebuild();
    }

    public override void _Process(double delta) => _turntable.RotateY((float)delta * Spin);

    /// <summary>Puts the machine's model on the turntable, its footprint's center on the axis, and frames it.</summary>
    private void Rebuild()
    {
        _model?.QueueFree();
        _model = null;
        if (_def == null) return;
        var s = _def.Size;
        _model = Rig.Model(_def).Root;
        _model.Position = new Vector3(0f, 0f, -s.CenterZ);
        _turntable.AddChild(_model);
        _ground.TopRadius = _ground.BottomRadius = 0.5f * new Vector2(s.Length, s.Width).Length() + 0.5f;
        // Far enough for the whole box to stay in view as it turns.
        var reach = 0.5f * new Vector3(s.Length, s.Height, s.Width).Length();
        var target = new Vector3(0f, s.Height * 0.35f, 0f);
        _camera.LookAtFromPosition(target + new Vector3(0f, 0.42f, 1f).Normalized() * (reach / Mathf.Sin(Mathf.DegToRad(Fov * 0.5f))), target);
    }
}
