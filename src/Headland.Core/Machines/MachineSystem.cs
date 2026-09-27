using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;
using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>
/// Drives machines and their components: moves vehicles and places their chains (the components work out speed and
/// steering), hitching, the player's commands, and work areas. Each component then updates itself (animations,
/// transfers).
/// </summary>
public sealed class MachineSystem
{
    private const float AttachDistance = 1.6f;
    private readonly Simulation _sim;
    private readonly List<int> _cells = [];
    private readonly List<(Machine m, Vector2 pos, float heading)> _saved = [];
    private int _nextId = 1;

    public MachineSystem(Simulation sim) => _sim = sim;

    public List<Machine> All { get; } = [];

    /// <summary>Id of the next machine spawned (ids are never reused).</summary>
    internal int NextId { get => _nextId; set => _nextId = value; }

    private WorldMap World => _sim.World;
    private ContentDatabase Content => _sim.Content;
    private EventBus Events => _sim.Events;

    /// <summary>A new machine of type <paramref name="defId"/>, with the options <paramref name="configuration"/> picks (the defaults for the rest).</summary>
    public Machine Spawn(string defId, Vector2 position, float heading, int farmId = Farm.PlayerId,
        IReadOnlyDictionary<string, string>? configuration = null)
    {
        var def = Content.Machines[defId];
        if (configuration != null) def = def.Configure(configuration);
        var m = new Machine(_nextId++, def, position, heading, farmId);
        if (m.Get<WorkAreas>() is { Sows: true } seeder) seeder.Crop = DefaultSeedCrop();
        All.Add(m);
        return m;
    }

    public Machine? ById(int id) => All.Find(m => m.Id == id);

    private int DefaultSeedCrop()
    {
        var month = _sim.Clock.Month;
        var idx = Content.Crops.FindIndex(c => c.SowingMonths.Contains(month));
        return Math.Max(0, idx);
    }

    // ------------------------------------------------------------------ Hitching

    public bool Attach(Machine parent, string jointId, Machine child)
    {
        if (!Hitch(parent, jointId, child)) return false;
        Events.Publish(new ImplementAttached(parent, jointId, child));
        return true;
    }

    /// <summary>Attaches without publishing an event: machines placed by the map or restored from a save.</summary>
    internal bool Hitch(Machine parent, string jointId, Machine child)
    {
        var joint = parent.Joint(jointId);
        if (joint == null || child.Get<Attachable>() is not { } a || a.Def.Type != joint.Type) return false;
        if (parent.Attached.ContainsKey(jointId) || child.Parent != null) return false;
        parent.Attached[jointId] = child;
        child.Parent = parent;
        child.ParentJoint = jointId;
        foreach (var c in child.Components) c.OnHitched();
        if (a.Def.Mode == "trailed")
        {
            // Keep the trailer's heading but clamp it into the allowed articulation.
            var frame = parent.PartToWorld(joint.X, joint.Z).heading;
            var max = a.Def.MaxArticulationDeg * MathUtil.Deg2Rad;
            var rel = Math.Clamp(MathUtil.WrapAngle(child.Heading - frame), -max, max);
            child.Heading = frame + rel;
        }
        UpdateChildren(parent);
        return true;
    }

    public void Detach(Machine child)
    {
        var parent = child.Parent;
        var jointId = child.ParentJoint;
        if (parent == null || jointId == null) return;
        parent.Attached.Remove(jointId);
        child.Parent = null;
        child.ParentJoint = null;
        child.Speed = 0f;
        foreach (var c in child.Components) c.OnDetached();
        Events.Publish(new ImplementDetached(parent, jointId, child));
    }

