using Headland.Game.Common;
using Headland.Core.Content;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>A node a component view moves, with its rest pose (as modeled or built).</summary>
public readonly record struct RigPart(Node3D Node, Vector3 Position, Vector3 Rotation, Vector3 Scale);

/// <summary>
/// How a machine is drawn: procedural placeholder parts or a glTF model, and the nodes its component views move, by
/// role (wheel0L, pipe, tipper…). Views animate a part from its rest pose, so placeholders and models move alike.
/// </summary>
public sealed class MachineRig
{
    private readonly Dictionary<string, RigPart> _parts = new();
    private Node3D? _frontFrame;
    private float _hinge;

    public Node3D Root { get; } = new() { Name = "Visual" };
    /// <summary>Built from placeholder parts: views add their own parts, and recolor loads by fill type.</summary>
    public bool IsPlaceholder { get; init; }
    /// <summary>Meters per unit of the parts' space (a model's scale), for moves given in meters.</summary>
    public float Scale { get; init; } = 1f;

    public void Add(string role, Node3D node) => _parts[role] = new RigPart(node, node.Position, node.Rotation, node.Scale);

    public RigPart? Part(string role) => _parts.TryGetValue(role, out var p) ? p : null;

    /// <summary>An articulated placeholder's front frame (role frontFrame), hinged at <paramref name="hinge"/>.</summary>
    public void SetFrontFrame(Node3D frame, float hinge)
    {
        _frontFrame = frame;
        _hinge = hinge;
        Add("frontFrame", frame);
    }

    /// <summary>Adds a placeholder part at <paramref name="position"/> (machine space): on the front frame when ahead of its hinge.</summary>
    public void AddPart(Node3D part, Vector3 position)
    {
        if (_frontFrame != null && position.Z > _hinge)
        {
            _frontFrame.AddChild(part);
            part.Position = position - new Vector3(0f, 0f, _hinge);
            return;
        }
        Root.AddChild(part);
        part.Position = position;
    }

    /// <summary>
    /// The machine's glTF model (what its root node holds) with its moving parts found by name, and the nodes it doesn't
    /// have with its options hidden (docs/MODELING.md). Empty if the model doesn't load: such machines are left out
    /// when the game starts.
    /// </summary>
    public static MachineRig Model(MachineDef def)
    {
        var v = def.Visual;
        var rig = new MachineRig { Scale = v.Scale };
        if (Models.Load(v, def.Id) is not { } holder) return rig;
        rig.Root.AddChild(holder);

        var nodes = new Dictionary<string, Node3D>();
        foreach (var node in Descendants(holder.GetChild(0)))
        {
            var name = node.Name.ToString();
            nodes.TryAdd(name, node);
            if (def.Hides(name)) node.Visible = false;
            if (def.UnknownOption(name) is { } c)
                GD.PushWarning($"{def.Id}: model node '{name}' is named after configuration '{c.Id}' but none of its options " +
                               $"({string.Join(", ", c.Options.Select(o => $"{c.Id}_{o.Id}"))})");
        }
        var missing = new List<string>();
        foreach (var role in def.Roles)
        {
            var name = def.NodeOf(role);
            if (nodes.TryGetValue(name, out var node)) rig.Add(role, node);
            else if (v.Nodes?.ContainsKey(role) == true) GD.PushWarning($"{def.Id}: model has no node '{name}' for '{role}'");
            else missing.Add(role);
        }
        foreach (var name in def.Configurations.SelectMany(c => c.Options).SelectMany(o => o.Show).Distinct().Where(n => !nodes.ContainsKey(n)))
            GD.PushWarning($"{def.Id}: model has no node '{name}' that its options show");
        GD.Print($"{def.Id}: model {v.Model} (parts: {(rig._parts.Count > 0 ? string.Join(", ", rig._parts.Keys) : "none")}" +
                 $"{(missing.Count > 0 ? $"; not in the model: {string.Join(", ", missing)}" : "")})");
        return rig;
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
