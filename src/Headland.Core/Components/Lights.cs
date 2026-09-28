using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Components;

public sealed class LampDef
{
    /// <summary>What switches a lamp (FS: the driver's lights, and PlaceableLights by time of day or trigger).</summary>
    public static readonly string[] Switches = ["driver", "dark", "hours", "trigger"];

    /// <summary>Optional, unique on the entity: names the lamp so that a configuration option can add or change it.</summary>
    public string? Id { get; set; }
    /// <summary>A lamp type's id (lamptypes.json): the driver's control it answers to.</summary>
    public string Type { get; set; } = "head";

    /// <summary>Its <see cref="Type"/>, once linked to the content.</summary>
    [JsonIgnore]
    public LampTypeDef? TypeDef { get; internal set; }
    /// <summary>
    /// One of <see cref="Switches"/>: "driver" (by the driver's control its type answers to: the light key, the beacons,
    /// the turn signals; a helper driving at night lights every step), "dark" (a light sensor: at night, or under a sky
    /// darkened by rain, snow or fog), "hours" (a timer:
    /// <see cref="Hours"/>) or "trigger" (while someone is in <see cref="Trigger"/>).
    /// </summary>
    public string Switch { get; set; } = "driver";
    /// <summary>Hours switch: [from, to) game hours it's on (past midnight when from > to).</summary>
    public float[]? Hours { get; set; }
    /// <summary>Trigger switch: the area that switches it on.</summary>
    public TriggerDef? Trigger { get; set; }
    public float X { get; set; }
    public float Y { get; set; } = 1.5f;
    public float Z { get; set; }
    /// <summary>Where it points: tipped down by a negative pitch, turned left by a positive yaw (180 shines backward).</summary>
    public float PitchDeg { get; set; } = -18f;
    public float YawDeg { get; set; }
    /// <summary>Reach (m), cone angle and brightness of its beam.</summary>
    public float Range { get; set; } = 30f;
    public float AngleDeg { get; set; } = 32f;
    public float Energy { get; set; } = 4f;
    public string Color { get; set; } = "#fff0d1";
}

/// <summary>
/// Lamps: a machine's headlights, work lights, beacons and turn signals, switched by its driver (a helper lights them at
/// night); a building's lamps, lit by a light sensor, a timer or someone coming by.
/// </summary>
public sealed class LightsDef : ComponentDef
{
    public LampDef[] Lamps { get; set; } = [];

    internal override void Link(ContentDatabase content)
    {
        foreach (var l in Lamps) l.TypeDef = content.LampTypes.GetValueOrDefault(l.Type);
    }

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        foreach (var l in Lamps.Where(l => !content.LampTypes.ContainsKey(l.Type)))
            yield return $"unknown lamp type '{l.Type}' ({string.Join(", ", content.LampTypes.Keys)})";
        if (Lamps.Any(l => l.Range <= 0f || l.AngleDeg is <= 0f or >= 90f || l.Energy < 0f)) yield return "lamps need range > 0, angleDeg in (0, 90) and energy >= 0";
        foreach (var id in Lamps.Where(l => l.Id != null).GroupBy(l => l.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            yield return $"lamp '{id}' is defined more than once";
        foreach (var l in Lamps)
        {
            var what = l.Id != null ? $"lamp '{l.Id}'" : $"a {l.Type} lamp";
            if (!LampDef.Switches.Contains(l.Switch)) yield return $"{what}: unknown switch '{l.Switch}' ({string.Join(", ", LampDef.Switches)})";
            if (l.Switch == "hours" && (l.Hours == null || MathUtil.HoursError(l.Hours) != null)) yield return $"{what}: the hours switch needs hours: [from, to], 0..24 and different";
            if (l.Switch == "trigger" && l.Trigger == null) yield return $"{what}: the trigger switch needs a trigger";
            else if (l.Trigger?.Error() is { } error) yield return $"{what} trigger: {error}";
            if (l.Hours != null && l.Switch != "hours" || l.Trigger != null && l.Switch != "trigger")
                yield return $"{what}: hours go with the hours switch, a trigger with the trigger switch";
        }
    }

    internal override Component Create(Entity owner) => new Lights(owner, this);
}

/// <summary>What the turn signals do: blink on one side, or both (the hazard lights).</summary>
public enum TurnSignal
{
    Off,
    Left,
    Right,
    Hazards,
}

public sealed class LightsSave
{
    public int Step { get; set; }
    public bool Beacons { get; set; }
    public TurnSignal Signal { get; set; }
}

/// <summary>
/// The driver's light switches and which lamps shine. The driver switches the vehicle's: the light key steps through the
/// lamp types' steps (off, headlights and tail lights, work lights too), with beacon, turn signal and hazard keys; the
/// brake lights shine as it slows down and the reverse lights as it backs up. The implements hanging on it follow.
/// </summary>
public sealed class Lights : Component<LightsDef, LightsSave>, IActionSource
{
    private readonly bool[] _lit;

