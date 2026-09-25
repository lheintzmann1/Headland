using FarmSim.Game.Common;
using FarmSim.Core;
using FarmSim.Core.Content;
using FarmSim.Core.Machines;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace FarmSim.Game.Vehicles;

/// <summary>
/// Follows a Core machine: pose on the terrain (with pitch/roll), wheels, lift, pipe, reel, tipper and load.
/// Draws the procedural placeholder, or a glTF model (e.g. from Blockbench) whose named parts are animated.
/// </summary>
public partial class MachineView : Node3D
{
    private const float TipAngleDeg = 42f;

    public Simulation Sim { get; init; } = null!;
    public Machine Machine { get; init; } = null!;
    private MachineRig _rig = null!;
    private ModelParts? _model;
    private readonly List<SpotLight3D> _headlights = [];
    private float _lastTankLevel;
    private float _reelAngle;

    public override void _Ready()
    {
        Name = $"{Machine.Def.Id}_{Machine.Id}";
        _rig = PlaceholderBuilder.Build(Machine.Def);
        AddChild(_rig.Root);
        _model = ModelParts.Load(this, Machine.Def);
        if (_model != null) _rig.Root.Visible = false;
        if (Machine.IsMotorized) AddHeadlights();
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

        // Shared animation state.
        var rearSteer = m.Def.Motorized?.SteerAxle == "rear";
        var steer = rearSteer ? -m.SteerAngle : m.SteerAngle;
        var pipeOut = Ease(m.PipeAnim);
        var tip = Ease(m.TipAnim);
        var flowing = false;
        if (m.Def.Pipe is { } pipe)
        {
            var tank = m.Unit(pipe.FillUnit)!;
            flowing = m.PipeAnim > 0.95f && tank.Level < _lastTankLevel - 0.01f;
            _lastTankLevel = tank.Level;
        }
        if (m.Def.WorkArea?.Type == "harvester" && m.Parent is { TurnedOn: true }) _reelAngle += (float)delta * 3.5f;
        var load = m.Def.Tipper != null ? m.Unit(m.Def.Tipper.FillUnit) : m.FillUnits.FirstOrDefault();

        if (_model != null) _model.Animate(m, steer, pipeOut, tip, _reelAngle, load);
        else AnimatePlaceholder(m, steer, pipeOut, tip, flowing, load);
    }

    private void AnimatePlaceholder(Machine m, float steer, float pipeOut, float tip, bool flowing, FillUnit? load)
    {
        foreach (var (steerNode, spin, w) in _rig.Wheels)
        {
            spin.Rotation = new Vector3(m.Distance / w.Radius, 0f, 0f);
            if (w.Steer) steerNode.Rotation = new Vector3(0f, steer, 0f);
        }
        if (_rig.PipePivot != null) _rig.PipePivot.Rotation = new Vector3(0f, Mathf.Lerp(Mathf.Pi, Mathf.Pi / 2f, pipeOut), 0f);
        if (_rig.Stream != null) _rig.Stream.Visible = flowing;
        if (_rig.Reel != null) _rig.Reel.Rotation = new Vector3(_reelAngle, 0f, 0f);
        if (_rig.TipPivot != null) _rig.TipPivot.Rotation = new Vector3(-tip * Mathf.DegToRad(TipAngleDeg), 0f, 0f);

        if (_rig.Content == null) return;
        if (load == null || load.IsEmpty)
        {
            _rig.Content.Visible = false;
            return;
        }
        var h = Mathf.Max(0.05f, load.Fraction * _rig.ContentHeight);
        _rig.Content.Visible = true;
        _rig.Content.Scale = new Vector3(1f, h, 1f);
        _rig.Content.Position = _rig.Content.Position with { Y = _rig.ContentFloor + h * 0.5f };
        if (load.FillType != null && Sim.Content.FillTypes.TryGetValue(load.FillType, out var ft))
            _rig.Content.MaterialOverride = Materials.Get(Conv.Hex(ft.Color), 0.95f);
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
            AddChild(light);
            _headlights.Add(light);
        }
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    public NVec2 GroundPosition => Machine.Footprint.Center;

