namespace Headland.Core.Input;

/// <summary>A binding shared by two actions that can be active at once.</summary>
public sealed record InputConflict(string Action, string Other, InputBinding Binding);

/// <summary>
/// Every bindable action (the simulation's, the game's, later the mods') and the inputs each is bound to: its defaults
/// until the player rebinds it. Two actions active in the same situation can't share a binding (see
/// <see cref="Conflicts"/>).
/// </summary>
public sealed class Bindings
{
    private readonly List<InputActionDef> _actions = [];
    private readonly Dictionary<string, InputActionDef> _byId = new();
    private readonly Dictionary<string, IReadOnlyList<InputBinding>> _bindings = new();

    public Bindings(IEnumerable<InputActionDef> actions)
    {
        foreach (var a in actions) Add(a);
    }

    /// <summary>In the order they were added: the order the help lists them.</summary>
    public IReadOnlyList<InputActionDef> Actions => _actions;

    public void Add(InputActionDef action)
    {
        if (!_byId.TryAdd(action.Id, action)) throw new ArgumentException($"input action '{action.Id}' is defined twice");
        _actions.Add(action);
        _bindings[action.Id] = action.Defaults;
    }

    public InputActionDef? Def(string action) => _byId.GetValueOrDefault(action);

    /// <summary>What <paramref name="action"/> is bound to now (nothing for an unknown one).</summary>
    public IReadOnlyList<InputBinding> Of(string action) => _bindings.GetValueOrDefault(action) ?? [];

    public void Set(string action, IEnumerable<InputBinding> bindings)
    {
        if (!_byId.ContainsKey(action)) throw new ArgumentException($"unknown input action '{action}'");
        _bindings[action] = bindings.Distinct().ToArray();
    }

    public void Reset(string action) => Set(action, _byId[action].Defaults);

    /// <summary>
    /// The actions <paramref name="binding"/> would clash with if <paramref name="action"/> had it: those active in
    /// a same situation with a binding on its input that fires the same way. An analog action reads its input whatever
    /// the modifiers and the trigger, so it clashes with anything else on that input; the others clash on the same
    /// modifiers and trigger (a press and a hold on one key are told apart by time).
    /// </summary>
    public IEnumerable<string> Conflicts(string action, InputBinding binding)
    {
        var def = _byId[action];
        foreach (var other in _actions)
        {
            if (other == def || !InputContexts.Overlap(def.Contexts, other.Contexts)) continue;
            if (Of(other.Id).Any(b => Clash(def, binding, other, b))) yield return other.Id;
        }
    }

    /// <summary>Every binding two actions share, each pair once.</summary>
    public IEnumerable<InputConflict> AllConflicts()
    {
        for (var i = 0; i < _actions.Count; i++)
        foreach (var b in Of(_actions[i].Id))
        foreach (var other in Conflicts(_actions[i].Id, b))
            if (_actions.FindIndex(a => a.Id == other) > i)
                yield return new InputConflict(_actions[i].Id, other, b);
    }

    private static bool Clash(InputActionDef a, InputBinding x, InputActionDef b, InputBinding y) =>
        x.Input == y.Input && (a.Analog || b.Analog || x.Modifiers == y.Modifiers && x.Trigger == y.Trigger);
}