    /// <summary>Nearest free joint in the vehicle's chain that an unattached implement of the same farm can hook onto.</summary>
    public (Machine parent, AttacherJointDef joint, Machine child)? FindAttachable(Machine vehicle)
    {
        (Machine, AttacherJointDef, Machine)? best = null;
        var bestDist = AttachDistance;
        foreach (var p in vehicle.Chain())
        foreach (var j in p.Def.Joints)
        {
            if (p.Attached.ContainsKey(j.Id)) continue;
            var (jw, frame) = p.PartToWorld(j.X, j.Z);
            foreach (var c in All)
            {
                if (c.Parent != null || c.Get<Attachable>() is not { } a || c.Has<Motor>() || c.Root == vehicle.Root) continue;
                if (c.FarmId != vehicle.FarmId) continue;
                if (a.Def.Type != j.Type) continue;
                var aw = c.LocalToWorld(a.Def.X, a.Def.Z);
                var d = Vector2.Distance(jw, aw);
                if (d >= bestDist) continue;
                var rel = MathF.Abs(MathUtil.WrapAngle(c.Heading - frame));
                var limit = a.Def.Mode == "mounted" ? 45f : a.Def.MaxArticulationDeg;
                if (rel > limit * MathUtil.Deg2Rad) continue;
                bestDist = d;
                best = (p, j, c);
            }
        }
        return best;
    }

    /// <summary>Attaches the nearest compatible implement, or detaches the last one in the chain.</summary>
    public void ToggleAttach(Machine vehicle)
    {
        if (FindAttachable(vehicle) is var (p, j, c))
        {
            Attach(p, j.Id, c);
            return;
        }
        var leaf = vehicle.Chain().LastOrDefault(m => m != vehicle);
        if (leaf != null) Detach(leaf);
        else _sim.Notifications.Post("Nothing to attach nearby: back up to an implement's hitch");
    }

    // ------------------------------------------------------------------ Commands

    /// <summary>Implements of the vehicle's chain that are lowered and raised (the vehicle's own attachable excluded).</summary>
    public static List<Attachable> Lowerable(Machine vehicle) =>
        vehicle.Chain().Where(m => m != vehicle).Select(m => m.Get<Attachable>()).OfType<Attachable>().Where(a => a.Def.Lowerable).ToList();

    /// <summary>What the turn-on key switches in the vehicle's chain.</summary>
    public static List<ISwitchable> Switchable(Machine vehicle) =>
        vehicle.Chain().SelectMany(m => m.Components.OfType<ISwitchable>()).Where(s => s.CanTurnOn).ToList();

    public void ToggleLower(Machine vehicle)
    {
        var tools = Lowerable(vehicle);
        if (tools.Count == 0)
        {
            _sim.Notifications.Post("No implement to lower");
            return;
        }
        var lower = !tools.Any(t => t.Lowered);
        foreach (var t in tools)
        {
            t.Lowered = lower;
            // Lowering a folded implement unfolds it first.
            if (lower && t.Machine.Get<AnimatedParts>() is { Folded: true } parts) parts.Folded = false;
        }
    }

    public void ToggleOn(Machine vehicle)
    {
        var parts = Switchable(vehicle);
        if (parts.Count == 0)
        {
            _sim.Notifications.Post("Nothing to turn on");
            return;
        }
        var on = !parts.Any(p => p.On);
        foreach (var p in parts) p.On = on;
    }

    /// <summary>Folds the vehicle's chain for transport (raising it), or unfolds it.</summary>
    public void ToggleFold(Machine vehicle)
    {
        var parts = vehicle.Chain().Select(m => m.Get<AnimatedParts>()).OfType<AnimatedParts>().Where(p => p.CanFold).ToList();
        if (parts.Count == 0)
        {
            _sim.Notifications.Post("Nothing to fold");
            return;
        }
        var fold = !parts.Any(p => p.Folded);
        foreach (var p in parts) p.Folded = fold;
    }

    /// <summary>Combine: fold/unfold the pipe. Trailer: start/stop tipping.</summary>
    public void ToggleUnload(Machine vehicle)
    {
        if (vehicle.Get<Pipe>() is { } pipe)
        {
            pipe.Out = !pipe.Out;
            return;
        }
        var tippers = vehicle.Chain().Select(m => m.Get<Tipper>()).OfType<Tipper>().ToList();
        if (tippers.Count == 0)
        {
            _sim.Notifications.Post("Nothing to unload");
            return;
        }
        foreach (var t in tippers)
        {
            if (t.Tipping) t.Tipping = false;
            else if (t.Start(_sim) is { } why) _sim.Notifications.Post(why, t.Load.IsEmpty ? Severity.Info : Severity.Warning);
        }
    }

