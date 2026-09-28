using System.Numerics;

namespace Headland.Core.Input;

/// <summary>
/// Turns inputs going down and up (keys, buttons, axes) into the actions they fire in the current
/// <see cref="Context"/>, and says how far analog actions are pushed. Of the bindings on an input whose modifiers are
/// all held, the one with the most modifiers wins, so Shift+Tab isn't also Tab, and a key still fires while running with
/// Shift. A key with a hold binding fires its press on a quick release instead; a key with a double tap waits a moment
/// for the second tap before firing its press.
/// </summary>
public sealed class InputRouter(Bindings bindings)
{
    public const double HoldSeconds = 0.4;
    public const double DoubleTapSeconds = 0.3;
    /// <summary>Stick and trigger travel ignored at rest; the rest is rescaled to 0..1.</summary>
    public const float DeadZone = 0.2f;
    /// <summary>An axis pushed past this acts as a pressed button, released again below <see cref="ReleaseAt"/>.</summary>
    public const float PressAt = 0.5f;
    public const float ReleaseAt = 0.4f;

    private readonly HashSet<string> _down = [];
    private readonly Dictionary<string, float> _axes = new();
    private readonly List<Pending> _pending = [];
    private readonly List<Pending> _taps = [];
    private readonly List<string> _fired = [];
    private InputContext _context;

    /// <summary>The bindings of one input with the same modifiers: what a press of it may fire.</summary>
    private sealed class Pending(string input, List<(string action, InputTrigger trigger)> group, double time)
    {
        public string Input { get; } = input;
        public List<(string action, InputTrigger trigger)> Group { get; } = group;
        public double Time { get; set; } = time;
        /// <summary>Its hold fired, or it was the second tap: its release fires nothing.</summary>
        public bool Done { get; set; }

        public bool Has(InputTrigger trigger) => Group.Any(g => g.trigger == trigger);

        public IEnumerable<string> Actions(InputTrigger trigger) => Group.Where(g => g.trigger == trigger).Select(g => g.action);

        public bool Same(Pending other) => Group.SequenceEqual(other.Group);
    }

    public Bindings Bindings { get; } = bindings;

    /// <summary>The situation the player is in (one of <see cref="InputContexts.Situations"/>); None ignores every input.</summary>
    public InputContext Context
    {
        get => _context;
        set
        {
            if (value == _context) return;
            _context = value;
            _pending.Clear();
            _taps.Clear();
        }
    }

    /// <summary>Keys and buttons down now (axes pushed past half way included).</summary>
    public IReadOnlyCollection<string> Down => _down;

    public bool InContext(string action) => Bindings.Def(action) is { } def && (def.Contexts & Context) != 0;

    /// <summary>A key or button went down with <paramref name="modifiers"/> held, at <paramref name="time"/> seconds.</summary>
    public void Press(string input, Modifiers modifiers, double time)
    {
        if (!_down.Add(input)) return;
        var group = Candidates(input, modifiers);
        if (group.Count == 0) return;
        var press = new Pending(input, group, time);
        if (!press.Has(InputTrigger.Hold) && !press.Has(InputTrigger.DoubleTap))
        {
            _fired.AddRange(press.Actions(InputTrigger.Press));
            return;
        }
        if (_taps.Find(t => t.Same(press) && time - t.Time <= DoubleTapSeconds) is { } first)
        {
            _taps.Remove(first);
            _fired.AddRange(press.Actions(InputTrigger.DoubleTap));
            press.Done = true;
        }
        _pending.Add(press);
    }

    public void Release(string input, double time)
    {
        if (!_down.Remove(input)) return;
        foreach (var p in _pending.Where(p => p.Input == input).ToList())
        {
            _pending.Remove(p);
            if (p.Done) continue;
            if (p.Has(InputTrigger.DoubleTap))
            {
                p.Time = time;
                _taps.Add(p);
            }
            else _fired.AddRange(p.Actions(InputTrigger.Press));
        }
    }

