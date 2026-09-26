using Godot;

namespace Headland.Game.Common;

/// <summary>
/// Registers input actions on physical key positions, so AZERTY users get ZQSD where QWERTY users get WASD.
/// Labels shown in the HUD are translated back to the user's layout with <see cref="Label"/>.
/// A key may carry the Shift modifier (<see cref="Shift"/>). <see cref="Bindings"/> are the defaults; the
/// player's own keys come from the settings file.
/// </summary>
public static class InputSetup
{
    private const Key ShiftMask = (Key)KeyModifierMask.MaskShift;

    private static Key Shift(Key key) => key | ShiftMask;

    public static readonly (string action, Key key, string help)[] Bindings =
    [
        ("move_forward", Key.W, "Move / accelerate"),
        ("move_back", Key.S, "Move back / brake, reverse"),
        ("move_left", Key.A, "Move / steer left"),
        ("move_right", Key.D, "Move / steer right"),
        ("run", Key.Shift, "Run"),
        ("brake", Key.Space, "Handbrake"),
        ("cam_rotate_left", Key.Q, "Rotate camera"),
        ("cam_rotate_right", Key.E, "Rotate camera"),
        ("enter", Key.F, "Enter / exit vehicle"),
        ("next_vehicle", Key.Tab, "Switch to the next vehicle"),
        ("prev_vehicle", Shift(Key.Tab), "Switch to the previous vehicle"),
        ("attach", Key.G, "Attach / detach implement"),
        ("lower", Key.V, "Lower / raise implements"),
        ("turn_on", Key.B, "Turn on / off"),
        ("unload", Key.U, "Pipe / tip trailer"),
        ("cycle_seed", Key.X, "Change seed"),
        ("use", Key.R, "Use a POI: buy, load, refuel, repair, wash"),
        ("helper", Key.H, "Hire / dismiss a field helper"),
        ("time_1", Key.Key1, "Time ×1"),
        ("time_2", Key.Key2, "Time ×5"),
        ("time_3", Key.Key3, "Time ×15"),
        ("time_4", Key.Key4, "Time ×60"),
        ("time_5", Key.Key5, "Time ×120"),
        ("time_6", Key.Key6, "Time ×240"),
        ("pause", Key.P, "Pause time"),
        ("skip_day", Key.F9, "Sleep until tomorrow 6:00"),
        ("quicksave", Key.F5, "Quicksave"),
        ("quickload", Key.F8, "Quickload"),
        ("toggle_help", Key.F1, "Help"),
        ("toggle_finances", Key.F2, "Finances"),
        ("toggle_debug", Key.F3, "Debug overlay"),
        ("screenshot", Key.F12, "Screenshot"),
    ];

    /// <summary>The key each action is registered on (after <see cref="Bindings"/>: statics initialize in order).</summary>
    private static readonly Dictionary<string, Key> Keys = Bindings.ToDictionary(b => b.action, b => b.key);

    /// <summary>Registers every action on <paramref name="keys"/> (action → physical key), defaults for the rest.</summary>
    public static void Register(IReadOnlyDictionary<string, Key>? keys = null)
    {
        foreach (var (action, fallback, _) in Bindings)
        {
            var key = keys?.GetValueOrDefault(action, fallback) ?? fallback;
            Keys[action] = key;
            if (InputMap.HasAction(action)) InputMap.EraseAction(action);
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key & ~ShiftMask, ShiftPressed = (key & ShiftMask) != 0 });
        }
    }

    /// <summary>Key label on the user's keyboard layout for an action (e.g. "Z" on AZERTY for move_forward).</summary>
    public static string Label(string action)
    {
        if (!Keys.TryGetValue(action, out var key)) return "?";
        var local = DisplayServer.KeyboardGetKeycodeFromPhysical(key);
        return OS.GetKeycodeString(local == Key.None ? key : local);
    }
}
