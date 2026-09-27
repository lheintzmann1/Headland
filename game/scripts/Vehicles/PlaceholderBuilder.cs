using Headland.Game.Common;
using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>
/// Procedural stand-ins with realistic proportions: the body of the machine's placeholder archetype, built from its
/// size (and its parts that move, as roles: tipper, load, reel), and the blocks of visual.parts its options show.
/// Component views add their own parts: wheels, pipe, linkages. Local space: +Z forward, +X left, origin at the
/// definition's origin (reference axle or attacher), y = 0 on the ground.
/// </summary>
public static class PlaceholderBuilder
{
    public static MachineRig Build(MachineDef def)
    {
        var rig = new MachineRig { IsPlaceholder = true };
        var body = Conv.Hex(def.Visual.Color);
        var hinge = def.Get<RunningGearDef>()?.Articulation;
        if (hinge != null)
        {
            var frame = new Node3D { Name = "FrontFrame", Position = new Vector3(0f, 0f, hinge.Z) };
            rig.Root.AddChild(frame);
            rig.SetFrontFrame(frame, hinge.Z);
        }
        switch (def.Visual.Placeholder)
        {
            case "tractor": Tractor(rig, def, body); break;
            case "combine": Combine(rig, def, body); break;
            case "trailer": Trailer(rig, def, body); break;
            case "cultivator": Cultivator(rig, def, body); break;
            case "seeder": Seeder(rig, def, body); break;
            case "header": Header(rig, def, body, corn: false); break;
            case "cornheader": Header(rig, def, body, corn: true); break;
            default:
                if (hinge != null) Articulated(rig, def, body, hinge.Z);
                else
                    Box(rig.Root, new Vector3(def.Size.Width, def.Size.Height, def.Size.Length),
                        new Vector3(0, def.Size.Height * 0.5f, def.Size.CenterZ), body);
                break;
        }
        // Extra blocks (a front weight), unless they belong to options the machine doesn't have.
        foreach (var p in def.Visual.Parts.Where(p => !def.Hides(p.Id)))
            Box(rig, new Vector3(p.W, p.H, p.D), new Vector3(p.X, p.Y, p.Z), p.Color != null ? Conv.Hex(p.Color) : body).Name = p.Id;
        return rig;
    }

    // ------------------------------------------------------------------ Archetypes

