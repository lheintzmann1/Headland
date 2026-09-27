using Headland.Game.Common;
using Headland.Game.Weather;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles.Components;

/// <summary>
/// A spot light per lamp, lit while its type is switched on; headlights also after dark. Beacons turn, and lamps on an
/// articulated machine's front frame swing with it.
/// </summary>
public partial class LightsView : ComponentView
{
    private readonly List<(LampDef lamp, SpotLight3D light)> _lamps = [];
    private RunningGear? _gear;
    private float _beacon;

    public Lights Lights { get; init; } = null!;

    public override void _Ready()
    {
        _gear = Machine.Get<RunningGear>();
        foreach (var l in Lights.Def.Lamps)
        {
            var light = new SpotLight3D
            {
                Position = new Vector3(l.X, l.Y, l.Z),
                // Spot lights shine along -Z: turned around to face forward (+Z), then as the lamp points.
                Rotation = new Vector3(Mathf.DegToRad(l.PitchDeg), Mathf.Pi + Mathf.DegToRad(l.YawDeg), 0f),
                SpotRange = l.Range,
                SpotAngle = l.AngleDeg,
                LightEnergy = l.Energy,
                LightColor = Conv.Hex(l.Color),
                ShadowEnabled = false,
                Visible = false,
            };
            AddChild(light);
            _lamps.Add((l, light));
        }
    }

    public override void _Process(double delta)
    {
        _beacon = (_beacon + (float)delta * 9f) % Mathf.Tau;
        var dark = EnvironmentController.CurrentDaylight < 0.35f;
        foreach (var (l, light) in _lamps)
        {
            light.Visible = Lights.IsOn(l.Type) || l.Type == "head" && dark;
            if (!light.Visible) continue;
            var (x, z, swing) = _gear?.Swing(l.X, l.Z) ?? (l.X, l.Z, 0f);
            light.Position = new Vector3(x, l.Y, z);
            var spin = l.Type == "beacon" ? _beacon : 0f;
            light.Rotation = light.Rotation with { Y = Mathf.Pi + Mathf.DegToRad(l.YawDeg) + swing + spin };
        }
    }
}
