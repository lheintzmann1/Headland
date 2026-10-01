using Headland.Core.Components;
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
    /// <summary>
    /// A top light (FS: isTopLight), such as headlights on the cab's roof: it shines only while an implement on a front
    /// joint switches the vehicle to its top lights; a bottom light, such as those in the hood, only while none does.
    /// </summary>
    public bool TopLight { get; set; }
    public bool BottomLight { get; set; }
    /// <summary>A beacon on whenever someone drives the vehicle, whatever the beacon key (FS: alwaysActive).</summary>
    public bool AlwaysActive { get; set; }
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
            if (l.TopLight && l.BottomLight) yield return $"{what}: a top light or a bottom light, not both";
            if (l.AlwaysActive && l.TypeDef is { Control: not "beacons" }) yield return $"{what}: alwaysActive is for beacons";
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
    /// <summary>The types of lights on (null in saves from before: those of the step).</summary>
    public List<string>? On { get; set; }
    public bool Beacons { get; set; }
    public TurnSignal Signal { get; set; }
}

/// <summary>
/// The driver's light switches and which lamps shine (FS: Lights). The driver switches the vehicle's: the light key steps
/// through the lamp types' steps (off, headlights and tail lights, work lights too) and back, and the types with a key
/// of their own switch alone (high beams, the work lights); with beacon, turn signal and hazard keys. The brake lights
/// shine as it slows down and the reverse lights as it backs up, the cab lights while someone drives, dimmed by day.
/// An implement on a front joint switches it to its top lights. The implements hanging on it follow.
/// </summary>
public sealed class Lights : Component<LightsDef, LightsSave>, IActionSource, IReadoutSource
{
    private readonly bool[] _lit;
    private readonly float[] _level;

    public Lights(Entity owner, LightsDef def) : base(owner, def)
    {
        _lit = new bool[def.Lamps.Length];
        _level = new float[def.Lamps.Length];
    }

    /// <summary>The light key's step: 0 off, then the lamps of each step on in turn (1 headlights, 2 work lights too…).</summary>
    public int Step { get; set; }
    /// <summary>The types of lights on (FS: the lights types mask): those of the step, and those switched alone.</summary>
    public HashSet<string> On { get; } = [];
    public bool Beacons { get; set; }
    public TurnSignal Signal { get; set; }