    public Lights(Entity owner, LightsDef def) : base(owner, def) => _lit = new bool[def.Lamps.Length];

    /// <summary>The light key's step: 0 off, then the lamps of each step on in turn (1 headlights, 2 work lights too…).</summary>
    public int Step { get; set; }
    public bool Beacons { get; set; }
    public TurnSignal Signal { get; set; }

    /// <summary>Whether lamp <paramref name="index"/> (of the def's lamps) shines, as of the last tick.</summary>
    public bool Lit(int index) => _lit[index];

    /// <summary>Its lamps the driver switches with <paramref name="control"/> (see <see cref="LampTypeDef.Controls"/>).</summary>
    public IEnumerable<LampDef> Driven(string control) => Def.Lamps.Where(l => l.Switch == "driver" && l.TypeDef?.Control == control);

    /// <summary>Whose switches its lamps follow: the vehicle's for an implement hanging on one, else its own.</summary>
    private Lights Switches => Owner is Machine { Parent: not null } m && m.Root.Get<Lights>() is { } vehicle ? vehicle : this;

    /// <summary>A vehicle's light keys, for the lamps of its whole chain: those it has lamps for.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Owner is not Machine { Parent: null } vehicle) return;
        var chain = vehicle.Chain().Select(m => m.Get<Lights>()).OfType<Lights>().ToList();
        bool Has(string control) => chain.Any(l => l.Driven(control).Any());

        var types = chain.SelectMany(l => l.Driven("lights")).Select(l => l.TypeDef!).Distinct().ToList();
        if (types.Count > 0)
        {
            var next = types.Select(t => t.Step).Where(s => s > Step).DefaultIfEmpty(0).Min();
            var names = types.Where(t => t.Step == next).Select(t => t.Name).ToList();
            actions.Add(InputActions.Lights, next == 0 ? "Lights off" : $"{Activation.Join(names)} on", () => Step = next);
        }
        if (Has("beacons")) actions.Add(InputActions.Beacons, Beacons ? "Beacons off" : "Beacons on", () => Beacons = !Beacons, hinted: false);
        if (Has("turnLeft")) actions.Add(InputActions.TurnLeft, Signal == TurnSignal.Left ? "Stop signalling" : "Signal left", () => Turn(TurnSignal.Left), hinted: false);
        if (Has("turnRight")) actions.Add(InputActions.TurnRight, Signal == TurnSignal.Right ? "Stop signalling" : "Signal right", () => Turn(TurnSignal.Right), hinted: false);
        if (Has("turnLeft") || Has("turnRight"))
            actions.Add(InputActions.Hazards, Signal == TurnSignal.Hazards ? "Hazard lights off" : "Hazard lights on", () => Turn(TurnSignal.Hazards), hinted: false);
    }

    /// <summary>The driver got out (FS: lights off on leaving): the lights, beacons and turn signals go off, the hazards stay on.</summary>
    public void Leave()
    {
        Step = 0;
        Beacons = false;
        if (Signal != TurnSignal.Hazards) Signal = TurnSignal.Off;
    }

    /// <summary>Switches the turn signals to <paramref name="signal"/>, or off when they already are.</summary>
    private void Turn(TurnSignal signal) => Signal = Signal == signal ? TurnSignal.Off : signal;

    internal override void Update(Simulation sim, float dt)
    {
        var weather = sim.Weather;
        var switches = Switches;
        var step = Owner is Machine m && m.Root.Get<Drivable>()?.Controller is FieldWorkController && weather.Night ? int.MaxValue : switches.Step;
        var motor = (Owner as Machine)?.Root.Get<Motor>();
        var (braking, reversing) = (motor?.Braking == true, motor?.Reversing == true);
        var left = switches.Signal is TurnSignal.Left or TurnSignal.Hazards;
        var right = switches.Signal is TurnSignal.Right or TurnSignal.Hazards;
        for (var i = 0; i < _lit.Length; i++)
        {
            var l = Def.Lamps[i];
            _lit[i] = l.Switch switch
            {
                "driver" => l.TypeDef?.Control switch
                {
                    "lights" => step >= l.TypeDef.Step,
                    "beacons" => switches.Beacons,
                    "turnLeft" => left,
                    "turnRight" => right,
                    "brake" => braking,
                    "reverse" => reversing,
                    _ => false,
                },
                "dark" => weather.Dim,
                "hours" => l.Hours is [var from, var to] && MathUtil.InHours(sim.Clock.HourOfDay, from, to),
                "trigger" => l.Trigger?.Occupied(Owner, sim) == true,
                _ => false,
            };
        }
    }

    protected override LightsSave Capture(ContentDatabase content) => new() { Step = Step, Beacons = Beacons, Signal = Signal };

    protected override void Restore(LightsSave save, SaveContext context)
    {
        Step = Math.Clamp(save.Step, 0, 99);
        Beacons = save.Beacons;
        Signal = Enum.IsDefined(save.Signal) ? save.Signal : TurnSignal.Off;
    }
}
