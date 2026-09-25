using FarmSim.Game.Common;
using FarmSim.Core.Content;
using Godot;

namespace FarmSim.Game.Vehicles;

/// <summary>Moving parts of a machine visual, animated by <see cref="MachineView"/>.</summary>
public sealed class MachineRig
{
    public Node3D Root { get; } = new() { Name = "Visual" };
    public List<(Node3D steer, Node3D spin, WheelDef def)> Wheels { get; } = [];
    public Node3D? PipePivot { get; set; }
    public Node3D? Stream { get; set; }
    public Node3D? TipPivot { get; set; }
    public MeshInstance3D? Content { get; set; }
    public float ContentFloor { get; set; }
    public float ContentHeight { get; set; }
    public Node3D? Reel { get; set; }
}

/// <summary>
/// Procedural stand-ins with realistic proportions, built from the machine definition's size and wheels.
/// Local space: +Z forward, +X left, origin at the definition's origin (reference axle or attacher), y = 0 on the ground.
/// </summary>
public static class PlaceholderBuilder
{
    public static MachineRig Build(MachineDef def)
    {
        var rig = new MachineRig();
        var body = Conv.Hex(def.Visual.Color);
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
                Box(rig.Root, new Vector3(def.Size.Width, def.Size.Height, def.Size.Length),
                    new Vector3(0, def.Size.Height * 0.5f, def.Size.CenterZ), body);
                break;
        }
        foreach (var w in def.Wheels) AddWheel(rig, w);
        return rig;
    }

    // ------------------------------------------------------------------ Archetypes

    private static void Tractor(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        var back = s.CenterZ - s.Length * 0.5f;
        var r = rig.Root;
        var rearR = d.Wheels.Where(w => !w.Steer).Select(w => w.Radius).DefaultIfEmpty(0.75f).Max();
        // Chassis and engine hood.
        Box(r, new Vector3(0.8f, 0.45f, front - back - 0.3f), new Vector3(0, 0.75f, (front + back) * 0.5f + 0.1f), Materials.DarkSteel);
        Box(r, new Vector3(s.Width * 0.4f, 0.95f, front - 0.75f), new Vector3(0, 1.35f, (front + 0.75f) * 0.5f), body);
        Box(r, new Vector3(s.Width * 0.38f, 0.5f, 0.1f), new Vector3(0, 1.3f, front - 0.02f), Materials.DarkSteel);
        // Cab: posts, glass, roof.
        var cabH = s.Height - 1.35f;
        var cab = new Vector3(s.Width * 0.62f, cabH, 1.7f);
        var cabCenter = new Vector3(0, 1.35f + cabH * 0.5f, -0.1f);
        var glass = new MeshInstance3D { Mesh = new BoxMesh { Size = cab - new Vector3(0.06f, 0.1f, 0.06f) }, Position = cabCenter, MaterialOverride = Materials.Glass };
        r.AddChild(glass);
        foreach (var (x, z) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            Box(r, new Vector3(0.08f, cabH, 0.08f), cabCenter + new Vector3(x * cab.X * 0.5f, 0, z * cab.Z * 0.5f), Materials.DarkSteel);
        Box(r, new Vector3(cab.X + 0.15f, 0.12f, cab.Z + 0.2f), new Vector3(0, s.Height - 0.05f, -0.1f), body);
        Box(r, new Vector3(cab.X - 0.1f, 0.55f, 1.3f), new Vector3(0, 1.1f, -0.1f), body * 0.85f); // cab base / seat box
        // Rear fenders over the big wheels.
        foreach (var side in new[] { 1f, -1f })
            Box(r, new Vector3(0.6f, 0.08f, rearR * 1.6f), new Vector3(side * (s.Width * 0.5f - 0.3f), rearR * 2f + 0.05f, 0), body);
        // Three-point linkage and exhaust.
        Box(r, new Vector3(0.9f, 0.5f, 0.25f), new Vector3(0, 0.75f, back + 0.15f), Materials.DarkSteel);
        r.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1.1f, RadialSegments = 8 },
            Position = new Vector3(-s.Width * 0.18f, s.Height - 0.35f, 0.95f),
            MaterialOverride = Materials.Get(Materials.DarkSteel, 0.5f, 0.6f),
        });
    }

    private static void Combine(MachineRig rig, MachineDef d, Color body)
    {
        var s = d.Size;
        var front = s.CenterZ + s.Length * 0.5f;
        var back = s.CenterZ - s.Length * 0.5f;
        var r = rig.Root;
        var len = front - back;
        Box(r, new Vector3(s.Width * 0.72f, 2.4f, len - 1.0f), new Vector3(0, 2.05f, back + (len - 1.0f) * 0.5f), body);
        Box(r, new Vector3(s.Width * 0.7f, 0.25f, len - 1.3f), new Vector3(0, 0.85f, back + (len - 1.3f) * 0.5f + 0.1f), Materials.DarkSteel);
        // Grain tank with an opening on top.
        var tankZ = back + len * 0.55f;
        Box(r, new Vector3(s.Width * 0.78f, 0.9f, 2.8f), new Vector3(0, 3.7f, tankZ), body.Lightened(0.08f));
        Box(r, new Vector3(s.Width * 0.62f, 0.05f, 2.4f), new Vector3(0, 4.16f, tankZ), new Color(0.12f, 0.1f, 0.08f));
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
        Box(r, new Vector3(s.Width * 0.6f, 0.6f, 0.4f), new Vector3(0, 1.1f, back + 0.1f), Materials.DarkSteel);
        Box(r, new Vector3(1.2f, 0.9f, 0.05f), new Vector3(0, 2.8f, back - 0.02f), Materials.DarkSteel);

        // Unloading pipe (folded backward, swings out to the left).
        if (d.Pipe is { } pipe)
        {
            var pivotX = s.Width * 0.36f;
            var pivot = new Node3D { Name = "PipePivot", Position = new Vector3(pivotX, 3.9f, pipe.Z) };
            r.AddChild(pivot);
            var reach = pipe.X - pivotX;
            var tube = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = reach, RadialSegments = 10 },
                Position = new Vector3(0, 0.25f, reach * 0.5f),
                Rotation = new Vector3(Mathf.Pi / 2f, 0, 0),
                MaterialOverride = Materials.Get(body, 0.6f, 0.2f),
            };
            pivot.AddChild(tube);
            Box(pivot, new Vector3(0.35f, 0.55f, 0.35f), new Vector3(0, 0.05f, reach), body * 0.8f);
            var stream = new Node3D { Name = "Stream", Position = new Vector3(0, -1.8f, reach), Visible = false };
            Box(stream, new Vector3(0.22f, 3.3f, 0.22f), Vector3.Zero, new Color(0.8f, 0.68f, 0.4f));
            pivot.AddChild(stream);
            rig.PipePivot = pivot;
            rig.Stream = stream;
        }
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
        if (d.Attacher is { } a)
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
        var content = Box(bed, new Vector3(s.Width - 0.2f, 1f, s.Length - 0.2f), new Vector3(0, 0.12f, s.CenterZ), new Color(0.78f, 0.64f, 0.38f));
        content.Visible = false;
        rig.TipPivot = pivot;
        rig.Content = content;
        rig.ContentFloor = 0.12f;
        rig.ContentHeight = wallH - 0.1f;
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
        if (d.Attacher is { } a)
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
        rig.Reel = reel;
    }

    // ------------------------------------------------------------------ Parts

    private static void AddWheel(MachineRig rig, WheelDef w)
    {
        var steer = new Node3D { Name = "Wheel", Position = new Vector3(w.X, w.Radius, w.Z) };
        var spin = new Node3D();
        steer.AddChild(spin);
        spin.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = w.Radius, BottomRadius = w.Radius, Height = w.Width, RadialSegments = 18 },
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.Tire, 0.95f),
        });
        spin.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = w.Radius * 0.6f, BottomRadius = w.Radius * 0.6f, Height = w.Width + 0.03f, RadialSegments = 12 },
            Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
            MaterialOverride = Materials.Get(Materials.Rim, 0.5f, 0.3f),
        });
        // Tread blocks make the rotation visible.
        for (var k = 0; k < 8; k++)
        {
            var a = k * Mathf.Tau / 8f;
            spin.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w.Width * 1.02f, 0.06f, w.Radius * 0.35f) },
                Position = new Vector3(0, Mathf.Sin(a) * (w.Radius - 0.01f), Mathf.Cos(a) * (w.Radius - 0.01f)),
                Rotation = new Vector3(-a, 0, 0),
                MaterialOverride = Materials.Get(Materials.Tire * 1.3f, 0.95f),
            });
        }
        rig.Root.AddChild(steer);
        rig.Wheels.Add((steer, spin, w));
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color)
    {
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = Materials.Get(color, 0.75f) };
        parent.AddChild(m);
        return m;
    }
}
