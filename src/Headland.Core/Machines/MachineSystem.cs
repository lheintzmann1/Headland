using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Ownership;
using Headland.Core.World;

namespace Headland.Core.Machines;

/// <summary>Vehicle kinematics, hitching, work areas and fill transfers.</summary>
public sealed class MachineSystem
{
    private const float AttachDistance = 1.6f;
    private readonly Simulation _sim;
    private readonly List<int> _cells = [];
    private readonly List<(Machine m, Vector2 pos, float heading)> _saved = [];
    private readonly Dictionary<int, (string sellPoint, string fillType, float amount, float income)> _sales = new();
    private int _nextId = 1;

    public MachineSystem(Simulation sim) => _sim = sim;

    public List<Machine> All { get; } = [];

    private WorldMap World => _sim.World;
    private ContentDatabase Content => _sim.Content;
    private EventBus Events => _sim.Events;

    public Machine Spawn(string defId, Vector2 position, float heading, int farmId = Farm.PlayerId)
    {
        var def = Content.Machines[defId];
        var m = new Machine(_nextId++, def, position, heading, farmId);
        if (def.SeedTank != null) m.SelectedCrop = DefaultSeedCrop();
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
        if (joint == null || child.Def.Attacher == null || child.Def.Attacher.Type != joint.Type) return false;
        if (parent.Attached.ContainsKey(jointId) || child.Parent != null) return false;
        parent.Attached[jointId] = child;
        child.Parent = parent;
        child.ParentJoint = jointId;
        child.Lowered = false;
        child.LowerAnim = 0f;
        child.HasWorkPose = false;
        if (child.Def.Attacher.Mode == "trailed")
        {
            // Keep the trailer's heading but clamp it into the allowed articulation.
            var max = child.Def.Attacher.MaxArticulationDeg * MathUtil.Deg2Rad;
            var rel = Math.Clamp(MathUtil.WrapAngle(child.Heading - parent.Heading), -max, max);
            child.Heading = parent.Heading + rel;
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
        child.TurnedOn = false;
        child.Tipping = false;
        child.Lowered = false;
        child.HasWorkPose = false;
        Events.Publish(new ImplementDetached(parent, jointId, child));
    }

    /// <summary>Nearest free joint in the vehicle's chain that an unattached implement of the same farm can hook onto.</summary>
    public (Machine parent, AttacherJointDef joint, Machine child)? FindAttachable(Machine vehicle)
    {
        (Machine, AttacherJointDef, Machine)? best = null;
        var bestDist = AttachDistance;
        foreach (var p in vehicle.Chain())
        foreach (var j in p.Def.AttacherJoints)
        {
            if (p.Attached.ContainsKey(j.Id)) continue;
            var jw = p.LocalToWorld(j.X, j.Z);
            foreach (var c in All)
            {
                if (c.Parent != null || c.Def.Attacher == null || c.IsMotorized || c.Root == vehicle.Root) continue;
                if (c.FarmId != vehicle.FarmId) continue;
                if (c.Def.Attacher.Type != j.Type) continue;
                var aw = c.LocalToWorld(c.Def.Attacher.X, c.Def.Attacher.Z);
                var d = Vector2.Distance(jw, aw);
                if (d >= bestDist) continue;
                var rel = MathF.Abs(MathUtil.WrapAngle(c.Heading - p.Heading));
                var limit = c.Def.Attacher.Mode == "mounted" ? 45f : c.Def.Attacher.MaxArticulationDeg;
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

    public void ToggleLower(Machine vehicle)
    {
        var tools = vehicle.Chain().Where(m => m != vehicle && m.Def.WorkArea is { RequiresLowered: true }).ToList();
        if (tools.Count == 0)
        {
            _sim.Notifications.Post("No implement to lower");
            return;
        }
        var lower = !tools.Any(t => t.Lowered);
        foreach (var t in tools) t.Lowered = lower;
    }

    public void ToggleOn(Machine vehicle)
    {
        var parts = vehicle.Chain().Where(m => m.Def.HarvestTank != null || m.Def.WorkArea is { RequiresOn: true }).ToList();
        if (parts.Count == 0)
        {
            _sim.Notifications.Post("Nothing to turn on");
            return;
        }
        var on = !parts.Any(p => p.TurnedOn);
        foreach (var p in parts) p.TurnedOn = on;
    }

    /// <summary>Combine: fold/unfold the pipe. Trailer: start/stop tipping.</summary>
    public void ToggleUnload(Machine vehicle)
    {
        if (vehicle.Def.Pipe != null)
        {
            vehicle.PipeOut = !vehicle.PipeOut;
            return;
        }
        var tippers = vehicle.Chain().Where(m => m.Def.Tipper != null).ToList();
        if (tippers.Count == 0)
        {
            _sim.Notifications.Post("Nothing to unload");
            return;
        }
        foreach (var t in tippers)
        {
            if (t.Tipping)
            {
                t.Tipping = false;
                continue;
            }
            var unit = t.Unit(t.Def.Tipper!.FillUnit)!;
            var sell = World.SellPointAt(t.Footprint.Center);
            if (unit.IsEmpty) _sim.Notifications.Post($"{t.Def.Name} is empty");
            else if (sell == null) _sim.Notifications.Post("Drive the trailer into a sell point to tip", Severity.Warning);
            else if (!sell.FillTypes.Contains(unit.FillType!))
                _sim.Notifications.Post($"{sell.Name} does not buy {Content.FillTypes[unit.FillType!].Name}", Severity.Warning);
            else t.Tipping = true;
        }
    }

    public void CycleSeed(Machine vehicle)
    {
        var seeders = vehicle.Chain().Where(m => m.Def.SeedTank != null).ToList();
        if (seeders.Count == 0)
        {
            _sim.Notifications.Post("No seeder attached");
            return;
        }
        foreach (var s in seeders)
        {
            s.SelectedCrop = (s.SelectedCrop + 1) % Content.Crops.Count;
            var crop = Content.Crops[s.SelectedCrop];
            _sim.Notifications.Post($"Seeder: {crop.Name} (sow {Months(crop.SowingMonths)})");
        }
    }

    public static string Months(int[] months) =>
        months.Length == 0 ? "any time" : string.Join(", ", months.Select(m => Time.Calendar.MonthNames[m - 1][..3]));

    /// <summary>Refills every machine of the chain parked inside a shop with what the shop sells.</summary>
    public void BuyAtShop(Machine vehicle)
    {
        var bought = false;
        foreach (var m in vehicle.Chain())
        {
            var shop = World.ShopAt(m.Footprint.Center);
            if (shop == null) continue;
            foreach (var unit in m.FillUnits)
            foreach (var ft in shop.FillTypes)
            {
                if (!unit.CanAccept(ft)) continue;
                var price = _sim.Economy.Price(ft, _sim.Clock.Month);
                var amount = _sim.Economy.Buy(ft, unit.Free, _sim.Clock.Month);
                unit.Add(ft, amount);
                if (amount > 0.5f)
                {
                    bought = true;
                    Events.Publish(new FillBought(m, shop.Id, ft, amount, amount * price));
                }
            }
        }
        if (!bought) _sim.Notifications.Post("Park the implement inside a shop area to buy supplies");
    }

    // ------------------------------------------------------------------ Simulation

    public void Update(float dt)
    {
        foreach (var m in All)
            if (m.Parent == null && m.IsMotorized)
                Drive(m, dt);

        foreach (var m in All)
        {
            m.LowerAnim = MathUtil.MoveToward(m.LowerAnim, m.Lowered ? 1f : 0f, dt * 1.5f);
            m.PipeAnim = MathUtil.MoveToward(m.PipeAnim, m.PipeOut ? 1f : 0f, dt * 0.4f);
            m.TipAnim = MathUtil.MoveToward(m.TipAnim, m.Tipping ? 1f : 0f, dt * 0.35f);
            if (m.Def.Pipe != null) UpdatePipe(m, dt);
            if (m.Def.Tipper != null) UpdateTipper(m, dt);
        }
    }

    private bool WorkEngaged(Machine m) => m.Def.WorkArea is { } wa &&
                                           (!wa.RequiresLowered || m.Lowered) &&
                                           (wa.Type == "harvester" ? m.Parent is { TurnedOn: true } : !wa.RequiresOn || m.TurnedOn);

    private void Drive(Machine v, float dt)
    {
        var mot = v.Def.Motorized!;
        var input = v.Controller?.GetInput(v, dt) ?? new VehicleInput { Brake = true };
        v.Status = null;

        // Speed limits from working implements, engine power and soft ground.
        var maxF = mot.MaxSpeedKmh * MathUtil.KmhToMs;
        var maxR = mot.MaxReverseKmh * MathUtil.KmhToMs;
        var demand = 0f;
        var totalMass = 0f;
        foreach (var m in v.Chain())
        {
            totalMass += m.SelfMassWithLoad(Content);
            if (!WorkEngaged(m)) continue;
            var wa = m.Def.WorkArea!;
            maxF = MathF.Min(maxF, wa.MaxWorkSpeedKmh * MathUtil.KmhToMs);
            demand += wa.RequiredPowerHp;
        }
        if (demand > mot.PowerHp)
        {
            maxF *= MathF.Max(0.3f, mot.PowerHp / demand);
            v.Status = $"Needs {demand:N0} hp, has {mot.PowerHp:N0} hp";
        }
        var (cx, cz) = World.WorldToCell(v.Position);
        if (World.InBounds(cx, cz))
        {
            var i = World.CellIndex(cx, cz);
            if (World.IsWorkable((GroundType)World.Layers.Ground[i]) && World.Layers.Moisture[i] > 204)
                maxF *= 0.75f;
        }
        var accel = mot.Acceleration * Math.Clamp(v.SelfMassWithLoad(Content) / MathF.Max(1f, totalMass), 0.25f, 1f);

        var s = v.Speed;
        if (input.Brake) s = MathUtil.MoveToward(s, 0f, mot.Braking * dt);
        else if (input.Throttle > 0.01f)
        {
            var target = maxF * input.Throttle;
            s = s < -0.01f ? MathUtil.MoveToward(s, 0f, mot.Braking * dt)
                : s < target ? MathF.Min(target, s + accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else if (input.Throttle < -0.01f)
        {
            var target = -maxR * -input.Throttle;
            s = s > 0.01f ? MathUtil.MoveToward(s, 0f, mot.Braking * dt)
                : s > target ? MathF.Max(target, s - accel * dt)
                : MathUtil.MoveToward(s, target, 1.5f * dt);
        }
        else s = MathUtil.MoveToward(s, 0f, 1.5f * dt);
        if (s > maxF) s = MathUtil.MoveToward(s, maxF, mot.Braking * dt);

        var speedFactor = MathUtil.Lerp(1f, 0.4f, MathUtil.Saturate(MathF.Abs(s) / (40f * MathUtil.KmhToMs)));
        var steerTarget = Math.Clamp(input.Steer, -1f, 1f) * mot.MaxSteerDeg * MathUtil.Deg2Rad * speedFactor;
        v.SteerAngle = MathUtil.MoveToward(v.SteerAngle, steerTarget, mot.SteerRateDeg * MathUtil.Deg2Rad * dt);

        if (MathF.Abs(s) < 1e-4f)
        {
            v.Speed = 0f;
            foreach (var m in v.Chain()) m.HasWorkPose &= WorkEngaged(m);
            return;
        }

        _saved.Clear();
        foreach (var m in v.Chain()) _saved.Add((m, m.Position, m.Heading));

        v.Heading = MathUtil.WrapAngle(v.Heading + s * MathF.Tan(v.SteerAngle) / mot.Wheelbase * dt);
        v.Position += MathUtil.Forward(v.Heading) * s * dt;
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
        foreach (var (m, pos, _) in _saved)
        {
            var moved = Vector2.Dot(m.Position - pos, m.Forward);
            m.Distance += moved;
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
        root.HasWorkPose = false;
        var depth = 1;
        foreach (var m in root.Chain().Skip(1))
        {
            // Start each trailed child far behind so it straightens toward its hitch.
            m.Heading = heading;
            m.Position = position - MathUtil.Forward(heading) * (20f * depth++);
            m.Speed = 0f;
            m.HasWorkPose = false;
        }
        UpdateChildren(root);
    }

    /// <summary>Places attached machines: mounted ones rigidly, trailed ones following their hitch point.</summary>
    public void UpdateChildren(Machine parent)
    {
        foreach (var (jointId, child) in parent.Attached)
        {
            var j = parent.Joint(jointId)!;
            var jw = parent.LocalToWorld(j.X, j.Z);
            var a = child.Def.Attacher!;
            if (a.Mode == "mounted")
            {
                child.Heading = parent.Heading;
                child.Position = jw - (MathUtil.Left(child.Heading) * a.X + MathUtil.Forward(child.Heading) * a.Z);
            }
            else
            {
                var dir = jw - child.Position;
                if (dir.LengthSquared() > 1e-6f)
                {
                    var max = a.MaxArticulationDeg * MathUtil.Deg2Rad;
                    var rel = Math.Clamp(MathUtil.WrapAngle(MathUtil.HeadingOf(dir) - parent.Heading), -max, max);
                    child.Heading = MathUtil.WrapAngle(parent.Heading + rel);
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
            var fp = m.Footprint;
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
                if (Geometry.Overlaps(box, other.Footprint)) return true;
            }
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
        var limit = (root.Controller as FieldWorkController)?.Field.Shape;
        Span<Vector2> pts = stackalloc Vector2[8];
        foreach (var m in root.Chain())
        {
            if (m.Def.WorkArea is not { } wa) continue;
            if (!WorkEngaged(m) || root.Speed < 0.05f || (wa.RequiresLowered && m.LowerAnim < 0.9f))
            {
                m.HasWorkPose = false;
                continue;
            }

            var center = m.LocalToWorld(wa.X, wa.Z);
            MathUtil.RectCorners(center, m.Heading, wa.Width * 0.5f, wa.Length * 0.5f, pts[..4]);
            var count = 4;
            if (m.HasWorkPose && Vector2.Distance(m.PrevWorkCenter, center) < 5f)
            {
                MathUtil.RectCorners(m.PrevWorkCenter, m.PrevWorkHeading, wa.Width * 0.5f, wa.Length * 0.5f, pts[4..]);
                count = 8;
            }
            m.HasWorkPose = true;
            m.PrevWorkCenter = center;
            m.PrevWorkHeading = m.Heading;

            _cells.Clear();
            Geometry.RasterizeConvex(pts[..count], WorldMap.CellSize, World.CellsX, World.CellsZ, _cells);
            if (limit != null) _cells.RemoveAll(i => !limit.Contains(World.CellCenter(i % World.CellsX, i / World.CellsX)));
            var fieldId = FieldIdAt(center);
            var changed = wa.Type switch
            {
                "cultivator" => Cultivate(m),
                "seeder" => Sow(m),
                "harvester" => Harvest(m, wa, fieldId),
                _ => 0,
            };
            if (changed == 0) continue;
            var ha = changed * WorldMap.CellArea / 10000f;
            m.WorkedHa += ha;
            Events.Publish(new FieldWorked(m, wa.Type, fieldId, ha));
        }
    }

    private int FieldIdAt(Vector2 p)
    {
        var (cx, cz) = World.WorldToCell(p);
        return World.InBounds(cx, cz) ? World.Layers.FieldId[World.CellIndex(cx, cz)] : 0;
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

    private int Sow(Machine m)
    {
        var crop = Content.Crops[m.SelectedCrop];
        var unit = m.Unit(m.Def.SeedTank)!;
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
            WorkOps.Sow(World, i, m.SelectedCrop, health, angle);
            n++;
        }
        return n;
    }

    private int Harvest(Machine header, WorkAreaDef wa, int fieldId)
    {
        var combine = header.Parent!;
        var tank = combine.Unit(combine.Def.HarvestTank)!;
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

    private void UpdatePipe(Machine m, float dt)
    {
        var pipe = m.Def.Pipe!;
        var tank = m.Unit(pipe.FillUnit)!;
        if (!m.PipeOut || m.PipeAnim < 0.95f || tank.IsEmpty) return;
        var outlet = m.LocalToWorld(pipe.X, pipe.Z);
        var target = FindReceiver(outlet, tank.FillType!, m);
        if (target == null) return;
        var ft = tank.FillType!;
        var moved = target.Add(ft, MathF.Min(pipe.RatePerSecond * dt, tank.Level));
        tank.Remove(moved);
    }

    private void UpdateTipper(Machine m, float dt)
    {
        var unit = m.Unit(m.Def.Tipper!.FillUnit)!;
        if (!m.Tipping)
        {
            FlushSale(m);
            return;
        }
        var sell = World.SellPointAt(m.Footprint.Center);
        if (unit.IsEmpty || sell == null || !sell.FillTypes.Contains(unit.FillType!))
        {
            m.Tipping = false;
            FlushSale(m);
            return;
        }
        if (m.TipAnim < 0.6f) return;
        var ft = unit.FillType!;
        var amount = unit.Remove(m.Def.Tipper.RatePerSecond * dt);
        var income = _sim.Economy.Sell(ft, amount, _sim.Clock.Month);
        var prev = _sales.GetValueOrDefault(m.Id, (sellPoint: sell.Id, fillType: ft, amount: 0f, income: 0f));
        _sales[m.Id] = (sell.Id, ft, prev.amount + amount, prev.income + income);
    }

    private void FlushSale(Machine m)
    {
        if (!_sales.Remove(m.Id, out var sale) || sale.amount < 1f) return;
        Events.Publish(new FillSold(m, sale.sellPoint, sale.fillType, sale.amount, sale.income));
    }
}
