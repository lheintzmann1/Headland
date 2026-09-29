using System.Text.Json;
using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Saves;

namespace Headland.Core.Components;

// Everything on the map is built from components (FS: specializations): a machine from running gear, a motor, fill
// units…, a POI from what it does. Each kind has its def (under "components" in its thing's JSON), its runtime
// component with the state it keeps and saves, and a view in the game. Kinds are registered in ComponentKinds; each
// checks that it goes on the thing it's on (a motor only on a machine).

/// <summary>A component of an entity type (a machine, a POI), as its JSON gives it under <c>components</c>.</summary>
public abstract class ComponentDef
{
    /// <summary>Its kind: its key under <c>components</c>.</summary>
    [JsonIgnore]
    public string Kind => ComponentKinds.NameOf(GetType());

    /// <summary>Model node roles its view moves: the keys it allows in the entity's <c>visual.nodes</c>.</summary>
    [JsonIgnore]
    public virtual IEnumerable<string> Roles => [];

    /// <summary>
    /// What the key hints say for the toggles it offers, in its own words (FS: an action's texts), by the key's action:
    /// <c>{ "lower": ["Lower boom", "Lift boom"] }</c>, to do it and to undo it.
    /// </summary>
    public Dictionary<string, string[]> Words { get; set; } = new();

    /// <summary>The keys it offers a toggle on (<see cref="Input.ActionList.Toggle"/>): those its <see cref="Words"/> can name.</summary>
    [JsonIgnore]
    public virtual IEnumerable<string> Toggles => [];

    /// <summary>What <paramref name="action"/>'s hint says to do it and to undo it: its JSON's words, else these.</summary>
    public (string Do, string Undo) WordsFor(string action, string @do, string undo) =>
        Words.GetValueOrDefault(action) is [var d, var u] ? (d, u) : (@do, undo);

    /// <summary>What's wrong with its <see cref="Words"/>.</summary>
    internal IEnumerable<string> WordErrors()
    {
        var toggles = Toggles.ToList();
        foreach (var (action, words) in Words)
        {
            if (!toggles.Contains(action))
                yield return toggles.Count == 0 ? $"words: it offers no key to name ('{action}')"
                    : $"words: '{action}' is not one of its keys ({string.Join(", ", toggles)})";
            else if (words is not [var d, var u] || string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(u))
                yield return $"words: '{action}' needs two texts, to do it and to undo it";
        }
    }

    /// <summary>What's wrong with it, checked against the rest of what it's on (and whether it goes on that at all).</summary>
    internal virtual IEnumerable<string> Errors(EntityDef owner, ContentDatabase content) => [];

    /// <summary>Once read: finds the content it names by id (a joint's type), leaving what's missing for <see cref="Errors"/>.</summary>
    internal virtual void Link(ContentDatabase content)
    {
    }

    internal abstract Component Create(Entity owner);

    /// <summary>A fill unit of the entity's <c>fillUnits</c>, by id.</summary>
    protected static bool HasUnit(EntityDef owner, string? id) =>
        id != null && owner.Get<FillUnitsDef>()?.Units.Any(u => u.Id == id) == true;
}

/// <summary>A component of one entity: its runtime state, what it does each tick, and what it saves.</summary>
public abstract class Component(Entity owner)
{
    /// <summary>What it's part of: a machine, a POI.</summary>
    public Entity Owner { get; } = owner;

    public abstract ComponentDef Definition { get; }

    /// <summary>Every tick, once the vehicles have moved: animations, transfers.</summary>
    internal virtual void Update(Simulation sim, float dt)
    {
    }

    /// <summary>Its entity was just hitched to another (a machine to a vehicle).</summary>
    internal virtual void OnHitched()
    {
    }

    /// <summary>Its entity was just unhitched.</summary>
    internal virtual void OnDetached()
    {
    }

    /// <summary>The state to save, or null when it keeps none.</summary>
    internal virtual JsonElement? SaveState(ContentDatabase content) => null;

    /// <summary>Restores what <see cref="SaveState"/> wrote, once its entity is as it was (a machine hitched).</summary>
    internal virtual void LoadState(JsonElement data, SaveContext context)
    {
    }
}

public abstract class Component<TDef>(Entity owner, TDef def) : Component(owner) where TDef : ComponentDef
{
    public TDef Def { get; } = def;
    public override ComponentDef Definition => Def;
}

/// <summary>A component keeping state in saves, as a <typeparamref name="TSave"/> (missing properties load as defaults).</summary>
public abstract class Component<TDef, TSave>(Entity owner, TDef def) : Component<TDef>(owner, def)
    where TDef : ComponentDef where TSave : class, new()
{
    protected abstract TSave Capture(ContentDatabase content);

    protected abstract void Restore(TSave save, SaveContext context);

    internal sealed override JsonElement? SaveState(ContentDatabase content) => JsonSerializer.SerializeToElement(Capture(content), SaveGame.Json);

    internal sealed override void LoadState(JsonElement data, SaveContext context) =>
        Restore(data.Deserialize<TSave>(SaveGame.Json) ?? new TSave(), context);
}

/// <summary>What a component restoring from a save may need: the content, and where to say what couldn't be restored.</summary>
public sealed record SaveContext(ContentDatabase Content, List<string> Warnings);
