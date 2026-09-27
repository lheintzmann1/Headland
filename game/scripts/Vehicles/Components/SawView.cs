using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>The saw blade (saw), spinning about its own Y axis while turned on.</summary>
public partial class SawView : ComponentView
{
    private float _angle;

    public Saw Saw { get; init; } = null!;

    public override void _Process(double delta)
    {
        if (!Saw.On) return;
        _angle = (_angle + (float)delta * 12f) % Mathf.Tau;
        Turn("saw", new Vector3(0f, _angle, 0f));
    }
}
