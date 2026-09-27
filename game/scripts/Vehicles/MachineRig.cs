using Headland.Core.Content;
using Godot;

namespace Headland.Game.Vehicles;

/// <summary>A node a component view moves, with its rest pose (as modeled or built).</summary>
public readonly record struct RigPart(Node3D Node, Vector3 Position, Vector3 Rotation, Vector3 Scale);

/// <summary>
/// How a machine is drawn: procedural placeholder parts or a glTF model, and the nodes its component views move, by
/// role (wheel0, pipe, tipper…). Views animate a part from its rest pose, so placeholders and models move alike.
/// </summary>
public sealed class MachineRig
{
    private readonly Dictionary<string, RigPart> _parts = new();

    public Node3D Root { get; } = new() { Name = "Visual" };
    /// <summary>Built from placeholder parts: views add their own parts, and recolor loads by fill type.</summary>
    public bool IsPlaceholder { get; init; }
    /// <summary>Meters per unit of the parts' space (a model's scale), for moves given in meters.</summary>
    public float Scale { get; init; } = 1f;

    public void Add(string role, Node3D node) => _parts[role] = new RigPart(node, node.Position, node.Rotation, node.Scale);

    public RigPart? Part(string role) => _parts.TryGetValue(role, out var p) ? p : null;

    /// <summary>The machine's glTF model with its parts mapped by visual.nodes, or null to use the placeholder.</summary>
    public static MachineRig? Model(MachineDef def)
    {
        var v = def.Visual;
        if (string.IsNullOrEmpty(v.Model)) return null;
        if (!ResourceLoader.Exists(v.Model))
        {
            GD.PushWarning($"{def.Id}: model '{v.Model}' not found (was it imported?); using the placeholder");
            return null;
        }
        var rig = new MachineRig { Scale = v.Scale };
        var holder = new Node3D
        {
            Name = "Model",
            Position = v.Offset.Length == 3 ? new Vector3(v.Offset[0], v.Offset[1], v.Offset[2]) : Vector3.Zero,
            Rotation = new Vector3(0f, Mathf.DegToRad(v.YawDeg), 0f),
            Scale = Vector3.One * v.Scale,
        };
        rig.Root.AddChild(holder);
        var instance = GD.Load<PackedScene>(v.Model).Instantiate();
        holder.AddChild(instance);

        foreach (var (role, nodeName) in v.Nodes ?? new Dictionary<string, string>())
        {
            if (instance.FindChild(nodeName, recursive: true, owned: false) is Node3D node) rig.Add(role, node);
            else GD.PushWarning($"{def.Id}: model has no node '{nodeName}' for '{role}'");
        }
        GD.Print($"{def.Id}: model {v.Model} (parts: {(rig._parts.Count > 0 ? string.Join(", ", rig._parts.Keys) : "none")})");
        return rig;
    }
}