    /// <summary>Switches the vehicle to its next steering mode (normal, all-wheel, crab).</summary>
    public void CycleSteering(Machine vehicle)
    {
        if (vehicle.Get<RunningGear>() is not { Def.Modes.Length: > 1 } gear)
        {
            _sim.Notifications.Post("It has only one way to steer");
            return;
        }
        if (vehicle.Get<Drivable>()?.Controller is FieldWorkController)
        {
            _sim.Notifications.Post("The helper steers: dismiss them first");
            return;
        }
        var modes = gear.Def.Modes;
        gear.Mode = modes[(Array.IndexOf(modes, gear.Mode) + 1) % modes.Length];
        _sim.Notifications.Post($"Steering: {SteeringName(gear.Mode)}");
    }

    public static string SteeringName(SteeringMode mode) => mode switch
    {
        SteeringMode.AllWheel => "all-wheel",
        SteeringMode.Crab => "crab",
        _ => "normal",
    };

    public void CycleSeed(Machine vehicle)
    {
        var seeders = vehicle.Chain().Select(m => m.Get<WorkAreas>()).OfType<WorkAreas>().Where(w => w.Sows).ToList();
        if (seeders.Count == 0)
        {
            _sim.Notifications.Post("No seeder attached");
            return;
        }
        foreach (var s in seeders)
        {
            s.Crop = (s.Crop + 1) % Content.Crops.Count;
            var crop = Content.Crops[s.Crop];
            _sim.Notifications.Post($"Seeder: {crop.Name} (sow {Months(crop.SowingMonths)})");
        }
    }

    public static string Months(int[] months) =>
        months.Length == 0 ? "any time" : string.Join(", ", months.Select(m => Time.Calendar.MonthNames[m - 1][..3]));

    // ------------------------------------------------------------------ Simulation

    public void Update(float dt)
    {
        foreach (var m in All)
            if (m.Parent == null && m.Has<Motor>())
                Drive(m, dt);

        foreach (var m in All)
        foreach (var c in m.Components)
            c.Update(_sim, dt);
    }

    /// <summary>
    /// Moves a vehicle and its chain: the engine works out how fast it can go (motor), the driver asks (drivable), the
    /// engine gives the speed and the running gear the turn; the chain follows, and stops short of what it would bump
    /// into.
    /// </summary>
    private void Drive(Machine v, float dt)
    {
        var motor = v.Get<Motor>()!;
        motor.Prepare(_sim);
        var input = v.Get<Drivable>()?.Input(dt) ?? new VehicleInput { Brake = true };
        var s = motor.Drive(_sim, input, dt);
        if (MathF.Abs(s) < 1e-4f) s = 0f;
        var gear = v.Get<RunningGear>()!;
        gear.Steer(input, s, dt);
        // Standing, it may still turn: on the spot with skid steer.
        var (turn, sideways) = gear.Motion(s, dt);

        if (s == 0f && turn == 0f)
        {
            v.Speed = 0f;
            foreach (var m in v.Chain())
                if (m.Get<WorkAreas>() is { } w)
                    foreach (var area in w.Areas)
                        area.HasPose &= w.Working(area.Def);
            // An articulated vehicle's front frame swings standing too, with what hangs on it.
            if (gear.SteeringKind == SteeringKind.Articulated) UpdateChildren(v);
            return;
        }

        _saved.Clear();
        foreach (var m in v.Chain()) _saved.Add((m, m.Position, m.Heading));

        v.Heading = MathUtil.WrapAngle(v.Heading + turn);
        v.Position += MathUtil.Forward(v.Heading) * s * dt;
        if (sideways != 0f) v.Position += MathUtil.Left(v.Heading) * sideways;
        UpdateChildren(v);

        if (ChainCollides(v) && !SavedPoseCollides(v))
        {
            foreach (var (m, pos, heading) in _saved)
            {
                m.Position = pos;
                m.Heading = heading;
            }
            v.Speed = 0f;
            return;
        }

        v.Speed = s;
        foreach (var (m, pos, heading) in _saved)
        {
            var moved = Vector2.Dot(m.Position - pos, m.Forward);
            m.Get<RunningGear>()?.Roll(moved, Vector2.Dot(m.Position - pos, MathUtil.Left(m.Heading)), MathUtil.WrapAngle(m.Heading - heading), dt);
            if (m != v) m.Speed = moved / dt;
        }
        ProcessWorkAreas(v);
    }