    /// <summary>
    /// An axis moved: a stick's ("Joy LX", -1..1), a trigger's ("Joy LT", 0..1) or the mouse's ("Mouse X", pixels,
    /// added up until <see cref="EndFrame"/>). A stick or trigger pushed past <see cref="PressAt"/> presses its input.
    /// </summary>
    public void Axis(string axis, float value, double time)
    {
        if (axis.StartsWith("Mouse ", StringComparison.Ordinal))
        {
            _axes[axis + "+"] = _axes.GetValueOrDefault(axis + "+") + MathF.Max(value, 0f);
            _axes[axis + "-"] = _axes.GetValueOrDefault(axis + "-") + MathF.Max(-value, 0f);
            return;
        }
        if (axis is "Joy LT" or "Joy RT") Set(axis, value, time);
        else
        {
            Set(axis + "+", MathF.Max(value, 0f), time);
            Set(axis + "-", MathF.Max(-value, 0f), time);
        }
    }

    private void Set(string input, float value, double time)
    {
        _axes[input] = value;
        if (value >= PressAt) Press(input, Modifiers.None, time);
        else if (value < ReleaseAt) Release(input, time);
    }

    /// <summary>The mouse's motion was used: it adds up from zero again.</summary>
    public void EndFrame()
    {
        foreach (var axis in _axes.Keys.Where(a => a.StartsWith("Mouse ", StringComparison.Ordinal)).ToList()) _axes[axis] = 0f;
    }

    /// <summary>Everything lets go at once (the window lost focus): nothing fires.</summary>
    public void ReleaseAll()
    {
        _down.Clear();
        _axes.Clear();
        _pending.Clear();
        _taps.Clear();
    }

    /// <summary>The actions fired since the last poll, with the holds and single taps that came due by <paramref name="time"/>.</summary>
    public IReadOnlyList<string> Poll(double time)
    {
        foreach (var p in _pending.Where(p => !p.Done && p.Has(InputTrigger.Hold) && time - p.Time >= HoldSeconds))
        {
            _fired.AddRange(p.Actions(InputTrigger.Hold));
            p.Done = true;
        }
        foreach (var t in _taps.Where(t => time - t.Time > DoubleTapSeconds).ToList())
        {
            _taps.Remove(t);
            _fired.AddRange(t.Actions(InputTrigger.Press));
        }
        var fired = _fired.ToArray();
        _fired.Clear();
        return fired;
    }

    /// <summary>
    /// How far an analog action is pushed, 0..1 (keys and buttons 1 while down, sticks past the dead zone), or the
    /// mouse's motion in pixels this frame; 0 outside its context.
    /// </summary>
    public float Strength(string action)
    {
        if (!InContext(action)) return 0f;
        var strength = 0f;
        foreach (var b in Bindings.Of(action))
            strength = MathF.Max(strength, StrengthOf(b.Input));
        return strength;
    }

    /// <summary>An analog action pushed past half way.</summary>
    public bool Held(string action) => Strength(action) >= 0.5f;

    private float StrengthOf(string input)
    {
        if (!InputBinding.IsAxis(input)) return _down.Contains(input) ? 1f : 0f;
        var value = _axes.GetValueOrDefault(input);
        if (InputBinding.DeviceOf(input) == InputDevice.Mouse) return value;
        return value <= DeadZone ? 0f : MathF.Min(1f, (value - DeadZone) / (1f - DeadZone));
    }

    /// <summary>The non-analog bindings in context on <paramref name="input"/> with the most of the held modifiers.</summary>
    private List<(string action, InputTrigger trigger)> Candidates(string input, Modifiers held)
    {
        var best = -1;
        var group = new List<(string, InputTrigger)>();
        foreach (var def in Bindings.Actions)
        {
            if (def.Analog || (def.Contexts & Context) == 0) continue;
            foreach (var b in Bindings.Of(def.Id))
            {
                if (b.Input != input || (b.Modifiers & ~held) != 0) continue;
                var count = BitOperations.PopCount((uint)b.Modifiers);
                if (count < best) continue;
                if (count > best) group.Clear();
                best = count;
                group.Add((def.Id, b.Trigger));
            }
        }
        return group;
    }
}
