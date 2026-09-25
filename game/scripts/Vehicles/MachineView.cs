using FarmSim.Game.Common;
using FarmSim.Core;
using FarmSim.Core.Machines;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace FarmSim.Game.Vehicles;

/// <summary>Follows a Core machine: pose on the terrain (with pitch/roll), wheels, lift, pipe, reel, tipper and load.</summary>
public partial class MachineView : Node3D
{
    public Simulation Sim { get; init; } = null!;
    public Machine Machine { get; init; } = null!;
    private MachineRig _rig = null!;
    private readonly List<SpotLight3D> _headlights = [];
    private float _lastTankLevel;
    private float _reelAngle;

    public override void _Ready()
    {
        Name = $"{Machine.Def.Id}_{Machine.Id}";
        _rig = PlaceholderBuilder.Build(Machine.Def);
        AddChild(_rig.Root);
        if (Machine.IsMotorized) AddHeadlights();
        var model = Machine.Def.Visual.Model;
        if (!string.IsNullOrEmpty(model) && ResourceLoader.Exists(model))
        {
            // A real model replaces the placeholder body; placeholder wheels stay unless the model maps its own.
            _rig.Root.Visible = false;
            AddChild(GD.Load<PackedScene>(model).Instantiate());
        }
    }

    public override void _Process(double delta)
    {
        var m = Machine;
        var world = Sim.World;
        var s = m.Def.Size;

        // Terrain tilt from four samples around the footprint.
        var halfL = s.Length * 0.4f;
        var halfW = s.Width * 0.4f;
        var hf = world.HeightAt(m.LocalToWorld(0f, s.CenterZ + halfL));
        var hb = world.HeightAt(m.LocalToWorld(0f, s.CenterZ - halfL));
        var hl = world.HeightAt(m.LocalToWorld(halfW, s.CenterZ));
        var hr = world.HeightAt(m.LocalToWorld(-halfW, s.CenterZ));
        var pitch = Mathf.Atan2(hb - hf, halfL * 2f);
        var roll = Mathf.Atan2(hl - hr, halfW * 2f);
        var basis = new Basis(Vector3.Up, m.Heading) * new Basis(Vector3.Right, pitch) * new Basis(Vector3.Back, roll);

        var lift = 0f;
        if (m.Parent != null && m.Def.Attacher?.Mode == "mounted")
            lift = (1f - m.LowerAnim) * (m.Def.Attacher.Type == "header" ? 0.6f : 0.45f);
        GlobalTransform = new Transform3D(basis, world.OnGround(m.Position, lift));

        var lightsOn = Weather.EnvironmentController.CurrentDaylight < 0.35f;
        foreach (var l in _headlights) l.Visible = lightsOn;

        // Wheels: rolling, and steering on the steered axle (rear-steer combines turn the other way).
        var rearSteer = m.Def.Motorized?.SteerAxle == "rear";
        foreach (var (steer, spin, w) in _rig.Wheels)
        {
            spin.Rotation = new Vector3(m.Distance / w.Radius, 0f, 0f);
            if (w.Steer) steer.Rotation = new Vector3(0f, rearSteer ? -m.SteerAngle : m.SteerAngle, 0f);
        }

        if (_rig.PipePivot != null && m.Def.Pipe is { } pipe)
        {
            _rig.PipePivot.Rotation = new Vector3(0f, Mathf.Lerp(Mathf.Pi, Mathf.Pi / 2f, Ease(m.PipeAnim)), 0f);
            var tank = m.Unit(pipe.FillUnit)!;
            var flowing = m.PipeAnim > 0.95f && tank.Level < _lastTankLevel - 0.01f;
            _lastTankLevel = tank.Level;
            if (_rig.Stream != null) _rig.Stream.Visible = flowing;
        }

        if (_rig.Reel != null)
        {
            var on = m.Parent is { TurnedOn: true };
            _reelAngle += (float)delta * (on ? 3.5f : 0f);
            _rig.Reel.Rotation = new Vector3(_reelAngle, 0f, 0f);
        }

        if (_rig.TipPivot != null) _rig.TipPivot.Rotation = new Vector3(-Ease(m.TipAnim) * Mathf.DegToRad(42f), 0f, 0f);

        if (_rig.Content != null)
        {
            var unit = m.Def.Tipper != null ? m.Unit(m.Def.Tipper.FillUnit) : m.FillUnits.FirstOrDefault();
            if (unit == null || unit.IsEmpty) _rig.Content.Visible = false;
            else
            {
                var h = Mathf.Max(0.05f, unit.Fraction * _rig.ContentHeight);
                _rig.Content.Visible = true;
                _rig.Content.Scale = new Vector3(1f, h, 1f);
                _rig.Content.Position = _rig.Content.Position with { Y = _rig.ContentFloor + h * 0.5f };
                if (unit.FillType != null && Sim.Content.FillTypes.TryGetValue(unit.FillType, out var ft))
                    _rig.Content.MaterialOverride = Materials.Get(Conv.Hex(ft.Color), 0.95f);
            }
        }
    }

    private void AddHeadlights()
    {
        var s = Machine.Def.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        foreach (var x in new[] { s.Width * 0.3f, -s.Width * 0.3f })
        {
            var light = new SpotLight3D
            {
                Position = new Vector3(x, Mathf.Min(s.Height - 0.3f, 2.2f), front),
                // Spot lights shine along -Z: turn around to face forward (+Z), tipped toward the ground.
                Rotation = new Vector3(Mathf.DegToRad(-18f), Mathf.Pi, 0f),
                SpotRange = 30f,
                SpotAngle = 32f,
                LightEnergy = 4f,
                LightColor = new Color(1f, 0.94f, 0.82f),
                ShadowEnabled = false,
                Visible = false,
            };
            _rig.Root.AddChild(light);
            _headlights.Add(light);
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    public NVec2 GroundPosition => Machine.Footprint.Center;
}
