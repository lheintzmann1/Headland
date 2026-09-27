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

    /// <summary>A POI in its local space (+Z its front), drawn by its model (nothing if that doesn't load).</summary>
    private Node3D BuildPoi(Poi poi)
    {
        var root = new Node3D
        {
            Name = poi.Id,
            Position = new Vector3(poi.Position.X, 0f, poi.Position.Y),
            Rotation = new Vector3(0f, poi.Heading, 0f),
        };
        if (Models.Load(poi.Def.Visual, poi.Def.Id) is { } model)
        {
            model.Position += new Vector3(0f, LowestGround(poi.Footprint), 0f);
            root.AddChild(model);
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
