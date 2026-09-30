using Headland.Core.Input;
using Godot;

namespace Headland.Game.Controls;

/// <summary>
/// What the mouse does now: point (hover, click), or drive what holds it: the camera's pan, or the driver's tool (FS:
/// the mouse's crane control, the right button held).
/// </summary>
public enum MouseMode
{
    Cursor,
    Drag,
    Tool,
}

/// <summary>
/// The game's input: Godot's events turned into named inputs for Core's <see cref="InputRouter"/>, which fires the
/// actions bound to them in the current <see cref="Context"/> (<see cref="Fired"/>) and says how far analog ones are
/// pushed. Keys go by their physical position, so AZERTY players get ZQSD where QWERTY players get WASD;
/// <see cref="Label"/> names them on the player's layout. Godot's own ui_* actions still drive the GUI.
/// </summary>
public partial class InputLayer : Node
{
    private static Bindings _current = GameActions.Catalog();
    private static bool _padLast;
    private readonly Dictionary<string, Key> _keys = new();
    private readonly Dictionary<string, MouseButton> _buttons = new();
    private readonly Dictionary<string, JoyButton> _pad = new();
    private InputRouter _router = null!;

    /// <summary>What each action is bound to (the player's settings).</summary>
    public Bindings Bindings { get; init; } = null!;

    /// <summary>The bindings in use, for labels.</summary>
    public static Bindings Current => _current;

    /// <summary>An action fired (see <see cref="InputRouter"/> for when).</summary>
    public event Action<string>? Fired;

    public InputContext Context
    {
        get => _router.Context;
        set => _router.Context = value;
    }

