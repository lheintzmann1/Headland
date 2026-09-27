using Headland.Game.Common;
using Headland.Core;
using Headland.Core.Content;
using Headland.Core.Contracts;
using Headland.Core.Events;
using Headland.Core.Machines;
using Headland.Core.Pois;
using Headland.Core.World;
using Godot;
using NVec2 = System.Numerics.Vector2;

namespace Headland.Game.World;

/// <summary>
/// POIs (buildings and sites) with their trigger areas, trees (seasonal colors), and field signs with the contracts on
/// them, outlining the fields the farm has a contract on.
/// </summary>
public partial class PropsRenderer : Node3D
{
    private static readonly Color ContractColor = Color.FromHtml(UI.Palette.Contract);

    public Simulation Sim { get; init; } = null!;
    private MultiMesh? _broadleafCanopy;
    private int _shownMonth = -1;
    private readonly Dictionary<FieldInfo, Label3D> _signs = new();
    private readonly Dictionary<FieldInfo, MeshInstance3D> _outlines = new();
    private IDisposable? _ownerChanges;
    private IDisposable? _contractChanges;

    public override void _Ready()
    {
        foreach (var poi in Sim.World.Pois)
        {
            AddChild(BuildPoi(poi));
            foreach (var trigger in poi.Triggers) AddChild(BuildTrigger(trigger));
        }
        BuildTrees();
        foreach (var f in Sim.World.Fields)
        {
            _signs[f] = FieldSign(f);
            AddChild(_signs[f]);
            UpdateSign(f);
        }
        _ownerChanges = Sim.Events.Subscribe<FarmlandOwnerChanged>(e => e.Farmland.Fields.ForEach(UpdateSign));
        _contractChanges = Sim.Events.SubscribeAll(e =>
        {
            if (e is IContractEvent { Contract.Field: { } field }) UpdateSign(field);
        });
    }

    public override void _ExitTree()
    {
        _ownerChanges?.Dispose();
        _contractChanges?.Dispose();
    }

    public override void _Process(double delta)
    {
        var month = Sim.Clock.Month;
        if (month != _shownMonth) ApplySeason(month);
    }

    // ------------------------------------------------------------------ POIs

