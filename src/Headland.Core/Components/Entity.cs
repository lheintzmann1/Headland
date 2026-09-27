using System.Numerics;
using System.Text.Json;
using Headland.Core.Content;
using Headland.Core.Ownership;

namespace Headland.Core.Components;

/// <summary>
/// A thing on the map built from components: a machine, a POI (later the farmer, pallets, animals…). Its type
/// (<see cref="Def"/>) lists the components; each keeps its part of the state (<see cref="Get{T}"/>).
/// </summary>
public abstract class Entity : IOwnable
{
    /// <summary>Its type.</summary>
    public abstract EntityDef Def { get; }

    public virtual string Name => Def.Name;

    /// <summary>Owning farm (<see cref="Farm.None"/> = an NPC's): only its members drive a machine or hitch to it.</summary>
    public int FarmId { get; set; }

    /// <summary>
    /// Its origin on the ground (a machine's is where it turns about, the middle of its fixed axles; a POI's the
    /// center of its footprint), and the heading of its front (+Z).
    /// </summary>
    public Vector2 Position { get; set; }
    public float Heading { get; set; }

    public IReadOnlyList<Component> Components { get; protected set; } = [];

    /// <summary>Its component of type <typeparamref name="T"/> (or implementing it), if it has one.</summary>
    public T? Get<T>() where T : class
    {
        foreach (var c in Components)
            if (c is T t) return t;
        return null;
    }

    public bool Has<T>() where T : class => Get<T>() != null;

    /// <summary>Its fill units (none without a fillUnits component).</summary>
    public IReadOnlyList<FillUnit> FillUnits => Get<FillUnits>()?.Units ?? [];

    public FillUnit? Unit(string? id) => Get<FillUnits>()?.Unit(id);

    public Vector2 LocalToWorld(Vector2 local) => MathUtil.LocalToWorld(Position, Heading, local);
    public Vector2 LocalToWorld(float x, float z) => LocalToWorld(new Vector2(x, z));

    /// <summary>Builds its components from its type.</summary>
    protected void CreateComponents() => Components = Def.Components.Select(c => c.Create(this)).ToArray();

    /// <summary>What each of its components keeps, by kind (those keeping nothing are left out).</summary>
    internal Dictionary<string, JsonElement> SaveComponents(ContentDatabase content)
    {
        var states = new Dictionary<string, JsonElement>();
        foreach (var c in Components)
            if (c.SaveState(content) is { } state)
                states[c.Definition.Kind] = state;
        return states;
    }

    /// <summary>
    /// Gives each component what <see cref="SaveComponents"/> kept for its kind. A kind it no longer has loses its
    /// state; one it didn't have starts afresh, as does one whose state can't be read (with a warning).
    /// </summary>
    internal void LoadComponents(IReadOnlyDictionary<string, JsonElement> states, SaveContext context)
    {
        foreach (var c in Components)
        {
            if (!states.TryGetValue(c.Definition.Kind, out var state)) continue;
            try
            {
                c.LoadState(state, context);
            }
            catch (JsonException)
            {
                context.Warnings.Add($"The {c.Definition.Kind} of {Name} could not be read: it starts afresh");
            }
        }
    }
}