    public MouseMode MouseMode =>
        _router.Held(GameActions.Pan) ? MouseMode.Drag : _router.Held(InputActions.ToolMouse) ? MouseMode.Tool : MouseMode.Cursor;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        _current = Bindings;
        _router = new InputRouter(Bindings);
        // Before the nodes reading the actions each frame.
        ProcessPriority = -100;
    }

    public float Strength(string action) => _router.Strength(action);

    public bool Held(string action) => _router.Held(action);

    public override void _UnhandledInput(InputEvent e)
    {
        var now = Now;
        switch (e)
        {
            case InputEventKey { Echo: true }:
                return;
            case InputEventKey k:
                var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
                var name = OS.GetKeycodeString(key);
                _padLast = false;
                if (!k.Pressed)
                {
                    _router.Release(name, now);
                    break;
                }
                _keys[name] = key;
                _router.Press(name, ModifiersOf(k, key), now);
                break;
            case InputEventMouseButton mb when MouseName(mb.ButtonIndex) is { } button:
                _padLast = false;
                if (!mb.Pressed)
                {
                    _router.Release(button, now);
                    break;
                }
                _buttons[button] = mb.ButtonIndex;
                _router.Press(button, ModifiersOf(mb, Key.None), now);
                break;
            case InputEventMouseMotion mm:
                _router.Axis("Mouse X", mm.Relative.X, now);
                _router.Axis("Mouse Y", mm.Relative.Y, now);
                break;
            case InputEventJoypadButton jb when PadName(jb.ButtonIndex) is { } pad:
                _padLast = true;
                if (!jb.Pressed)
                {
                    _router.Release(pad, now);
                    break;
                }
                _pad[pad] = jb.ButtonIndex;
                _router.Press(pad, Modifiers.None, now);
                break;
            case InputEventJoypadMotion jm when AxisName(jm.Axis) is { } axis:
                if (MathF.Abs(jm.AxisValue) >= InputRouter.PressAt) _padLast = true;
                _router.Axis(axis, jm.AxisValue, now);
                break;
            default:
                return;
        }
        Fire(now);
    }

    public override void _Process(double delta)
    {
        var now = Now;
        // A release the GUI kept for itself (Space on a focused button) must not leave the input down.
        foreach (var input in _router.Down.Where(i => !StillDown(i)).ToList()) _router.Release(input, now);
        Fire(now);
        // The mouse's motion adds up until every node has read it.
        Callable.From(_router.EndFrame).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) _router?.ReleaseAll();
    }

    private void Fire(double now)
    {
        foreach (var action in _router.Poll(now)) Fired?.Invoke(action);
    }

    private bool StillDown(string input) => InputBinding.DeviceOf(input) switch
    {
        InputDevice.Keyboard => _keys.TryGetValue(input, out var key) && Input.IsPhysicalKeyPressed(key),
        InputDevice.Mouse => _buttons.TryGetValue(input, out var button) && Input.IsMouseButtonPressed(button),
        // Axes pushed past half way are released by the axis itself.
        _ => !_pad.TryGetValue(input, out var pad) || Input.GetConnectedJoypads().Any(d => Input.IsJoyButtonPressed(d, pad)),
    };

    /// <summary>The modifiers held, less the one the key itself is (Shift pressed alone is "Shift", not "Shift+Shift").</summary>
    private static Modifiers ModifiersOf(InputEventWithModifiers e, Key key)
    {
        var m = Modifiers.None;
        if (e.CtrlPressed && key != Key.Ctrl) m |= Modifiers.Ctrl;
        if (e.ShiftPressed && key != Key.Shift) m |= Modifiers.Shift;
        if (e.AltPressed && key != Key.Alt) m |= Modifiers.Alt;
        return m;
    }

    private static string? MouseName(MouseButton button) => button switch
    {
        MouseButton.Left => "Mouse Left",
        MouseButton.Right => "Mouse Right",
        MouseButton.Middle => "Mouse Middle",
        MouseButton.WheelUp => "Mouse Wheel Up",
        MouseButton.WheelDown => "Mouse Wheel Down",
        MouseButton.Xbutton1 => "Mouse Back",
        MouseButton.Xbutton2 => "Mouse Forward",
        _ => null,
    };

    private static string? PadName(JoyButton button) => button switch
    {
        JoyButton.A => "Joy A",
        JoyButton.B => "Joy B",
        JoyButton.X => "Joy X",
        JoyButton.Y => "Joy Y",
        JoyButton.LeftShoulder => "Joy LB",
        JoyButton.RightShoulder => "Joy RB",
        JoyButton.Back => "Joy Back",
        JoyButton.Start => "Joy Start",
        JoyButton.Guide => "Joy Guide",
        JoyButton.LeftStick => "Joy L3",
        JoyButton.RightStick => "Joy R3",
        JoyButton.DpadUp => "Joy Up",
        JoyButton.DpadDown => "Joy Down",
        JoyButton.DpadLeft => "Joy Left",
        JoyButton.DpadRight => "Joy Right",
        _ => null,
    };

    private static string? AxisName(JoyAxis axis) => axis switch
    {
        JoyAxis.LeftX => "Joy LX",
        JoyAxis.LeftY => "Joy LY",
        JoyAxis.RightX => "Joy RX",
        JoyAxis.RightY => "Joy RY",
        JoyAxis.TriggerLeft => "Joy LT",
        JoyAxis.TriggerRight => "Joy RT",
        _ => null,
    };

    // ------------------------------------------------------------------ Names

    /// <summary>
    /// A binding as read from the settings, with its key checked and named as Godot names it; null with
    /// <paramref name="error"/> when the key doesn't exist.
    /// </summary>
    public static InputBinding? Check(InputBinding binding, out string? error)
    {
        error = null;
        if (binding.Device != InputDevice.Keyboard) return binding;
        var key = OS.FindKeycodeFromString(binding.Input);
        if (key == Key.None || ((long)key & (long)KeyModifierMask.ModifierMask) != 0)
        {
            error = $"unknown key '{binding.Input}'";
            return null;
        }
        return binding with { Input = OS.GetKeycodeString(key) };
    }

    /// <summary>
    /// What to press for an action, as the player's keyboard names it ("Z" on AZERTY for move_forward), or its
    /// gamepad button when the pad was used last; "–" when it's unbound.
    /// </summary>
    public static string Label(string action)
    {
        var bindings = _current.Of(action);
        if (bindings.Count == 0) return "–";
        var shown = bindings.FirstOrDefault(b => b.Device == InputDevice.Gamepad == _padLast);
        return Label(shown.Input != null ? shown : bindings[0]);
    }

    public static string Label(InputBinding b) => InputBinding.Prefix(b.Trigger) + InputBinding.Prefix(b.Modifiers) + InputName(b.Input);

    private static string InputName(string input)
    {
        switch (InputBinding.DeviceOf(input))
        {
            case InputDevice.Mouse:
                var button = input["Mouse ".Length..];
                return button switch
                {
                    "Left" or "Right" or "Middle" => $"{button} mouse",
                    "Wheel Up" => "Wheel up",
                    "Wheel Down" => "Wheel down",
                    _ => $"Mouse {button.ToLowerInvariant()}",
                };
            case InputDevice.Gamepad:
                return "Pad " + input["Joy ".Length..];
            default:
                var key = OS.FindKeycodeFromString(input);
                // Without a display (headless runs) there is no layout to translate to.
                if (key == Key.None || DisplayServer.GetName() == "headless") return input;
                var local = DisplayServer.KeyboardGetKeycodeFromPhysical(key);
                return OS.GetKeycodeString(local == Key.None ? key : local);
        }
    }
}
