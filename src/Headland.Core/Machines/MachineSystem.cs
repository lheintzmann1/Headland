using System.Numerics;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines.Components;
using Headland.Core.Machines.Work;
using Headland.Core.Ownership;
using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>
/// Drives machines and their components: moves vehicles and places their chains (the components work out speed and
/// steering), hitching, and work areas. Each component then updates itself (animations, transfers); the player's keys
/// go to what the components offer (see <see cref="Simulation.Offers"/>).
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

    /// <summary>
    /// Gives <paramref name="m"/> the options of <paramref name="def"/> (a def of its type). What hangs on a joint it no
    /// longer has is unhitched, and so is the machine itself when it can't hang where it does any more; the rest of its
    /// chain is placed again.
    /// </summary>
    internal void Reconfigure(Machine m, MachineDef def)
    {
        foreach (var (jointId, child) in m.Attached.ToList())
            if (def.Joints.FirstOrDefault(j => j.Id == jointId)?.Type != m.Joint(jointId)!.Type)
                Detach(child);
        if (m.Parent is { } parent && def.Get<AttachableDef>()?.Type != parent.Joint(m.ParentJoint!)!.Type) Detach(m);
        m.Reconfigure(def, Content);
        UpdateChildren(m.Root);
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

    /// <summary>
    /// Why <paramref name="vehicle"/>'s chain can't hitch the implement of its farm nearest to it, within a few meters,
    /// when none of its joints is of the implement's type ("The Loader 30 hitches to front loader consoles…"); null
    /// when there's no such implement.
    /// </summary>
    public string? AttachBlocker(Machine vehicle)
    {
        var at = vehicle.Footprint.Center;
        var types = vehicle.Chain().SelectMany(m => m.Def.Joints.Where(j => !m.Attached.ContainsKey(j.Id))).Select(j => j.Type).ToHashSet();
        var near = All
            .Where(c => c.Parent == null && c.Root != vehicle.Root && c.FarmId == vehicle.FarmId && !c.Has<Motor>())
            .Select(c => (c, type: c.Get<Attachable>()?.Def.Type))
            .Where(x => x.type != null && !types.Contains(x.type) && Vector2.Distance(x.c.Footprint.Center, at) < 8f)
            .OrderBy(x => Vector2.Distance(x.c.Footprint.Center, at))
            .FirstOrDefault();
        if (near.c is not { } child) return null;
        var joint = Content.JointTypes.GetValueOrDefault(near.type!)?.Name.ToLowerInvariant() ?? near.type;
        // An option of the vehicle that gives it such a joint: a workshop fits it.
        var fits = vehicle.Def.Configurations.Any(c => c.Options.Any(o =>
            vehicle.Def.Configure(new Dictionary<string, string>(vehicle.Def.Choices) { [c.Id] = o.Id }).Joints.Any(j => j.Type == near.type)));
        return $"The {child.Def.Name} hitches to {joint}: the {vehicle.Def.Name} has none" + (fits ? ", a workshop fits them" : "");
    }

    // ------------------------------------------------------------------ Names

    public static string SteeringName(SteeringMode mode) => mode switch
    {
        SteeringMode.AllWheel => "all-wheel",
        SteeringMode.Crab => "crab",
        _ => "normal",
    };

    public static string Months(int[] months) =>
        months.Length == 0 ? "any time" : string.Join(", ", months.Select(m => Time.Calendar.MonthNames[m - 1][..3]));

    // ------------------------------------------------------------------ Simulation

    public void Update(float dt)
    {
        foreach (var m in All)
            if (m.Parent == null && m.Has<Motor>())
                Drive(m, dt);

        foreach (var m in All)
        {
            if (m.Operating) m.OperatingHours += dt / 3600.0;
            foreach (var c in m.Components)
                c.Update(_sim, dt);
        }
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
        var seat = v.Get<Drivable>();
        var input = seat?.Input(dt) ?? new VehicleInput { Brake = true };
        seat?.DriveTools(input, dt);
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
            var lowering = m.Get<Attachable>() is { Def.Lowerable: true, LowerAnim: < Attachable.WorkingDepth };
            var working = false;
            foreach (var area in w.Areas)
            {
                var wa = area.Def;
                if (!w.Working(wa) || root.Speed < 0.05f || lowering)
                {
                    area.HasPose = false;
                    continue;
                }
                // What kept it from working before is forgotten as it works again (and reported anew if it still does).
                if (!working) w.ClearConditions();
                working = true;

                var center = m.LocalToWorld(wa.X, wa.Z);
                var half = new Vector2(wa.Width * 0.5f, wa.Length * 0.5f);
                MathUtil.RectCorners(center, m.Heading, half.X, half.Y, pts[..4]);
                var count = 4;
                Obb? before = null;
                if (area.HasPose && Vector2.Distance(area.PrevCenter, center) < 5f)
                {
                    MathUtil.RectCorners(area.PrevCenter, area.PrevHeading, half.X, half.Y, pts[4..]);
                    count = 8;
                    before = new Obb(area.PrevCenter, half, area.PrevHeading);
                }
                area.HasPose = true;
                area.PrevCenter = center;
                area.PrevHeading = m.Heading;

                _cells.Clear();
                Geometry.RasterizeConvex(pts[..count], WorldMap.CellSize, World.CellsX, World.CellsZ, _cells);
                if (limit != null) _cells.RemoveAll(i => !limit.Contains(World.CellCenter(i % World.CellsX, i / World.CellsX)));
                var refused = KeepAllowed(root.FarmId, w, wa);
                var fieldId = WorkedFieldId(center);
                var pass = new WorkPass(_sim, m, w, wa, _cells, fieldId, before);
                var changed = wa.Work.Work(pass);
                pass.Finish();
                if (changed == 0)
                {
                    if (refused != null) w.Report(new NotAllowed(refused));
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
        var crop = wa.Work.Crop(w, Content);
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

    // ------------------------------------------------------------------ Transfers

    /// <summary>A fill unit of a machine (other than <paramref name="exclude"/>) at <paramref name="point"/> that takes the fill type.</summary>
    public FillUnit? FindReceiver(Vector2 point, string fillType, Machine exclude)
    {
        foreach (var m in All)
        {
            if (m == exclude || !m.Footprint.Contains(point, 0.4f)) continue;
            foreach (var u in m.FillUnits)
                if (u.CanAccept(fillType) && !m.ClosedOver(u)) return u;
        }
        return null;
    }
}
