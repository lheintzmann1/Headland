using Headland.Game.Common;
using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Pois;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.World;

/// <summary>POIs (buildings and sites), trees (seasonal colors), sell/shop area markers and field signs.</summary>
public partial class PropsRenderer : Node3D
{
    public Simulation Sim { get; init; } = null!;
    private MultiMesh? _broadleafCanopy;
    private int _shownMonth = -1;
    private readonly Dictionary<FieldInfo, Label3D> _signs = new();
    private IDisposable? _ownerChanges;

    public override void _Ready()
    {
        foreach (var poi in Sim.World.Pois) AddChild(BuildPoi(poi));
        BuildTrees();
        foreach (var a in Sim.World.SellPoints) AddChild(BuildArea(a, new Color(0.86f, 0.68f, 0.2f), $"{a.Name}\nSells grain · tip here (U)"));
        foreach (var a in Sim.World.Shops) AddChild(BuildArea(a, new Color(0.35f, 0.6f, 0.85f), $"{a.Name}\nBuy seed here (R)"));
        foreach (var f in Sim.World.Fields)
        {
            _signs[f] = FieldSign(f);
            AddChild(_signs[f]);
            UpdateSign(f);
        }
        _ownerChanges = Sim.Events.Subscribe<FarmlandOwnerChanged>(e => e.Farmland.Fields.ForEach(UpdateSign));
    }

    public override void _ExitTree() => _ownerChanges?.Dispose();

    public override void _Process(double delta)
    {
        var month = Sim.Clock.Month;
        if (month != _shownMonth) ApplySeason(month);
    }

    // ------------------------------------------------------------------ POIs

    /// <summary>A POI in its local space (+Z its front), from its placeholder parts or its model.</summary>
    private Node3D BuildPoi(Poi poi)
    {
        var root = new Node3D
        {
            Name = poi.Id,
            Position = new Vector3(poi.Position.X, 0f, poi.Position.Y),
            Rotation = new Vector3(0f, poi.Heading, 0f),
        };
        var parts = new Node3D { Name = "Parts" };
        root.AddChild(parts);
        foreach (var part in poi.Def.Parts) parts.AddChild(BuildPart(poi, part));
        if (LoadModel(poi) is { } model)
        {
            root.AddChild(model);
            parts.Visible = false;
        }
        return root;
    }

    /// <summary>Height of the lowest corner of a box, so slopes never show a gap under the walls.</summary>
    private float LowestGround(Obb box)
    {
        Span<NVec2> corners = stackalloc NVec2[4];
        MathUtil.RectCorners(box.Center, box.Heading, box.HalfExtents.X, box.HalfExtents.Y, corners);
        var y = float.MaxValue;
        foreach (var c in corners) y = Mathf.Min(y, Sim.World.HeightAt(c));
        return y;
    }

    private Node3D? LoadModel(Poi poi)
    {
        var v = poi.Def.Visual;
        if (string.IsNullOrEmpty(v?.Model)) return null;
        if (!ResourceLoader.Exists(v.Model))
        {
            GD.PushWarning($"{poi.Def.Id}: model '{v.Model}' not found (was it imported?); using the placeholder");
            return null;
        }
        var offset = v.Offset.Length == 3 ? new Vector3(v.Offset[0], v.Offset[1], v.Offset[2]) : Vector3.Zero;
        var holder = new Node3D
        {
            Name = "Model",
            Position = offset + new Vector3(0f, LowestGround(poi.Footprint), 0f),
            Rotation = new Vector3(0f, Mathf.DegToRad(v.YawDeg), 0f),
            Scale = Vector3.One * v.Scale,
        };
        holder.AddChild(GD.Load<PackedScene>(v.Model).Instantiate());
        return holder;
    }