    /// <summary>
    /// A glTF model instance and its mapped moving parts. Each part is animated relative to its rest rotation,
    /// around its own pivot (a Blockbench group's origin).
    /// </summary>
    private sealed class ModelParts
    {
        private readonly List<(Node3D node, Vector3 rest, WheelDef def)> _wheels = [];
        private (Node3D node, Vector3 rest)? _pipe;
        private (Node3D node, Vector3 rest)? _tipper;
        private (Node3D node, Vector3 rest)? _reel;
        private (Node3D node, Vector3 scale)? _load;

        public static ModelParts? Load(Node3D owner, MachineDef def)
        {
            var v = def.Visual;
            if (string.IsNullOrEmpty(v.Model)) return null;
            if (!ResourceLoader.Exists(v.Model))
            {
                GD.PushWarning($"{def.Id}: model '{v.Model}' not found (was it imported?); using the placeholder");
                return null;
            }
            var holder = new Node3D
            {
                Name = "Model",
                Position = v.Offset.Length == 3 ? new Vector3(v.Offset[0], v.Offset[1], v.Offset[2]) : Vector3.Zero,
                Rotation = new Vector3(0f, Mathf.DegToRad(v.YawDeg), 0f),
                Scale = Vector3.One * v.Scale,
            };
            owner.AddChild(holder);
            var instance = GD.Load<PackedScene>(v.Model).Instantiate();
            holder.AddChild(instance);

            var parts = new ModelParts();
            var mapped = new List<string>();
            foreach (var (role, nodeName) in v.Nodes ?? new Dictionary<string, string>())
            {
                if (instance.FindChild(nodeName, recursive: true, owned: false) is not Node3D node)
                {
                    GD.PushWarning($"{def.Id}: model has no node '{nodeName}' for '{role}'");
                    continue;
                }
                mapped.Add(role);
                if (role.StartsWith("wheel") && int.TryParse(role[5..], out var i) && i >= 0 && i < def.Wheels.Length)
                    parts._wheels.Add((node, node.Rotation, def.Wheels[i]));
                else if (role == "pipe") parts._pipe = (node, node.Rotation);
                else if (role == "tipper") parts._tipper = (node, node.Rotation);
                else if (role == "reel") parts._reel = (node, node.Rotation);
                else if (role == "load") parts._load = (node, node.Scale);
                else
                {
                    mapped.Remove(role);
                    GD.PushWarning($"{def.Id}: unknown model part role '{role}'");
                }
            }
            GD.Print($"{def.Id}: model {v.Model} (parts: {(mapped.Count > 0 ? string.Join(", ", mapped) : "none")})");
            return parts;
        }

        public void Animate(Machine m, float steer, float pipeOut, float tip, float reelAngle, FillUnit? load)
        {
            // Euler order is YXZ: steering (Y) turns the wheel, rolling (X) spins it about its axle.
            foreach (var (node, rest, w) in _wheels)
                node.Rotation = rest + new Vector3(m.Distance / w.Radius, w.Steer ? steer : 0f, 0f);
            // The pipe rests folded backward and swings 90° out to the left.
            if (_pipe is { } pipe) pipe.node.Rotation = pipe.rest + new Vector3(0f, -Mathf.Pi / 2f * pipeOut, 0f);
            // The tipper bed hinges at its node's pivot (the rear edge) and lifts its front.
            if (_tipper is { } bed) bed.node.Rotation = bed.rest + new Vector3(-Mathf.DegToRad(TipAngleDeg) * tip, 0f, 0f);
            if (_reel is { } reel) reel.node.Rotation = reel.rest + new Vector3(reelAngle, 0f, 0f);
            // The load block is modeled full, with its pivot at the bottom, and scaled with the fill level.
            if (_load is { } fill)
            {
                var empty = load == null || load.IsEmpty;
                fill.node.Visible = !empty;
                if (!empty) fill.node.Scale = fill.scale with { Y = fill.scale.Y * Mathf.Max(0.02f, load!.Fraction) };
            }
        }
    }
}
