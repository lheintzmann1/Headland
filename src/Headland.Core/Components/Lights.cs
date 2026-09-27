using Headland.Core.Content;

namespace Headland.Core.Components;

public sealed class LampDef
{
    /// <summary>Lamp types, each switched as a group.</summary>
    public static readonly string[] Types = ["head", "workFront", "workRear", "beacon"];

    /// <summary>What switches a lamp (FS: the driver's lights, and PlaceableLights by time of day or trigger).</summary>
    public static readonly string[] Switches = ["driver", "dark", "hours", "trigger"];

    /// <summary>Optional, unique on the entity: names the lamp so that a configuration option can add or change it.</summary>
    public string? Id { get; set; }
    /// <summary>One of <see cref="Types"/>.</summary>
    public string Type { get; set; } = "head";
    /// <summary>
    /// One of <see cref="Switches"/>: "driver" (with its type, as the driver switches it; headlights also at night),
    /// "dark" (a light sensor: at night, or under a sky darkened by rain, snow or fog), "hours" (a timer:
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
/// Lamps: a machine's headlights, work lights and beacons, switched by its driver (headlights also come on by themselves
/// at night); a building's lamps, lit by a light sensor, a timer or someone coming by.
/// </summary>
public sealed class LightsDef : ComponentDef
{
    public LampDef[] Lamps { get; set; } = [];

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        foreach (var l in Lamps.Where(l => !LampDef.Types.Contains(l.Type))) yield return $"unknown lamp type '{l.Type}' ({string.Join(", ", LampDef.Types)})";
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

public sealed class LightsSave
{
    public List<string> On { get; set; } = [];
}

public sealed class Lights : Component<LightsDef, LightsSave>
{
    private readonly HashSet<string> _on = [];
    private readonly bool[] _lit;

    public Lights(Entity owner, LightsDef def) : base(owner, def) => _lit = new bool[def.Lamps.Length];

    /// <summary>Lamp types the driver switched on.</summary>
    public IReadOnlyCollection<string> On => _on;

    public bool IsOn(string type) => _on.Contains(type);

    /// <summary>Whether lamp <paramref name="index"/> (of the def's lamps) shines, as of the last tick.</summary>
    public bool Lit(int index) => _lit[index];

    /// <summary>Switches the driver's lamps of a type on or off; false when it has none.</summary>
    public bool Switch(string type, bool on)
    {
        if (Def.Lamps.All(l => l.Type != type || l.Switch != "driver")) return false;
        if (on) _on.Add(type);
        else _on.Remove(type);
        return true;
    }

    internal override void Update(Simulation sim, float dt)
    {
        var weather = sim.Weather;
        for (var i = 0; i < _lit.Length; i++)
        {
            var l = Def.Lamps[i];
            _lit[i] = l.Switch switch
            {
                "driver" => _on.Contains(l.Type) || l.Type == "head" && weather.Night,
                "dark" => weather.Dim,
                "hours" => l.Hours is [var from, var to] && MathUtil.InHours(sim.Clock.HourOfDay, from, to),
                "trigger" => l.Trigger?.Occupied(Owner, sim) == true,
                _ => false,
            };
        }
    }

    protected override LightsSave Capture(ContentDatabase content) => new() { On = [.. _on.Order()] };

    protected override void Restore(LightsSave save, SaveContext context)
    {
        _on.Clear();
        foreach (var type in save.On) Switch(type, true);
    }
}