    private Node3D BuildPart(Poi poi, PoiPartDef b)
    {
        var root = new Node3D
        {
            Name = b.Shape,
            Position = new Vector3(b.X, LowestGround(poi.PartBox(b)), b.Z),
            Rotation = new Vector3(0f, Mathf.DegToRad(b.RotDeg), 0f),
        };
        var color = Conv.Hex(b.Color);
        switch (b.Shape)
        {
            case "house":
                Box(root, new Vector3(b.W, b.H * 0.62f, b.D), new Vector3(0, b.H * 0.31f - 0.3f, 0), color);
                Roof(root, b.W + 0.8f, b.H * 0.42f, b.D + 0.6f, b.H * 0.62f - 0.3f, new Color(0.35f, 0.22f, 0.18f));
                Box(root, new Vector3(0.9f, 2.4f, 0.9f), new Vector3(b.W * 0.25f, b.H * 0.9f, b.D * 0.1f), new Color(0.45f, 0.36f, 0.3f));
                for (var k = -1; k <= 1; k++)
                    Box(root, new Vector3(1.2f, 1.3f, 0.1f), new Vector3(k * b.W * 0.3f, b.H * 0.35f, b.D * 0.5f + 0.02f), new Color(0.12f, 0.14f, 0.16f));
                Box(root, new Vector3(1.1f, 2.1f, 0.1f), new Vector3(b.W * 0.15f, 0.75f, b.D * 0.5f + 0.02f), new Color(0.3f, 0.2f, 0.14f));
                break;
            case "shed":
                Box(root, new Vector3(b.W, b.H * 0.72f, b.D), new Vector3(0, b.H * 0.36f - 0.3f, 0), color);
                Roof(root, b.W + 0.6f, b.H * 0.28f, b.D + 0.8f, b.H * 0.72f - 0.3f, new Color(0.42f, 0.44f, 0.45f), ridgeAlongX: true);
                Box(root, new Vector3(b.W * 0.7f, b.H * 0.6f, 0.1f), new Vector3(0, b.H * 0.3f - 0.3f, b.D * 0.5f + 0.02f), new Color(0.1f, 0.1f, 0.1f));
                break;
            case "silo":
            case "tank":
                Cylinder(root, b.W * 0.5f, b.W * 0.5f, b.H * 0.82f, new Vector3(0, b.H * 0.41f - 0.2f, 0), color, 0.45f, 0.6f);
                Cylinder(root, 0.4f, b.W * 0.52f, b.H * 0.18f, new Vector3(0, b.H * 0.91f - 0.2f, 0), color * 0.9f, 0.45f, 0.6f);
                Box(root, new Vector3(0.5f, b.H * 0.9f, 0.08f), new Vector3(0, b.H * 0.45f, b.W * 0.5f + 0.05f), Materials.DarkSteel);
                break;
            case "elevator":
                Box(root, new Vector3(b.W * 0.5f, b.H, b.D * 0.5f), new Vector3(0, b.H * 0.5f - 0.3f, 0), color);
                Box(root, new Vector3(b.W * 0.7f, 3f, b.D * 0.7f), new Vector3(0, b.H + 1.2f, 0), color * 0.9f);
                var leg = Box(root, new Vector3(0.8f, b.H * 0.9f, 0.8f), new Vector3(-b.W * 0.6f, b.H * 0.45f, 0), Materials.Steel);
                leg.RotationDegrees = new Vector3(0, 0, -18f);
                Box(root, new Vector3(b.W, 4f, b.D), new Vector3(0, 1.7f, 0), color * 0.8f);
                break;
            case "store":
                Box(root, new Vector3(b.W, b.H, b.D), new Vector3(0, b.H * 0.5f - 0.3f, 0), color);
                Box(root, new Vector3(b.W + 0.6f, 0.25f, b.D + 0.6f), new Vector3(0, b.H - 0.2f, 0), new Color(0.3f, 0.3f, 0.3f));
                Box(root, new Vector3(b.W * 0.6f, 0.9f, 0.1f), new Vector3(0, b.H * 0.75f, b.D * 0.5f + 0.05f), new Color(0.75f, 0.68f, 0.45f));
                break;
            case "pallets":
                for (var i = 0; i < 6; i++)
                {
                    var px = (i % 3 - 1) * 1.3f;
                    var pz = (i / 3 - 0.5f) * 1.3f;
                    Box(root, new Vector3(1.2f, 0.15f, 1.0f), new Vector3(px, 0.08f, pz), new Color(0.55f, 0.45f, 0.3f));
                    Box(root, new Vector3(1.1f, 0.9f, 0.95f), new Vector3(px, 0.6f, pz), color);
                }
                break;
            default:
                Box(root, new Vector3(b.W, b.H, b.D), new Vector3(0, b.H * 0.5f, 0), color);
                break;
        }
        return root;
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color, float rough = 0.85f)
    {
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = Materials.Get(color, rough) };
        parent.AddChild(m);
        return m;
    }

    private static void Cylinder(Node3D parent, float top, float bottom, float height, Vector3 pos, Color color, float rough, float metal)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = 20, Rings = 1 },
            Position = pos,
            MaterialOverride = Materials.Get(color, rough, metal),
        });
    }

    private static void Roof(Node3D parent, float w, float h, float d, float baseY, Color color, bool ridgeAlongX = false)
    {
        // PrismMesh ridge runs along Z; rotate so it follows the building's longer side.
        var along = ridgeAlongX || w > d;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = along ? new Vector3(d, h, w) : new Vector3(w, h, d) },
            Position = new Vector3(0, baseY + h * 0.5f, 0),
            Rotation = new Vector3(0, along ? Mathf.Pi / 2f : 0f, 0),
            MaterialOverride = Materials.Get(color, 0.7f),
        });
    }

    // ------------------------------------------------------------------ Trees

    private void BuildTrees()
    {
        var w = Sim.World;
        var trunkMesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 1f, RadialSegments = 6, Rings = 1 };
        var broadMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 9, Rings = 5 };
        var conifMesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 1f, Height = 1f, RadialSegments = 8, Rings = 2 };
        var mat = Materials.Get(Colors.White, 0.9f, 0f, vertexColor: true);

        var broad = w.Trees.Where(t => t.Species == 0).ToList();
        var conif = w.Trees.Where(t => t.Species == 1).ToList();

        MultiMesh Make(Mesh mesh, int count) => new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = count,
        };

        var trunks = Make(trunkMesh, w.Trees.Count);
        _broadleafCanopy = Make(broadMesh, broad.Count);
        var conifers = Make(conifMesh, conif.Count * 2);

        for (var i = 0; i < w.Trees.Count; i++)
        {
            var t = w.Trees[i];
            var y = w.HeightAt(t.Position);
            var trunkH = t.Species == 0 ? t.Height * 0.5f : t.Height * 0.3f;
            trunks.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(1.2f, trunkH, 1.2f)),
                new Vector3(t.Position.X, y + trunkH * 0.5f - 0.2f, t.Position.Y)));
            trunks.SetInstanceColor(i, new Color(0.3f, 0.24f, 0.18f) * (0.85f + 0.3f * t.Seed));
        }
        for (var i = 0; i < broad.Count; i++)
        {
            var t = broad[i];
            var y = Sim.World.HeightAt(t.Position);
            var basis = new Basis(Vector3.Up, t.Seed * Mathf.Tau) * Basis.FromScale(new Vector3(t.Radius, t.Height * 0.33f, t.Radius * 0.9f));
            _broadleafCanopy.SetInstanceTransform(i, new Transform3D(basis, new Vector3(t.Position.X, y + t.Height * 0.62f, t.Position.Y)));
        }
        for (var i = 0; i < conif.Count; i++)
        {
            var t = conif[i];
            var y = Sim.World.HeightAt(t.Position);
            var col = new Color(0.13f, 0.2f, 0.12f) * (0.85f + 0.3f * t.Seed);
            conifers.SetInstanceTransform(i * 2, new Transform3D(Basis.FromScale(new Vector3(t.Radius, t.Height * 0.55f, t.Radius)),
                new Vector3(t.Position.X, y + t.Height * 0.45f, t.Position.Y)));
            conifers.SetInstanceTransform(i * 2 + 1, new Transform3D(Basis.FromScale(new Vector3(t.Radius * 0.7f, t.Height * 0.45f, t.Radius * 0.7f)),
                new Vector3(t.Position.X, y + t.Height * 0.75f, t.Position.Y)));
            conifers.SetInstanceColor(i * 2, col);
            conifers.SetInstanceColor(i * 2 + 1, col * 1.08f);
        }

        AddChild(new MultiMeshInstance3D { Name = "Trunks", Multimesh = trunks, MaterialOverride = mat });
        AddChild(new MultiMeshInstance3D { Name = "Broadleaf", Multimesh = _broadleafCanopy, MaterialOverride = mat });
        AddChild(new MultiMeshInstance3D { Name = "Conifers", Multimesh = conifers, MaterialOverride = mat });
    }

    /// <summary>Broadleaf canopies: fresh green in spring, deep green in summer, rust in autumn, sparse in winter.</summary>
    private void ApplySeason(int month)
    {
        _shownMonth = month;
        if (_broadleafCanopy == null) return;
        var broad = Sim.World.Trees.Where(t => t.Species == 0).ToList();
        for (var i = 0; i < broad.Count; i++)
        {
            var s = broad[i].Seed;
            Color c = month switch
            {
                3 or 4 => new Color(0.36f, 0.46f, 0.2f),
                5 or 6 or 7 or 8 => new Color(0.22f, 0.32f, 0.14f),
                9 => new Color(0.3f, 0.34f, 0.15f),
                10 => s < 0.5f ? new Color(0.55f, 0.35f, 0.12f) : new Color(0.45f, 0.4f, 0.15f),
                11 => new Color(0.42f, 0.28f, 0.14f),
                _ => new Color(0.3f, 0.26f, 0.22f, 1f),
            };
            _broadleafCanopy.SetInstanceColor(i, c * (0.82f + 0.36f * s));
        }
    }

    // ------------------------------------------------------------------ Areas and signs

    private Node3D BuildArea(Area a, Color color, string label)
    {
        var w = Sim.World;
        var root = new Node3D { Name = a.Id };
        var mat = Materials.Get(color, 0.6f);
        var y = w.HeightAt(a.Center) + 0.06f;
        (Vector3 pos, Vector3 size)[] edges =
        [
            (new Vector3(a.X + a.W * 0.5f, y, a.Z), new Vector3(a.W, 0.04f, 0.25f)),
            (new Vector3(a.X + a.W * 0.5f, y, a.Z + a.H), new Vector3(a.W, 0.04f, 0.25f)),
            (new Vector3(a.X, y, a.Z + a.H * 0.5f), new Vector3(0.25f, 0.04f, a.H)),
            (new Vector3(a.X + a.W, y, a.Z + a.H * 0.5f), new Vector3(0.25f, 0.04f, a.H)),
        ];
        foreach (var (pos, size) in edges)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = mat });
        root.AddChild(new Label3D
        {
            Text = label,
            Position = new Vector3(a.Center.X, y + 5f, a.Center.Y),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.02f,
            OutlineSize = 10,
            Modulate = color.Lightened(0.3f),
            NoDepthTest = true,
        });
        return root;
    }

    private Label3D FieldSign(FieldInfo f) => new()
    {
        Name = $"Field_{f.Id}",
        Position = Sim.World.OnGround(f.Center, 6f),
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FontSize = 48,
        PixelSize = 0.025f,
        OutlineSize = 12,
        NoDepthTest = true,
    };

    /// <summary>Your fields read bright; the neighbors' are dimmer and name their owner.</summary>
    private void UpdateSign(FieldInfo f)
    {
        var land = Sim.World.FarmlandById(f.FarmlandId);
        var yours = land?.FarmId == Sim.Player.FarmId;
        var sign = _signs[f];
        sign.Text = yours || land == null ? $"{f.Label}\n{f.AreaHa:0.00} ha" : $"{f.Label}\n{f.AreaHa:0.00} ha · {Sim.Farms.OwnerName(land)}";
        sign.Modulate = yours ? new Color(1f, 1f, 1f, 0.75f) : new Color(0.82f, 0.82f, 0.78f, 0.55f);
    }
}
