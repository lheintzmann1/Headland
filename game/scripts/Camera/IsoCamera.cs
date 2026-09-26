using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.Camera;

/// <summary>
/// Orthographic camera at the classic 2:1 angle (30° pitch, 45° yaw), rotating in 90° steps.
/// Wheel zooms, middle-drag pans (the offset eases back once the followed entity moves).
/// </summary>
public partial class IsoCamera : Camera3D
{
    public const float PitchDeg = 30f;
    private const float Distance = 180f;
    private const float MinSize = 14f;
    private const float MaxSize = 170f;

    private int _yawIndex;
    private float _yaw = Mathf.DegToRad(45f);
    private float _sizeTarget = 42f;
    private Vector3 _target;
    private Vector3 _pan;
    private bool _dragging;
    private bool _snapped;

    /// <summary>World point to follow (set every frame by the game).</summary>
    public Vector3 Follow { get; set; }
    /// <summary>Speed of the followed entity, used to recenter after panning.</summary>
    public float FollowSpeed { get; set; }

    public float Zoom
    {
        get => _sizeTarget;
        set => _sizeTarget = Mathf.Clamp(value, MinSize, MaxSize);
    }

    private float TargetYaw => Mathf.DegToRad(45f + 90f * _yawIndex);

    /// <summary>Screen-up direction on the ground plane (Core coordinates).</summary>
    public NVec2 GroundForward => new(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
    /// <summary>Screen-right direction on the ground plane (Core coordinates).</summary>
    public NVec2 GroundRight => new(Mathf.Cos(_yaw), -Mathf.Sin(_yaw));

    public override void _Ready()
    {
        Projection = ProjectionType.Orthogonal;
        Size = _sizeTarget;
        Near = 1f;
        Far = 520f;
        Current = true;
    }

    public void RotateStep(int dir) => _yawIndex += dir;

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Zoom /= 1.12f;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Zoom *= 1.12f;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                var perPixel = Size / GetViewport().GetVisibleRect().Size.Y;
                var right = new Vector3(GroundRight.X, 0, GroundRight.Y);
                var fwd = new Vector3(GroundForward.X, 0, GroundForward.Y);
                // Vertical screen motion covers more ground because the view is tilted.
                _pan -= right * motion.Relative.X * perPixel;
                _pan += fwd * motion.Relative.Y * perPixel / Mathf.Sin(Mathf.DegToRad(PitchDeg));
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var k = 1f - Mathf.Exp(-10f * dt);
        _yaw = Mathf.LerpAngle(_yaw, TargetYaw, 1f - Mathf.Exp(-8f * dt));
        Size = Mathf.Lerp(Size, _sizeTarget, k);
        if (FollowSpeed > 1f && !_dragging) _pan = _pan.Lerp(Vector3.Zero, 1f - Mathf.Exp(-1.5f * dt));

        var goal = Follow + _pan;
        _target = _snapped ? _target.Lerp(goal, k) : goal;
        _snapped = true;

        var pitch = Mathf.DegToRad(PitchDeg);
        var dir = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(_yaw) * Mathf.Cos(pitch));
        GlobalPosition = _target + dir * Distance;
        LookAt(_target, Vector3.Up);
    }

    public void SnapTo(Vector3 target)
    {
        Follow = target;
        _target = target;
        _pan = Vector3.Zero;
    }
}