    /// <summary>Moves a machine and everything attached to it, with trailers straightened behind it.</summary>
    public void Teleport(Machine root, Vector2 position, float heading)
    {
        root.Position = position;
        root.Heading = heading;
        root.Speed = 0f;
        root.Get<WorkAreas>()?.ForgetPoses();
        var depth = 1;
        foreach (var m in root.Chain().Skip(1))
        {
            // Start each trailed child far behind so it straightens toward its hitch.
            m.Heading = heading;
            m.Position = position - MathUtil.Forward(heading) * (20f * depth++);
            m.Speed = 0f;
            m.Get<WorkAreas>()?.ForgetPoses();
        }
        UpdateChildren(root);
    }

    /// <summary>
    /// Places attached machines: mounted ones rigidly, trailed ones following their hitch point. A joint on an
    /// articulated vehicle's front frame swings with it.
    /// </summary>
    public void UpdateChildren(Machine parent)
    {
        foreach (var (jointId, child) in parent.Attached)
        {
            var j = parent.Joint(jointId)!;
            var (jw, frame) = parent.PartToWorld(j.X, j.Z);
            var a = child.Get<Attachable>()!.Def;
            if (a.Mode == "mounted")
            {
                child.Heading = frame;
                child.Position = jw - (MathUtil.Left(child.Heading) * a.X + MathUtil.Forward(child.Heading) * a.Z);
            }
            else
            {
                // It turns about the middle of the axles holding it (self-steering ones too when backing up): that
                // point is drawn straight toward the hitch.
                var pivot = child.Get<RunningGear>()?.Def.Pivot(SteeringMode.Normal, parent.Root.Speed < 0f) ?? 0f;
                var dir = jw - child.LocalToWorld(0f, pivot);
                if (dir.LengthSquared() > 1e-6f)
                {
                    var max = a.MaxArticulationDeg * MathUtil.Deg2Rad;
                    var rel = Math.Clamp(MathUtil.WrapAngle(MathUtil.HeadingOf(dir) - frame), -max, max);
                    child.Heading = MathUtil.WrapAngle(frame + rel);
                }
                child.Position = jw - MathUtil.Forward(child.Heading) * a.Z - MathUtil.Left(child.Heading) * a.X;
            }
            UpdateChildren(child);
        }
    }

    private bool ChainCollides(Machine root)
    {
        foreach (var m in root.Chain())
        {
            var (rear, front) = m.Boxes;
            if (Collides(root, rear) || front is { } f && Collides(root, f)) return true;
        }
        return false;
    }

    /// <summary>True if the box (a little smaller) is off the map, or bumps into an obstacle or a machine not in the chain.</summary>
    private bool Collides(Machine root, Obb fp)
    {
        var box = fp with { HalfExtents = fp.HalfExtents - new Vector2(0.1f, 0.1f) };
        var c = box.Center;
        if (c.X < 1f || c.Y < 1f || c.X > World.Size - 1f || c.Y > World.Size - 1f) return true;
        var r = box.BoundingRadius;
        foreach (var o in World.Obstacles)
        {
            if (Vector2.DistanceSquared(o.Center, c) > MathF.Pow(r + o.BoundingRadius, 2)) continue;
            if (Geometry.Overlaps(box, o)) return true;
        }
        foreach (var other in All)
        {
            if (other.Root == root) continue;
            var (rear, front) = other.Boxes;
            if (Geometry.Overlaps(box, rear) || front is { } f && Geometry.Overlaps(box, f)) return true;
        }
        return false;
    }

