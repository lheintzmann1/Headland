using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// Dirt over the machine (FS: washable): an overlay on its meshes but the load (shaders/dirt.gdshader), as dirty as
/// Core says, thickest near the ground.
/// </summary>
public partial class WashableView : MachineComponentView
{
    private static Shader? _shader;
    private ShaderMaterial _overlay = null!;
    private float _shown = -1f;

    public Washable Washable { get; init; } = null!;

    public override void _Ready()
    {
        _shader ??= GD.Load<Shader>("res://shaders/dirt.gdshader");
        _overlay = new ShaderMaterial { Shader = _shader };
        foreach (var mesh in Rig.Body) mesh.MaterialOverlay = _overlay;
    }

    public override void _Process(double delta)
    {
        _overlay.SetShaderParameter("ground", GlobalPosition.Y);
        if (MathF.Abs(Washable.Dirt - _shown) < 0.005f) return;
        _shown = Washable.Dirt;
        _overlay.SetShaderParameter("dirt", _shown);
    }
}
