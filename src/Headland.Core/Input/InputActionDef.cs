namespace Headland.Core.Input;

/// <summary>
/// Where an action works. The player is in one situation at a time (<see cref="Situations"/>): walking about, driving,
/// or in a menu; <see cref="World"/> actions (the camera, time) work both walking and driving.
/// </summary>
[Flags]
public enum InputContext
{
    None = 0,
    World = 1,
    OnFoot = 2,
    Vehicle = 4,
    Menu = 8,
}

public static class InputContexts
{
    /// <summary>The situations the player can be in: each is a set of contexts active together.</summary>
    public static readonly InputContext[] Situations =
    [
        InputContext.World | InputContext.OnFoot,
        InputContext.World | InputContext.Vehicle,
        InputContext.Menu,
    ];

    /// <summary>Whether two sets of contexts can be active at once, so that one input can't serve both.</summary>
    public static bool Overlap(InputContext a, InputContext b) => Situations.Any(s => (a & s) != 0 && (b & s) != 0);
}

/// <summary>
/// An action the player can bind: its id, what it does (for the help and the controls page), where it works and the
/// inputs it comes on. An analog action (moving, braking) reads how far its inputs are pushed, whatever the modifiers;
/// the others fire as their bindings' triggers say.
/// </summary>
public sealed class InputActionDef(string id, string help, InputContext contexts, params string[] defaults)
{
    public string Id { get; } = id;
    public string Help { get; } = help;
    public InputContext Contexts { get; } = contexts;
    public IReadOnlyList<InputBinding> Defaults { get; } = defaults.Select(InputBinding.Parse).ToArray();
    public bool Analog { get; init; }
    /// <summary>What the player is told when it does nothing now ("No implement to lower").</summary>
    public string? Unavailable { get; init; }
    /// <summary>
    /// What its hint says when tools with words of their own switch together on it ("Lower all", "Raise all"): to do it
    /// and to undo it.
    /// </summary>
    public (string Do, string Undo)? Several { get; init; }

    public override string ToString() => Id;
}