    private bool SavedPoseCollides(Machine root)
    {
        var now = new List<(Machine, Vector2, float)>();
        foreach (var (m, _, _) in _saved) now.Add((m, m.Position, m.Heading));
        foreach (var (m, pos, heading) in _saved)
        {
            m.Position = pos;
            m.Heading = heading;
        }
        var collided = ChainCollides(root);
        foreach (var (m, pos, heading) in now)
        {
            m.Position = pos;
            m.Heading = heading;
        }
        return collided;
    }

    // ------------------------------------------------------------------ Work areas

    private void ProcessWorkAreas(Machine root)
    {
        // A helper only works the field it was hired for.
        var limit = (root.Get<Drivable>()?.Controller as FieldWorkController)?.Field.Shape;
        Span<Vector2> pts = stackalloc Vector2[8];
        foreach (var m in root.Chain())
        {
            if (m.Get<WorkAreas>() is not { } w) continue;
            var lowering = m.Get<Attachable>() is { Def.Lowerable: true, LowerAnim: < 0.9f };
            foreach (var area in w.Areas)
            {
                var wa = area.Def;
                if (!w.Working(wa) || root.Speed < 0.05f || lowering)
                {
                    area.HasPose = false;
                    continue;
                }

                var center = m.LocalToWorld(wa.X, wa.Z);
                MathUtil.RectCorners(center, m.Heading, wa.Width * 0.5f, wa.Length * 0.5f, pts[..4]);
                var count = 4;
                if (area.HasPose && Vector2.Distance(area.PrevCenter, center) < 5f)
                {
                    MathUtil.RectCorners(area.PrevCenter, area.PrevHeading, wa.Width * 0.5f, wa.Length * 0.5f, pts[4..]);
                    count = 8;
                }
                area.HasPose = true;
                area.PrevCenter = center;
                area.PrevHeading = m.Heading;

                _cells.Clear();
                Geometry.RasterizeConvex(pts[..count], WorldMap.CellSize, World.CellsX, World.CellsZ, _cells);
                if (limit != null) _cells.RemoveAll(i => !limit.Contains(World.CellCenter(i % World.CellsX, i / World.CellsX)));
                var refused = KeepAllowed(root.FarmId, w, wa);
                var fieldId = WorkedFieldId(center);
                var changed = wa.Type switch
                {
                    "cultivator" => Cultivate(m),
                    "seeder" => Sow(m, w, wa),
                    "harvester" => Harvest(m, wa, fieldId),
                    _ => 0,
                };
                if (changed == 0)
                {
                    if (refused != null) m.Status = refused;
                    continue;
                }
                var ha = changed * WorldMap.CellArea / 10000f;
                m.WorkedHa += ha;
                Events.Publish(new FieldWorked(m, wa.Type, fieldId, ha));
            }
        }
    }

    /// <summary>
    /// Drops the cells the farm may not work (<see cref="Farms.MayWork"/>): other farms' land, and fields it has no
    /// contract for. Returns why when that left field ground unworked.
    /// </summary>
    private string? KeepAllowed(int farmId, WorkAreas w, WorkAreaDef wa)
    {
        var crop = wa.Type == "seeder" ? Content.Crops[w.Crop] : null;
        var refused = -1;
        _cells.RemoveAll(i =>
        {
            if (_sim.Farms.MayWork(farmId, i, wa.Type, crop)) return false;
            if (refused < 0 && World.IsWorkable((GroundType)World.Layers.Ground[i])) refused = i;
            return true;
        });
        return refused >= 0 ? _sim.Farms.WorkBlocker(farmId, refused, wa.Type, crop) : null;
    }

    /// <summary>
    /// The field worked: the one under the work area's center, else the first one under its cells (the work area
    /// straddling the edge on its way in or out).
    /// </summary>
    private int WorkedFieldId(Vector2 center)
    {
        var (cx, cz) = World.WorldToCell(center);
        if (World.InBounds(cx, cz) && World.Layers.FieldId[World.CellIndex(cx, cz)] is var id and > 0) return id;
        foreach (var i in _cells)
            if (World.Layers.FieldId[i] is var cell and > 0) return cell;
        return 0;
    }