    /// <summary>A POI in its local space (+Z its front), from its placeholder parts or its model (nothing if that doesn't load).</summary>
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
        if (poi.Def.Visual is { } visual && !string.IsNullOrEmpty(visual.Model))
        {
            parts.Visible = false;
            if (Models.Load(visual, poi.Def.Id) is { } model)
            {
                model.Position += new Vector3(0f, LowestGround(poi.Footprint), 0f);
                root.AddChild(model);
            }
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
            case "canopy":
                // A roof on four posts, high enough to drive under.
                foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Box(root, new Vector3(0.3f, b.H, 0.3f), new Vector3(sx * (b.W * 0.5f - 0.5f), b.H * 0.5f, sz * (b.D * 0.5f - 0.5f)), Materials.Steel);
                Box(root, new Vector3(b.W, 0.5f, b.D), new Vector3(0, b.H, 0), color);
                break;
            case "pump":
                Box(root, new Vector3(b.W * 1.8f, 0.2f, b.D * 3f), new Vector3(0, 0.1f, 0), new Color(0.55f, 0.55f, 0.52f));
                Box(root, new Vector3(b.W, b.H, b.D), new Vector3(0, b.H * 0.5f + 0.2f, 0), color);
                Box(root, new Vector3(b.W * 1.1f, 0.2f, b.D * 1.1f), new Vector3(0, b.H + 0.3f, 0), Materials.DarkSteel);
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

    // ------------------------------------------------------------------ Triggers and signs

    /// <summary>A trigger's outline on the ground, under the POI's icon, name and what the farmer can do there.</summary>
    private Node3D BuildTrigger(PoiTrigger t)
    {
        var area = t.Area;
        var color = TriggerColor(t.Type);
        var root = new Node3D
        {
            Name = $"{t.Poi.Id}_{t.Id}",
            Position = Sim.World.OnGround(area.Center, 0.06f),
            Rotation = new Vector3(0f, area.Heading, 0f),
        };
        var mat = Materials.Get(color, 0.6f);
        var (hx, hz) = (area.HalfExtents.X, area.HalfExtents.Y);
        (Vector3 pos, Vector3 size)[] edges =
        [
            (new Vector3(0f, 0f, -hz), new Vector3(hx * 2f, 0.04f, 0.25f)),
            (new Vector3(0f, 0f, hz), new Vector3(hx * 2f, 0.04f, 0.25f)),
            (new Vector3(-hx, 0f, 0f), new Vector3(0.25f, 0.04f, hz * 2f)),
            (new Vector3(hx, 0f, 0f), new Vector3(0.25f, 0.04f, hz * 2f)),
        ];
        foreach (var (pos, size) in edges)
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, MaterialOverride = mat });
        var what = string.Join(" · ", Sim.Pois.Describe(t).Append(Hint(t.Type)));
        root.AddChild(new Label3D
        {
            Text = $"{t.Poi.Name}\n{what}",
            Position = new Vector3(0f, 5f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.02f,
            OutlineSize = 10,
            Modulate = color.Lightened(0.3f),
            NoDepthTest = true,
        });
        var icon = $"res://assets/icons/{t.Poi.Def.Icon}.svg";
        if (t.Poi.Def.Icon != null && ResourceLoader.Exists(icon))
            root.AddChild(new Sprite3D
            {
                Texture = GD.Load<Texture2D>(icon),
                Position = new Vector3(0f, 7.4f, 0f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                PixelSize = 0.018f,
                Modulate = color.Lightened(0.3f),
                NoDepthTest = true,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            });
        return root;
    }

    private static Color TriggerColor(string type) => type switch
    {
        "unload" => new Color(0.86f, 0.68f, 0.2f),
        "load" => new Color(0.55f, 0.8f, 0.4f),
        "delivery" => new Color(0.78f, 0.78f, 0.72f),
        "wash" => new Color(0.45f, 0.78f, 0.82f),
        "repair" => new Color(0.86f, 0.5f, 0.3f),
        _ => new Color(0.35f, 0.6f, 0.85f),
    };

    /// <summary>How to use a trigger, with the key on the player's layout.</summary>
    private static string Hint(string type) => type switch
    {
        "unload" => $"tip or pipe here ({InputSetup.Label("unload")})",
        "delivery" => "keep clear",
        _ => $"park here ({InputSetup.Label("use")})",
    };

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

    /// <summary>
    /// Your fields read bright; the neighbors' are dimmer and name their owner and any offer on them. A field you
    /// have a contract on takes the contract color and an outline.
    /// </summary>
    private void UpdateSign(FieldInfo f)
    {
        var land = Sim.World.FarmlandById(f.FarmlandId);
        var yours = land?.FarmId == Sim.Player.FarmId;
        var contract = Sim.Contracts.On(f);
        var taken = contract is { State: ContractState.Active } && contract.FarmId == Sim.Player.FarmId;
        var sign = _signs[f];
        sign.Text = yours || land == null ? $"{f.Label}\n{f.AreaHa:0.00} ha" : $"{f.Label}\n{f.AreaHa:0.00} ha · {Sim.Farms.OwnerName(land)}";
        if (taken) sign.Text += $"\nContract: {contract!.Job}, by {Sim.Contracts.DueDate(contract).Short}";
        else if (contract is { State: ContractState.Offered }) sign.Text += $"\nOffer: {contract.Job}, ${contract.Reward:N0}";
        sign.Modulate = taken ? ContractColor : yours ? new Color(1f, 1f, 1f, 0.75f) : new Color(0.82f, 0.82f, 0.78f, 0.55f);
        if (taken) Outline(f).Visible = true;
        else if (_outlines.TryGetValue(f, out var outline)) outline.Visible = false;
    }

    /// <summary>A band on the ground along the field's edge, built the first time the farm takes a contract on it.</summary>
    private MeshInstance3D Outline(FieldInfo f)
    {
        if (_outlines.TryGetValue(f, out var outline)) return outline;
        const float halfWidth = 0.25f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetNormal(Vector3.Up);
        var points = f.Shape.Points;
        for (var k = 0; k < points.Count; k++)
        {
            var a = points[k];
            var edge = points[(k + 1) % points.Count] - a;
            if (edge.LengthSquared() < 1e-4f) continue;
            var side = NVec2.Normalize(new NVec2(-edge.Y, edge.X)) * halfWidth;
            // A quad per meter, so the band follows the ground.
            var steps = Math.Max(1, (int)MathF.Ceiling(edge.Length()));
            for (var i = 0; i < steps; i++)
            {
                var p = a + edge * i / steps;
                var q = a + edge * (i + 1) / steps;
                Vector3[] quad = [Ground(p - side), Ground(p + side), Ground(q + side), Ground(q - side)];
                foreach (var v in (ReadOnlySpan<int>)[0, 1, 2, 0, 2, 3]) st.AddVertex(quad[v]);
            }
        }
        outline = new MeshInstance3D
        {
            Name = $"Contract_{f.Id}",
            Mesh = st.Commit(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = ContractColor, Roughness = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(outline);
        return _outlines[f] = outline;
    }

    private Vector3 Ground(NVec2 p) => Sim.World.OnGround(p, 0.1f);
}
