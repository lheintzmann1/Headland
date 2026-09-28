using System.Text.Json.Serialization;
using Headland.Core.Content;

namespace Headland.Core.Components;

/// <summary>
/// An entity type, from its JSON: what it is, how it looks, and the components it's built from (see
/// <see cref="ComponentKinds"/>). Machines and POIs are entity types.
/// </summary>
public abstract class EntityDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Its components, by kind (see <see cref="ComponentKinds"/>), in the order they run.</summary>
    [JsonConverter(typeof(ComponentDefsConverter))]
    public List<ComponentDef> Components { get; set; } = [];

    /// <summary>Its model (see docs/MODELING.md).</summary>
    public VisualDef Visual { get; set; } = new();

    /// <summary>Links its components to the content they name (joint and lamp types…), once read.</summary>
    internal void Link(ContentDatabase content)
    {
        foreach (var c in Components) c.Link(content);
    }

    /// <summary>Its component def of type <typeparamref name="T"/> (or implementing it), if it has one.</summary>
    public T? Get<T>() where T : class => Components.OfType<T>().FirstOrDefault();

    /// <summary>Model node roles its components move.</summary>
    public IEnumerable<string> Roles => Components.SelectMany(c => c.Roles);

    /// <summary>The model node that moves as <paramref name="role"/>: the one visual.nodes names, else the one named after the role.</summary>
    public string NodeOf(string role) => Visual.Nodes?.GetValueOrDefault(role) ?? role;

    /// <summary>The model nodes that may move as <paramref name="role"/>, the first a model has winning.</summary>
    public virtual IEnumerable<string> NodesOf(string role) => [NodeOf(role)];

    /// <summary>Whether the model node <paramref name="node"/> is hidden on this entity.</summary>
    public virtual bool Hides(string node) => false;
}