    private int Cultivate(Machine m)
    {
        var angle = WorldGen.AngleToByte(m.Heading);
        var n = 0;
        foreach (var i in _cells)
            if (WorkOps.Cultivate(World, i, angle)) n++;
        m.Status = null;
        return n;
    }

    private int Sow(Machine m, WorkAreas w, WorkAreaDef wa)
    {
        var crop = Content.Crops[w.Crop];
        var unit = m.Unit(wa.FillUnit)!;
        var kgPerCell = crop.SeedKgPerHa * WorldMap.CellArea / 10000f;
        var inWindow = _sim.Crops.InSowingWindow(crop, _sim.Clock.Month);
        var health = inWindow ? (byte)255 : (byte)150;
        var angle = WorldGen.AngleToByte(m.Heading);
        var n = 0;
        m.Status = inWindow ? null : $"{crop.Name} sown out of season: poor yield";
        foreach (var i in _cells)
        {
            if (!WorkOps.CanSow(World, i)) continue;
            if (unit.Level < kgPerCell)
            {
                m.Status = "Out of seed: buy seed at the shop";
                _sim.Notifications.Post($"{m.Def.Name} is out of seed", Severity.Warning, 10);
                break;
            }
            unit.Remove(kgPerCell);
            WorkOps.Sow(World, i, w.Crop, health, angle);
            n++;
        }
        return n;
    }

    private int Harvest(Machine header, WorkAreaDef wa, int fieldId)
    {
        var combine = header.Parent!;
        var tank = combine.Get<Thresher>()!.Tank;
        var L = World.Layers;
        var n = 0;
        CropDef? threshed = null;
        var threshedAmount = 0f;
        header.Status = null;
        foreach (var i in _cells)
        {
            var cropId = L.Crop[i];
            if (cropId == 0) continue;
            var def = Content.Crops[cropId - 1];
            var stage = L.Stage[i];
            if (stage == CropStage.Dead)
            {
                WorkOps.ClearToStubble(World, i, WorldGen.AngleToByte(header.Heading));
                n++;
                continue;
            }
            if (!def.Stages[stage].Harvestable) continue;
            if (!wa.HarvestGroups.Contains(def.HarvestGroup))
            {
                header.Status = $"Wrong header for {def.Name}";
                continue;
            }
            var liters = def.YieldPerHa * (L.Health[i] / 255f) * WorldMap.CellArea / 10000f;
            if (!tank.CanAccept(def.FillType) || tank.Free < liters)
            {
                header.Status = tank.IsEmpty || tank.FillType == def.FillType
                    ? "Grain tank full: unload into a trailer"
                    : $"Tank holds {Content.FillTypes[tank.FillType!].Name}: empty it first";
                _sim.Notifications.Post(header.Status, Severity.Warning, 10);
                break;
            }
            tank.Add(def.FillType, liters);
            threshed ??= def;
            threshedAmount += liters;
            WorkOps.ClearToStubble(World, i, WorldGen.AngleToByte(header.Heading));
            n++;
        }
        // The tank takes one fill type at a time, so one tick threshes one crop.
        if (threshed != null) Events.Publish(new CropHarvested(combine, threshed.Id, threshed.FillType, threshedAmount, fieldId));
        return n;
    }

    // ------------------------------------------------------------------ Transfers

    /// <summary>A fill unit of a machine (other than <paramref name="exclude"/>) at <paramref name="point"/> that takes the fill type.</summary>
    public FillUnit? FindReceiver(Vector2 point, string fillType, Machine exclude)
    {
        foreach (var m in All)
        {
            if (m == exclude || !m.Footprint.Contains(point, 0.4f)) continue;
            foreach (var u in m.FillUnits)
                if (u.CanAccept(fillType)) return u;
        }
        return null;
    }
}