    /// <summary>The lights the driver switched on: lamps, beacons, a turn signal or the hazards.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (On.Count > 0) yield return new Status("Lights", Tone.Info);
        if (Beacons) yield return new Status("Beacons", Tone.Info);
        if (Signal != TurnSignal.Off) yield return new Status(Signal switch { TurnSignal.Left => "Signal left", TurnSignal.Right => "Signal right", _ => "Hazards" }, Tone.Info);
    }

    /// <summary>Whether lamp <paramref name="index"/> (of the def's lamps) shines, as of the last tick.</summary>
    public bool Lit(int index) => _lit[index];

    /// <summary>How bright lamp <paramref name="index"/> shines, 0 … 1 of its energy: cab lights dim by day.</summary>
    public float Level(int index) => _lit[index] ? _level[index] : 0f;

    /// <summary>
    /// How bright cab lights are at <paramref name="hour"/> (FS: interior lights): off from 10 to 16, fading out from
    /// 8 and in until 18.
    /// </summary>
    public static float CabBrightness(float hour) => Math.Clamp(hour < 10f ? 1f - (hour - 8f) / 2f : hour > 16f ? (hour - 16f) / 2f : 0f, 0f, 1f);

    /// <summary>
    /// Whether an implement on one of <paramref name="vehicle"/>'s joints switches it to its top lights (FS:
    /// requiresTopLights): one that uses them, on a joint that does.
    /// </summary>
    public static bool TopLights(Machine vehicle) =>
        vehicle.Def.Joints.Any(j => j.TopLights && vehicle.Attached.GetValueOrDefault(j.Id)?.Get<Attachable>()?.Def.UseTopLights == true);

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

        var types = chain.SelectMany(l => l.Driven("lights")).Select(l => l.TypeDef!).Distinct().OrderBy(t => t.Step).ToList();
        var steps = types.Select(t => t.Step).Where(s => s > 0).Distinct().Order().ToList();
        string Names(Func<LampTypeDef, bool> which) => Activation.Join(types.Where(which).Select(t => t.Name).ToList());
        if (steps.Count > 0)
        {
            // FS: lights switched alone at step 0 go off with the light key.
            var next = Step == 0 && On.Count > 0 ? 0 : steps.Where(s => s > Step).DefaultIfEmpty(0).Min();
            actions.Add(InputActions.Lights, next == 0 ? "Lights off" : $"{Names(t => t.Step == next)} on", () => SetStep(next, types));
            var back = Step == 0 ? steps[^1] : steps.Where(s => s < Step).DefaultIfEmpty(0).Max();
            var label = back == 0 ? "Lights off" : back > Step ? $"{Names(t => t.Step >= 1 && t.Step <= back)} on" : $"{Names(t => t.Step > back && t.Step <= Step)} off";
            actions.Add(InputActions.LightsBack, label, () => SetStep(back, types), hinted: false);
        }
        foreach (var key in types.Where(t => t.Key != null).GroupBy(t => t.Key!))
        {
            var ids = key.Select(t => t.Id).ToList();
            var names = Activation.Join(key.Select(t => t.Name).ToList());
            actions.Toggle(key.Key, ids.Any(On.Contains), $"{names} on", $"{names} off", on =>
            {
                foreach (var id in ids)
                    if (on) On.Add(id);
                    else On.Remove(id);
                return null;
            }, hinted: false);
        }
        if (Has("beacons")) actions.Add(InputActions.Beacons, Beacons ? "Beacons off" : "Beacons on", () => Beacons = !Beacons, hinted: false);
        if (Has("turnLeft")) actions.Add(InputActions.TurnLeft, Signal == TurnSignal.Left ? "Stop signalling" : "Signal left", () => Turn(TurnSignal.Left), hinted: false);
        if (Has("turnRight")) actions.Add(InputActions.TurnRight, Signal == TurnSignal.Right ? "Stop signalling" : "Signal right", () => Turn(TurnSignal.Right), hinted: false);
        if (Has("turnLeft") || Has("turnRight"))
            actions.Add(InputActions.Hazards, Signal == TurnSignal.Hazards ? "Hazard lights off" : "Hazard lights on", () => Turn(TurnSignal.Hazards), hinted: false);
    }

    /// <summary>The light key's <paramref name="step"/>: the lights of its steps on, and only those (FS: a light state).</summary>
    private void SetStep(int step, IEnumerable<LampTypeDef> types)
    {
        Step = step;
        On.Clear();
        foreach (var t in types.Where(t => t.Step >= 1 && t.Step <= step)) On.Add(t.Id);
    }

    /// <summary>The driver got out (FS: lights off on leaving): the lights, beacons and turn signals go off, the hazards stay on.</summary>
    public void Leave()
    {
        Step = 0;
        On.Clear();
        Beacons = false;
        if (Signal != TurnSignal.Hazards) Signal = TurnSignal.Off;
    }

    /// <summary>Switches the turn signals to <paramref name="signal"/>, or off when they already are.</summary>
    private void Turn(TurnSignal signal) => Signal = Signal == signal ? TurnSignal.Off : signal;

    internal override void Update(Simulation sim, float dt)
    {
        var weather = sim.Weather;
        var switches = Switches;
        var root = (Owner as Machine)?.Root;
        var driven = root?.Get<Drivable>()?.Controller;
        // A helper at night lights every step (FS: the AI's working lights), not the high beams.
        var helperAtNight = driven is FieldWorkController && weather.Night;
        var motor = root?.Get<Motor>();
        var (braking, reversing) = (motor?.Braking == true, motor?.Reversing == true);
        var left = switches.Signal is TurnSignal.Left or TurnSignal.Hazards;
        var right = switches.Signal is TurnSignal.Right or TurnSignal.Hazards;
        var top = root != null && TopLights(root);
        var cab = driven != null ? CabBrightness(sim.Clock.HourOfDay) : 0f;
        for (var i = 0; i < _lit.Length; i++)
        {
            var l = Def.Lamps[i];
            _level[i] = l.TypeDef?.Control == "cab" ? cab : 1f;
            _lit[i] = (l.TopLight ? top : !l.BottomLight || !top) && l.Switch switch
            {
                "driver" => l.TypeDef?.Control switch
                {
                    "lights" => helperAtNight ? l.TypeDef.Step >= 1 : switches.On.Contains(l.TypeDef.Id),
                    "beacons" => switches.Beacons || l.AlwaysActive && driven != null,
                    "turnLeft" => left,
                    "turnRight" => right,
                    "brake" => braking,
                    "reverse" => reversing,
                    "cab" => cab > 0f,
                    _ => false,
                },
                "dark" => weather.Dim,
                "hours" => l.Hours is [var from, var to] && MathUtil.InHours(sim.Clock.HourOfDay, from, to),
                "trigger" => l.Trigger?.Occupied(Owner, sim) == true,
                _ => false,
            };
        }
    }

    protected override LightsSave Capture(ContentDatabase content) =>
        new() { Step = Step, On = On.Order().ToList(), Beacons = Beacons, Signal = Signal };

    protected override void Restore(LightsSave save, SaveContext context)
    {
        Step = Math.Clamp(save.Step, 0, 99);
        On.Clear();
        // Saves from before the lights switched alone keep the step's.
        foreach (var id in save.On ?? context.Content.LampTypes.Values.Where(t => t.Control == "lights" && t.Step >= 1 && t.Step <= Step).Select(t => t.Id))
            On.Add(id);
        Beacons = save.Beacons;
        Signal = Enum.IsDefined(save.Signal) ? save.Signal : TurnSignal.Off;
    }
}
