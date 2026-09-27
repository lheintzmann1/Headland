using Headland.Core.Content;
using Godot;

namespace Headland.Game.Common;

/// <summary>
/// glTF models of machines and POIs (docs/MODELING.md). The game shows what a model holds under its node named root,
/// with root's origin on its owner's reference point; the rest of the file (reference objects, cameras) is left out.
/// </summary>
public static class Models
{
    /// <summary>The node a model holds its parts under.</summary>
    public const string RootName = "root";

    /// <summary>
    /// Leaves out of the game each machine whose model, as it comes or with one of its options, doesn't load: an error
    /// says so, and what went with it (its places on the maps, its lease sets). There is no stand-in.
    /// </summary>
    public static void LeaveOutBroken(ContentDatabase content)
    {
        var problems = new Dictionary<string, string?>();
        string? ProblemOf(string model) => problems.TryGetValue(model, out var p) ? p : problems[model] = Problem(model);

        foreach (var def in content.Machines.Values.ToList())
        {
            var models = def.Configurations
                .SelectMany(c => c.Options.Select(o => def.Configure(new Dictionary<string, string> { [c.Id] = o.Id })))
                .Prepend(def)
                .Select(d => d.Visual.Model)
                .OfType<string>()
                .Where(m => m != "")
                .Distinct();
            if (models.Select(m => (model: m, problem: ProblemOf(m))).FirstOrDefault(x => x.problem != null) is not (var model, { } problem))
                continue;
            var changes = content.RemoveMachine(def.Id);
            GD.PushError($"{def.Id}: model '{model}' {problem}, so the machine is left out of the game" +
                         (changes.Count > 0 ? $": {string.Join("; ", changes)}" : ""));
        }
    }

    /// <summary>
    /// The model <paramref name="def"/> names, as it goes under its owner: its root node, in a node named Model that
    /// applies the def's offset, yawDeg and scale. Null, with an error naming <paramref name="owner"/>, when it doesn't load.
    /// </summary>
    public static Node3D? Load(ModelDef def, string owner)
    {
        if (string.IsNullOrEmpty(def.Model)) return null;
        var scene = Instantiate(def.Model);
        if (scene == null || FindRoot(scene) is not { } root)
        {
            GD.PushError($"{owner}: model '{def.Model}' {(scene == null ? NotFound : NoRoot)}");
            scene?.Free();
            return null;
        }
        if (root != scene)
        {
            root.GetParent().RemoveChild(root);
            scene.Free();
        }
        // Root's origin is the reference point; its rotation and scale stay.
        root.Position = Vector3.Zero;
        var holder = new Node3D
        {
            Name = "Model",
            Position = def.Offset.Length == 3 ? new Vector3(def.Offset[0], def.Offset[1], def.Offset[2]) : Vector3.Zero,
            Rotation = new Vector3(0f, Mathf.DegToRad(def.YawDeg), 0f),
            Scale = Vector3.One * def.Scale,
        };
        holder.AddChild(root);
        return holder;
    }

    private const string NotFound = "was not found (was it imported?)";
    private const string NoRoot = $"has no '{RootName}' node (see docs/MODELING.md)";

    /// <summary>What's wrong with the model at <paramref name="path"/>, or null when it loads and has its root node.</summary>
    private static string? Problem(string path)
    {
        var scene = Instantiate(path);
        if (scene == null) return NotFound;
        var hasRoot = FindRoot(scene) != null;
        scene.Free();
        return hasRoot ? null : NoRoot;
    }

    private static Node? Instantiate(string path) =>
        ResourceLoader.Exists(path) ? (ResourceLoader.Load(path) as PackedScene)?.Instantiate() : null;

    /// <summary>The model's root node, wherever the exporter put it.</summary>
    private static Node3D? FindRoot(Node scene) =>
        scene.Name == RootName ? scene as Node3D : scene.FindChild(RootName, recursive: true, owned: false) as Node3D;
}
