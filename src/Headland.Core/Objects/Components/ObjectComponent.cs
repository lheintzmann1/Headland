using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Objects.Components;

// The kinds of components only objects have (bales; later pallets): see Components/Component.cs.

/// <summary>A kind of component that goes on objects only.</summary>
public abstract class ObjectComponentDef : ComponentDef
{
    internal sealed override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content) =>
        owner is ObjectDef o ? Errors(o, content) : ["goes on objects only"];

    /// <summary>What's wrong with it, checked against the rest of its object.</summary>
    internal virtual IEnumerable<string> Errors(ObjectDef o, ContentDatabase content) => [];

    internal sealed override Component Create(Entity owner) => Create((WorldObject)owner);

    internal abstract Component Create(WorldObject o);
}

/// <summary>A component of one object.</summary>
public abstract class ObjectComponent<TDef>(WorldObject o, TDef def) : Component<TDef>(o, def) where TDef : ComponentDef
{
    public WorldObject Object { get; } = o;
}
