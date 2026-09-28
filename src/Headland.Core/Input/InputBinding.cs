namespace Headland.Core.Input;

/// <summary>Modifier keys held with a binding's input.</summary>
[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>How a binding's input fires an action (analog actions ignore it: they read the input's strength).</summary>
public enum InputTrigger
{
    /// <summary>On the press (or on a quick release, when the key also has a hold, or once no second tap came).</summary>
    Press,
    /// <summary>Held down for <see cref="InputRouter.HoldSeconds"/>.</summary>
    Hold,
    /// <summary>Pressed twice within <see cref="InputRouter.DoubleTapSeconds"/>.</summary>
    DoubleTap,
}

public enum InputDevice
{
    Keyboard,
    Mouse,
    Gamepad,
}

/// <summary>
/// An input an action is bound to: a key (by its position on a US QWERTY keyboard, as Godot names it: "W", "Tab",
/// "F1") with modifiers, a mouse button or axis, or a gamepad button or axis; and how it fires the action. Its text
/// form, as the settings file holds it: <c>[Hold |Double ][Ctrl+][Shift+][Alt+]input</c> ("Shift+Tab", "Hold V",
/// "Mouse Wheel Up", "Joy A", "Joy LX-").
/// </summary>
public readonly record struct InputBinding(string Input, Modifiers Modifiers = Modifiers.None, InputTrigger Trigger = InputTrigger.Press)
{
    private const string HoldPrefix = "Hold ";
    private const string DoublePrefix = "Double ";

    /// <summary>Mouse buttons, and the mouse's motion along X and Y (in pixels: + right and down).</summary>
    public static readonly string[] MouseInputs =
    [
        "Mouse Left", "Mouse Right", "Mouse Middle", "Mouse Wheel Up", "Mouse Wheel Down", "Mouse Back", "Mouse Forward",
        "Mouse X-", "Mouse X+", "Mouse Y-", "Mouse Y+",
    ];

    /// <summary>
    /// Gamepad buttons (Xbox names; Up… are the d-pad), the sticks' axes (Y+ is down) and the triggers.
    /// </summary>
    public static readonly string[] GamepadInputs =
    [
        "Joy A", "Joy B", "Joy X", "Joy Y", "Joy LB", "Joy RB", "Joy Back", "Joy Start", "Joy Guide", "Joy L3", "Joy R3",
        "Joy Up", "Joy Down", "Joy Left", "Joy Right",
        "Joy LX-", "Joy LX+", "Joy LY-", "Joy LY+", "Joy RX-", "Joy RX+", "Joy RY-", "Joy RY+", "Joy LT", "Joy RT",
    ];

    public InputDevice Device => DeviceOf(Input);

    public static InputDevice DeviceOf(string input) =>
        input.StartsWith("Mouse ", StringComparison.Ordinal) ? InputDevice.Mouse
        : input.StartsWith("Joy ", StringComparison.Ordinal) ? InputDevice.Gamepad
        : InputDevice.Keyboard;

    /// <summary>An axis (a stick's direction, a trigger, the mouse's motion) rather than a key or button.</summary>
    public static bool IsAxis(string input) =>
        input.EndsWith('-') || input.EndsWith('+') ? DeviceOf(input) != InputDevice.Keyboard : input is "Joy LT" or "Joy RT";

    /// <summary>Reads the text form; throws <see cref="FormatException"/> with what's wrong.</summary>
    public static InputBinding Parse(string text) =>
        TryParse(text, out var binding, out var error) ? binding : throw new FormatException(error);

    /// <summary>
    /// Reads the text form. Mouse and gamepad names are checked (and their case fixed); a key's name is left to the
    /// game, which knows the keyboard.
    /// </summary>
    public static bool TryParse(string text, out InputBinding binding, out string? error)
    {
        binding = default;
        var rest = text.Trim();
        var trigger = InputTrigger.Press;
        if (rest.StartsWith(HoldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            trigger = InputTrigger.Hold;
            rest = rest[HoldPrefix.Length..].TrimStart();
        }
        else if (rest.StartsWith(DoublePrefix, StringComparison.OrdinalIgnoreCase))
        {
            trigger = InputTrigger.DoubleTap;
            rest = rest[DoublePrefix.Length..].TrimStart();
        }
        var modifiers = Modifiers.None;
        while (TakeModifier(ref rest, out var m)) modifiers |= m;
        if (rest.Length == 0)
        {
            error = $"'{text}' names no input";
            return false;
        }
        var input = rest;
        if (DeviceOf(Canonical(input) ?? input) is var device and not InputDevice.Keyboard)
        {
            input = Canonical(input) ?? "";
            if (input.Length == 0)
            {
                error = $"'{rest}' is not a {(device == InputDevice.Mouse ? "mouse" : "gamepad")} input " +
                        $"({string.Join(", ", device == InputDevice.Mouse ? MouseInputs : GamepadInputs)})";
                return false;
            }
            if (device == InputDevice.Gamepad && modifiers != Modifiers.None)
            {
                error = $"'{text}': Ctrl, Shift and Alt go with keys and mouse buttons";
                return false;
            }
        }
        binding = new InputBinding(input, modifiers, trigger);
        error = null;
        return true;
    }

    /// <summary>A mouse or gamepad input's name as listed, whatever its case; null when it isn't one.</summary>
    private static string? Canonical(string input) =>
        MouseInputs.Concat(GamepadInputs).FirstOrDefault(i => string.Equals(i, input, StringComparison.OrdinalIgnoreCase));

    private static bool TakeModifier(ref string rest, out Modifiers modifier)
    {
        foreach (var (name, m) in new[] { ("Ctrl+", Modifiers.Ctrl), ("Shift+", Modifiers.Shift), ("Alt+", Modifiers.Alt) })
        {
            if (!rest.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            rest = rest[name.Length..];
            modifier = m;
            return true;
        }
        modifier = Modifiers.None;
        return false;
    }

    /// <summary>The modifiers as a prefix: "Ctrl+Shift+".</summary>
    public static string Prefix(Modifiers modifiers) =>
        (modifiers.HasFlag(Modifiers.Ctrl) ? "Ctrl+" : "") + (modifiers.HasFlag(Modifiers.Shift) ? "Shift+" : "") +
        (modifiers.HasFlag(Modifiers.Alt) ? "Alt+" : "");

    public static string Prefix(InputTrigger trigger) => trigger switch
    {
        InputTrigger.Hold => HoldPrefix,
        InputTrigger.DoubleTap => DoublePrefix,
        _ => "",
    };

    public override string ToString() => Prefix(Trigger) + Prefix(Modifiers) + Input;
}
