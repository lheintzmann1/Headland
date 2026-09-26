using System.Numerics;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Ownership;
using Headland.Core.World;

namespace Headland.Core;

/// <summary>The farmer avatar: walks, enters vehicles, and is the source of manual vehicle input.</summary>
public sealed class PlayerCharacter(EventBus events)
{
    public const float Radius = 0.3f;
    public const float WalkSpeed = 1.9f;
    public const float RunSpeed = 5f;
    public const float EnterDistance = 2.2f;

    /// <summary>The farm the player works for: its vehicles are the ones they can drive.</summary>
    public int FarmId { get; set; } = Farm.PlayerId;
    public Vector2 Position { get; set; }
    public float Heading { get; set; }
    public Vector2 Velocity { get; private set; }
    public Machine? Vehicle { get; private set; }

    /// <summary>World-space move direction (length ≤ 1), written by the presentation.</summary>
    public Vector2 MoveInput { get; set; }
    public bool Running { get; set; }

    /// <summary>Manual controls for whatever vehicle the player drives.</summary>
    public ManualController Controls { get; } = new();

    public void Update(Simulation sim, float dt)
    {
        if (Vehicle != null)
        {
            Position = Vehicle.Footprint.Center;
            Velocity = Vehicle.Forward * Vehicle.Speed;
            return;
        }

        var input = MoveInput;
        if (input.LengthSquared() > 1f) input = Vector2.Normalize(input);
        var target = input * (Running ? RunSpeed : WalkSpeed);
        Velocity = Vector2.Lerp(Velocity, target, MathUtil.Saturate(dt * 12f));
        if (Velocity.LengthSquared() > 0.01f) Heading = MathUtil.HeadingOf(Velocity);

        var p = Position + Velocity * dt;
        for (var pass = 0; pass < 2; pass++) p = ResolveCollisions(sim, p);
        Position = Vector2.Clamp(p, new Vector2(0.5f), new Vector2(sim.World.Size - 0.5f));
    }

    private static Vector2 ResolveCollisions(Simulation sim, Vector2 p)
    {
        foreach (var o in sim.World.Obstacles)
        {
            if (Vector2.DistanceSquared(o.Center, p) > MathF.Pow(o.BoundingRadius + Radius, 2)) continue;
            p = o.Shape == ObstacleShape.Circle
                ? PushOutOfCircle(p, o.Center, o.Radius)
                : PushOutOfBox(p, new Obb(o.Center, o.HalfExtents, o.Heading));
        }
        foreach (var m in sim.Machines.All)
        {
            var box = m.Footprint;
            if (Vector2.DistanceSquared(box.Center, p) > MathF.Pow(box.BoundingRadius + Radius, 2)) continue;
            p = PushOutOfBox(p, box);
        }
        return p;
    }

    private static Vector2 PushOutOfCircle(Vector2 p, Vector2 c, float r)
    {
        var d = p - c;
        var dist = d.Length();
        var min = r + Radius;
        if (dist >= min) return p;
        return dist < 1e-4f ? c + new Vector2(min, 0) : c + d / dist * min;
    }

    private static Vector2 PushOutOfBox(Vector2 p, Obb box)
    {
        var local = p - box.Center;
        var lx = Vector2.Dot(local, box.AxisX);
        var ly = Vector2.Dot(local, box.AxisY);
        var hx = box.HalfExtents.X + Radius;
        var hy = box.HalfExtents.Y + Radius;
        if (MathF.Abs(lx) >= hx || MathF.Abs(ly) >= hy) return p;
        // Push out along the axis of least penetration.
        var px = hx - MathF.Abs(lx);
        var py = hy - MathF.Abs(ly);
        if (px < py) lx = MathF.Sign(lx == 0 ? 1 : lx) * hx;
        else ly = MathF.Sign(ly == 0 ? 1 : ly) * hy;
        return box.Center + box.AxisX * lx + box.AxisY * ly;
    }

    public Machine? NearestEnterable(Simulation sim)
    {
        Machine? best = null;
        var bestD = EnterDistance;
        foreach (var m in sim.Machines.All)
        {
            if (!m.IsMotorized || !CanEnter(m)) continue;
            var d = m.Footprint.Distance(Position);
            if (d < bestD)
            {
                bestD = d;
                best = m;
            }
        }
        return best;
    }

    /// <summary>
    /// The vehicle after (step 1) or before (step -1) the current one in spawn order, wrapping around; on foot, the
    /// first or the last. Null when there is no other vehicle to enter.
    /// </summary>
    public Machine? NextVehicle(Simulation sim, int step)
    {
        var vehicles = sim.Machines.All.Where(m => m.IsMotorized && (m == Vehicle || CanEnter(m))).ToList();
        if (vehicles.Count == 0) return null;
        var i = Vehicle != null ? vehicles.IndexOf(Vehicle) : step > 0 ? -1 : vehicles.Count;
        var next = vehicles[((i + step) % vehicles.Count + vehicles.Count) % vehicles.Count];
        return next == Vehicle ? null : next;
    }

    /// <summary>
    /// The farm's vehicles that are free or driven by a helper (the helper keeps control until dismissed).
    /// </summary>
    private bool CanEnter(Machine m) => m.FarmId == FarmId && m.Controller is null or FieldWorkController;

    /// <summary>Puts the player back in a vehicle (or on foot) as a save had them, without an event.</summary>
    internal void Restore(Machine? vehicle)
    {
        Vehicle = vehicle;
        Controls.Input = default;
        if (vehicle != null) vehicle.Controller ??= Controls;
    }

    public bool Enter(Machine m)
    {
        if (!m.IsMotorized || !CanEnter(m)) return false;
        Controls.Input = default;
        m.Controller ??= Controls;
        Vehicle = m;
        events.Publish(new VehicleEntered(m));
        return true;
    }

    /// <summary>Steps out on the driver's side (or the other side / behind if blocked).</summary>
    public void Exit(Simulation sim)
    {
        var m = Vehicle;
        if (m == null) return;
        var half = m.Def.Size.Width * 0.5f + 0.7f;
        var z = m.Def.Size.CenterZ;
        Vector2[] spots =
        [
            m.LocalToWorld(half, z), m.LocalToWorld(-half, z),
            m.LocalToWorld(0f, z - m.Def.Size.Length * 0.5f - 0.8f), m.LocalToWorld(0f, z + m.Def.Size.Length * 0.5f + 0.8f),
        ];
        var spot = spots.FirstOrDefault(s => IsFree(sim, s), spots[0]);
        if (m.Controller == Controls) m.Controller = null;
        Controls.Input = default;
        Vehicle = null;
        Position = spot;
        Velocity = Vector2.Zero;
        events.Publish(new VehicleExited(m));
    }

    private static bool IsFree(Simulation sim, Vector2 p)
    {
        foreach (var o in sim.World.Obstacles)
            if (Vector2.Distance(o.Center, p) < o.BoundingRadius + Radius &&
                (o.Shape == ObstacleShape.Circle || new Obb(o.Center, o.HalfExtents, o.Heading).Distance(p) < Radius))
                return false;
        foreach (var m in sim.Machines.All)
            if (m.Footprint.Distance(p) < Radius) return false;
        return true;
    }
}