    private static void Tractor(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        var back = s.CenterZ - s.Length * 0.5f;
        var r = rig.Root;
        var w = BodyWidth(d);
        var rearR = (d.Get<RunningGearDef>()?.Axles ?? []).Where(a => a.Steering == "fixed").Select(a => a.Wheels.Radius).DefaultIfEmpty(0.75f).Max();
        // Chassis and engine hood.
        Box(r, new Vector3(0.8f, 0.45f, front - back - 0.3f), new Vector3(0, 0.75f, (front + back) * 0.5f + 0.1f), Materials.DarkSteel);
        Box(r, new Vector3(w * 0.4f, 0.95f, front - 0.75f), new Vector3(0, 1.35f, (front + 0.75f) * 0.5f), body);
        Box(r, new Vector3(w * 0.38f, 0.5f, 0.1f), new Vector3(0, 1.3f, front - 0.02f), Materials.DarkSteel);
        // Cab: posts, glass, roof.
        var cabH = s.Height - 1.35f;
        var cab = new Vector3(w * 0.62f, cabH, 1.7f);
        var cabCenter = new Vector3(0, 1.35f + cabH * 0.5f, -0.1f);
        var glass = new MeshInstance3D { Mesh = new BoxMesh { Size = cab - new Vector3(0.06f, 0.1f, 0.06f) }, Position = cabCenter, MaterialOverride = Materials.Glass };
        r.AddChild(glass);
        foreach (var (x, z) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            Box(r, new Vector3(0.08f, cabH, 0.08f), cabCenter + new Vector3(x * cab.X * 0.5f, 0, z * cab.Z * 0.5f), Materials.DarkSteel);
        Box(r, new Vector3(cab.X + 0.15f, 0.12f, cab.Z + 0.2f), new Vector3(0, s.Height - 0.05f, -0.1f), body);
        Box(r, new Vector3(cab.X - 0.1f, 0.55f, 1.3f), new Vector3(0, 1.1f, -0.1f), body * 0.85f); // cab base / seat box
        // Rear fenders over the big wheels.
        foreach (var side in new[] { 1f, -1f })
            Box(r, new Vector3(0.6f, 0.08f, rearR * 1.6f), new Vector3(side * (w * 0.5f - 0.3f), rearR * 2f + 0.05f, 0), body);
        // Exhaust.
        r.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1.1f, RadialSegments = 8 },
            Position = new Vector3(-w * 0.18f, s.Height - 0.35f, 0.95f),
            MaterialOverride = Materials.Get(Materials.DarkSteel, 0.5f, 0.6f),
        });
    }

    private static void Combine(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        var back = s.CenterZ - s.Length * 0.5f;
        var r = rig.Root;
        var w = BodyWidth(d);
        var len = front - back;
        Box(r, new Vector3(w * 0.72f, 2.4f, len - 1.0f), new Vector3(0, 2.05f, back + (len - 1.0f) * 0.5f), body);
        Box(r, new Vector3(w * 0.7f, 0.25f, len - 1.3f), new Vector3(0, 0.85f, back + (len - 1.3f) * 0.5f + 0.1f), Materials.DarkSteel);
        // Grain tank with an opening on top.
        var tankZ = back + len * 0.55f;
        Box(r, new Vector3(w * 0.78f, 0.9f, 2.8f), new Vector3(0, 3.7f, tankZ), body.Lightened(0.08f));
        Box(r, new Vector3(w * 0.62f, 0.05f, 2.4f), new Vector3(0, 4.16f, tankZ), new Color(0.12f, 0.1f, 0.08f));
        // Cab over the feeder.
        var cabZ = front - 1.4f;
        var glass = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.8f, 1.35f, 1.5f) },
            Position = new Vector3(0, 3.55f, cabZ),
            MaterialOverride = Materials.Glass,
        };
        r.AddChild(glass);
        Box(r, new Vector3(1.95f, 0.12f, 1.65f), new Vector3(0, 4.28f, cabZ), body);
        // Feeder house down to the header joint.
        var feeder = Box(r, new Vector3(1.3f, 0.75f, 1.8f), new Vector3(0, 1.3f, front - 0.3f), body * 0.9f);
        feeder.RotationDegrees = new Vector3(12f, 0, 0);
        // Straw chopper and engine grille.
        Box(r, new Vector3(w * 0.6f, 0.6f, 0.4f), new Vector3(0, 1.1f, back + 0.1f), Materials.DarkSteel);
        Box(r, new Vector3(1.2f, 0.9f, 0.05f), new Vector3(0, 2.8f, back - 0.02f), Materials.DarkSteel);
    }

    private static void Trailer(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        var back = s.CenterZ - s.Length * 0.5f;
        var r = rig.Root;
        var bedY = 1.25f;
        var wallH = s.Height - bedY;
        // Chassis and drawbar.
        Box(r, new Vector3(1.0f, 0.3f, s.Length - 0.4f), new Vector3(0, 0.95f, s.CenterZ), Materials.DarkSteel);
        // A drawbar out to the eye; a semi-trailer's kingpin is under its own front.
        if (d.Get<AttachableDef>() is { } a && a.Z > front)
        {
            var bar = Box(r, new Vector3(0.18f, 0.18f, a.Z - front + 0.3f), new Vector3(0, 0.65f, (a.Z + front) * 0.5f), Materials.DarkSteel);
            bar.RotationDegrees = new Vector3(-5f, 0, 0);
        }
        // Tipping bed hinged at the rear.
        var pivot = new Node3D { Name = "TipPivot", Position = new Vector3(0, bedY, back) };
        r.AddChild(pivot);
        var bed = new Node3D { Position = new Vector3(0, 0, -back) };
        pivot.AddChild(bed);
        Box(bed, new Vector3(s.Width, 0.12f, s.Length), new Vector3(0, 0.06f, s.CenterZ), body * 0.8f);
        Box(bed, new Vector3(s.Width, wallH, 0.08f), new Vector3(0, wallH * 0.5f, front), body);
        Box(bed, new Vector3(s.Width, wallH, 0.08f), new Vector3(0, wallH * 0.5f, back), body);
        Box(bed, new Vector3(0.08f, wallH, s.Length), new Vector3(s.Width * 0.5f, wallH * 0.5f, s.CenterZ), body);
        Box(bed, new Vector3(0.08f, wallH, s.Length), new Vector3(-s.Width * 0.5f, wallH * 0.5f, s.CenterZ), body);
        // Ribs on the side walls.
        for (var k = 0; k < 5; k++)
        {
            var z = back + s.Length * (k + 0.5f) / 5f;
            foreach (var side in new[] { 1f, -1f })
                Box(bed, new Vector3(0.05f, wallH, 0.08f), new Vector3(side * (s.Width * 0.5f + 0.04f), wallH * 0.5f, z), body * 0.85f);
        }
        // The load: modeled full, pivoting at the floor, scaled with the fill level.
        var load = new Node3D { Name = "Load", Position = new Vector3(0, 0.12f, 0), Visible = false };
        bed.AddChild(load);
        var full = wallH - 0.1f;
        Box(load, new Vector3(s.Width - 0.2f, full, s.Length - 0.2f), new Vector3(0, full * 0.5f, s.CenterZ), new Color(0.78f, 0.64f, 0.38f));
        rig.Add("tipper", pivot);
        rig.Add("load", load);
    }

    /// <summary>A box split at the hinge: the rear frame full height, the front one lower, swinging.</summary>
    private static void Articulated(MachineRig rig, MachineDef d, Color body, float hinge)
    {
        var s = d.Size;
        var back = s.CenterZ - s.Length * 0.5f;
        var front = s.CenterZ + s.Length * 0.5f;
        var gap = 0.15f;
        Box(rig, new Vector3(s.Width, s.Height, hinge - gap - back), new Vector3(0, s.Height * 0.5f, (back + hinge - gap) * 0.5f), body);
        Box(rig, new Vector3(s.Width * 0.85f, s.Height * 0.45f, front - hinge - gap), new Vector3(0, s.Height * 0.3f, (hinge + gap + front) * 0.5f), body);
        Box(rig.Root, new Vector3(0.5f, 0.6f, gap * 2f + 0.2f), new Vector3(0, 0.9f, hinge), Materials.DarkSteel);
    }

    private static void Cultivator(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var r = rig.Root;
        var back = s.CenterZ - s.Length * 0.5f;
        // A-frame mast at the hitch.
        var mast = Box(r, new Vector3(0.9f, 1.0f, 0.12f), new Vector3(0, 0.95f, -0.15f), body * 0.8f);
        mast.RotationDegrees = new Vector3(-15f, 0, 0);
        foreach (var z in new[] { -0.45f, -1.15f })
        {
            Box(r, new Vector3(s.Width, 0.14f, 0.14f), new Vector3(0, 0.62f, z), body);
            var n = (int)(s.Width / 0.36f);
            for (var k = 0; k < n; k++)
            {
                var x = -s.Width * 0.5f + (k + 0.5f + (z < -1f ? 0.5f : 0f)) * s.Width / n;
                if (x > s.Width * 0.5f) continue;
                var tine = Box(r, new Vector3(0.05f, 0.6f, 0.08f), new Vector3(x, 0.3f, z - 0.12f), Materials.DarkSteel);
                tine.RotationDegrees = new Vector3(25f, 0, 0);
            }
        }
        Box(r, new Vector3(0.14f, 0.14f, 1.4f), new Vector3(s.Width * 0.3f, 0.65f, -0.8f), body);
        Box(r, new Vector3(0.14f, 0.14f, 1.4f), new Vector3(-s.Width * 0.3f, 0.65f, -0.8f), body);
        // Packer roller at the back.
        r.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = s.Width - 0.1f, RadialSegments = 12 },
            Position = new Vector3(0, 0.25f, back + 0.3f),
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.Steel, 0.6f, 0.4f),
        });
    }

    private static void Seeder(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var r = rig.Root;
        Box(r, new Vector3(s.Width * 0.95f, 0.9f, 1.2f), new Vector3(0, 1.4f, 0.35f), body);
        Box(r, new Vector3(s.Width * 0.97f, 0.08f, 1.3f), new Vector3(0, 1.88f, 0.35f), body * 0.75f);
        Box(r, new Vector3(s.Width, 0.16f, 0.16f), new Vector3(0, 0.8f, -0.4f), Materials.DarkSteel);
        Box(r, new Vector3(s.Width, 0.16f, 0.16f), new Vector3(0, 0.8f, 1.0f), Materials.DarkSteel);
        var n = (int)(s.Width / 0.25f);
        for (var k = 0; k < n; k++)
        {
            var x = -s.Width * 0.5f + (k + 0.5f) * s.Width / n;
            Box(r, new Vector3(0.03f, 0.45f, 0.35f), new Vector3(x, 0.3f, -0.9f), Materials.DarkSteel);
        }
        if (d.Get<AttachableDef>() is { Z: > 1f } a)
            Box(r, new Vector3(0.16f, 0.16f, a.Z - 1.0f), new Vector3(0, 0.6f, (a.Z + 1.0f) * 0.5f), Materials.DarkSteel);
    }

    private static void Header(MachineRig rig, MachineDef d, Color body, bool corn)
    {
        var s = d.Size;
        var r = rig.Root;
        Box(r, new Vector3(s.Width, 1.0f, 0.2f), new Vector3(0, 0.75f, 0.15f), body * 0.85f);
        Box(r, new Vector3(s.Width, 0.35f, s.Length - 0.2f), new Vector3(0, 0.3f, s.Length * 0.5f), body);
        if (corn)
        {
            var rows = 6;
            for (var k = 0; k < rows; k++)
            {
                var x = -s.Width * 0.5f + (k + 0.5f) * s.Width / rows;
                var snout = new MeshInstance3D
                {
                    Mesh = new PrismMesh { Size = new Vector3(0.45f, 1.2f, 0.4f) },
                    Position = new Vector3(x, 0.4f, s.Length - 0.2f),
                    Rotation = new Vector3(Mathf.Pi / 2f, 0, 0),
                    MaterialOverride = Materials.Get(body, 0.7f),
                };
                r.AddChild(snout);
            }
            return;
        }
        Box(r, new Vector3(s.Width, 0.08f, 0.12f), new Vector3(0, 0.12f, s.Length - 0.05f), Materials.DarkSteel);
        foreach (var side in new[] { 1f, -1f })
            Box(r, new Vector3(0.1f, 0.7f, s.Length), new Vector3(side * s.Width * 0.5f, 0.45f, s.Length * 0.5f), body * 0.8f);
        // Reel: spins while threshing.
        var reel = new Node3D { Name = "Reel", Position = new Vector3(0, 1.15f, s.Length * 0.62f) };
        r.AddChild(reel);
        for (var k = 0; k < 5; k++)
        {
            var a = k * Mathf.Tau / 5f;
            reel.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(s.Width - 0.3f, 0.05f, 0.05f) },
                Position = new Vector3(0, Mathf.Sin(a) * 0.45f, Mathf.Cos(a) * 0.45f),
                MaterialOverride = Materials.Get(new Color(0.75f, 0.6f, 0.2f), 0.7f),
            });
        }
        reel.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = s.Width - 0.2f, RadialSegments = 8 },
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.DarkSteel, 0.6f),
        });
        rig.Add("reel", reel);
    }

    /// <summary>
    /// How wide a body is drawn: its fixed axle's track and a tire, so that duals, flotation tires or tracks widening the
    /// machine (its size) don't widen the body.
    /// </summary>
    private static float BodyWidth(MachineDef d) =>
        (d.Get<RunningGearDef>()?.Axles ?? []).Where(a => a.Steering == "fixed").Select(a => a.Track + 0.6f).DefaultIfEmpty(d.Size.Width).Max();

    // ------------------------------------------------------------------ Parts

    /// <summary>
    /// The tires on one side of an axle, in one node at the (inner) tire's center that steers (y) and rolls (x): a dual's
    /// second tire goes on the <paramref name="outward"/> side (+1 left, -1 right).
    /// </summary>
    public static Node3D Wheel(WheelSetDef w, float outward)
    {
        var wheel = new Node3D { Name = "Wheel" };
        // Rim size and tread bars: a flotation tire has a tall sidewall on a small rim, a row-crop one many thin bars.
        var (rim, bars) = w.Type switch
        {
            "flotation" => (0.45f, 10),
            "rowCrop" => (0.62f, 12),
            _ => (0.6f, 8),
        };
        Tire(wheel, w, 0f, rim, bars);
        if (w.Type == "dual") Tire(wheel, w, outward * (w.Width + w.Gap), rim, bars);
        return wheel;
    }

    private static void Tire(Node3D wheel, WheelSetDef w, float x, float rim, int bars)
    {
        wheel.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = w.Radius, BottomRadius = w.Radius, Height = w.Width, RadialSegments = 18 },
            Position = new Vector3(x, 0f, 0f),
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.Tire, 0.95f),
        });
        wheel.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = w.Radius * rim, BottomRadius = w.Radius * rim, Height = w.Width + 0.03f, RadialSegments = 12 },
            Position = new Vector3(x, 0f, 0f),
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.Rim, 0.5f, 0.3f),
        });
        // Tread bars make the rotation visible.
        for (var k = 0; k < bars; k++)
        {
            var a = k * Mathf.Tau / bars;
            wheel.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w.Width * 1.02f, 0.06f, w.Radius * 2.8f / bars) },
                Position = new Vector3(x, Mathf.Sin(a) * (w.Radius - 0.01f), Mathf.Cos(a) * (w.Radius - 0.01f)),
                Rotation = new Vector3(-a, 0, 0),
                MaterialOverride = Materials.Get(Materials.Tire * 1.3f, 0.95f),
            });
        }
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color)
    {
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = Materials.Get(color, 0.75f) };
        parent.AddChild(m);
        return m;
    }

    /// <summary>A box at <paramref name="pos"/> in machine space: on an articulated machine's front frame when ahead of the hinge.</summary>
    public static MeshInstance3D Box(MachineRig rig, Vector3 size, Vector3 pos, Color color)
    {
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Materials.Get(color, 0.75f) };
        rig.AddPart(m, pos);
        return m;
    }
}
