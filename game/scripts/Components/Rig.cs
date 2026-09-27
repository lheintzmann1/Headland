using Headland.Game.Common;
using Headland.Core.Components;
using Headland.Core.Content;
using Godot;

namespace Headland.Game.Components;

/// <summary>A node a component view moves, with its rest pose (as modeled).</summary>
public readonly record struct RigPart(Node3D Node, Vector3 Position, Vector3 Rotation, Vector3 Scale);

/// <summary>
/// How an entity (a machine, a POI) is drawn: its glTF model, and the nodes its component views move, by role
/// (wheel0L, pipe, tipper…). Views animate a part from its rest pose.
/// </summary>
public sealed class Rig
{
    private static readonly Dictionary<(BaseMaterial3D, Color), BaseMaterial3D> Tints = new();
    private readonly Dictionary<string, RigPart> _parts = new();
    private readonly List<(MeshInstance3D mesh, int surface, BaseMaterial3D material)> _fill = [];

    public Node3D Root { get; } = new() { Name = "Visual" };
    /// <summary>Meters per unit of the parts' space (a model's scale), for moves given in meters.</summary>
    public float Scale { get; init; } = 1f;

    public void Add(string role, Node3D node) => _parts[role] = new RigPart(node, node.Position, node.Rotation, node.Scale);

    public RigPart? Part(string role) => _parts.TryGetValue(role, out var p) ? p : null;

    /// <summary>Gives a model's fill materials (the load) the color of what it holds, times their own.</summary>
    public void SetFillColor(Color color)
    {
        foreach (var (mesh, surface, material) in _fill) mesh.SetSurfaceOverrideMaterial(surface, Tinted(material, color));
    }

    /// <summary><paramref name="material"/> with its color multiplied by <paramref name="color"/>, shared by the machines that use it.</summary>
    private static BaseMaterial3D Tinted(BaseMaterial3D material, Color color)
    {
        if (Tints.TryGetValue((material, color), out var tinted)) return tinted;
        tinted = (BaseMaterial3D)material.Duplicate();
        tinted.AlbedoColor = material.AlbedoColor * color;
        return Tints[(material, color)] = tinted;
    }

    /// <summary>
    /// Whether a material is <paramref name="kind"/> (paint, fill) or a shade of it (paint_dark, paint.001, or paint2 as
    /// Godot renames a material named like a node): see docs/MODELING.md.
    /// </summary>
    private static bool IsKind(Material? material, string kind) =>
        material is BaseMaterial3D { ResourceName: var name } && name.StartsWith(kind, StringComparison.Ordinal)
                                                              && (name.Length == kind.Length || name[kind.Length] is '_' or '.' or (>= '0' and <= '9'));

    /// <summary>
    /// The entity's glTF model (what its root node holds) with its moving parts found by name, the nodes a machine
    /// doesn't have with its options hidden, and its paint in the entity's color (docs/MODELING.md). Empty if the model
    /// doesn't load: such machines are left out when the game starts.
    /// </summary>
    public static Rig Model(EntityDef def)
    {
        var v = def.Visual;
        var rig = new Rig { Scale = v.Scale };
        var machine = def as MachineDef;
        if (Models.Load(v, def.Id) is not { } holder) return rig;
        rig.Root.AddChild(holder);

        var paint = Conv.Hex(v.Color);
        var nodes = new Dictionary<string, Node3D>();
        foreach (var node in Descendants(holder.GetChild(0)))
        {
            var name = node.Name.ToString();
            nodes.TryAdd(name, node);
            if (def.Hides(name)) node.Visible = false;
            if (machine?.UnknownOption(name) is { } c)
                GD.PushWarning($"{def.Id}: model node '{name}' is named after configuration '{c.Id}' but none of its options " +
                               $"({string.Join(", ", c.Options.Select(o => $"{c.Id}_{o.Id}"))})");
            if (node is MeshInstance3D mesh) rig.Collect(mesh, paint);
        }
        var missing = new List<string>();
        foreach (var role in def.Roles)
        {
            if (def.NodesOf(role).FirstOrDefault(nodes.ContainsKey) is { } name)
            {
                rig.Add(role, nodes[name]);
                // An option's own version of the part hides the machine's.
                if (name != def.NodeOf(role) && nodes.TryGetValue(def.NodeOf(role), out var replaced)) replaced.Visible = false;
            }
            else if (v.Nodes?.ContainsKey(role) == true) GD.PushWarning($"{def.Id}: model has no node '{def.NodeOf(role)}' for '{role}'");
            else missing.Add(role);
        }
        foreach (var name in (machine?.Configurations ?? []).SelectMany(c => c.Options).SelectMany(o => o.Show).Distinct().Where(n => !nodes.ContainsKey(n)))
            GD.PushWarning($"{def.Id}: model has no node '{name}' that its options show");
        GD.Print($"{def.Id}: model {v.Model} (parts: {(rig._parts.Count > 0 ? string.Join(", ", rig._parts.Keys) : "none")}" +
                 $"{(missing.Count > 0 ? $"; not in the model: {string.Join(", ", missing)}" : "")})");
        return rig;
    }

    /// <summary>Paints a mesh's paint materials in the entity's color, and keeps its fill materials for the load's color.</summary>
    private void Collect(MeshInstance3D mesh, Color paint)
    {
        for (var i = 0; i < (mesh.Mesh?.GetSurfaceCount() ?? 0); i++)
        {
            var material = mesh.GetActiveMaterial(i);
            if (IsKind(material, "paint")) mesh.SetSurfaceOverrideMaterial(i, Tinted((BaseMaterial3D)material, paint));
            else if (IsKind(material, "fill")) _fill.Add((mesh, i, (BaseMaterial3D)material));
        }
    }

    private static IEnumerable<Node3D> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Node3D n3) yield return n3;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
