using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class LampDef
{
    /// <summary>Lamp types, each switched as a group.</summary>
    public static readonly string[] Types = ["head", "workFront", "workRear", "beacon"];

    /// <summary>Optional, unique on the machine: names the lamp so that a configuration option can add or change it.</summary>
    public string? Id { get; set; }
    /// <summary>One of <see cref="Types"/>.</summary>
    public string Type { get; set; } = "head";
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

/// <summary>Headlights, work lights and beacons. Headlights also come on by themselves after dark.</summary>
public sealed class LightsDef : MachineComponentDef
{
    public LampDef[] Lamps { get; set; } = [];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        foreach (var l in Lamps.Where(l => !LampDef.Types.Contains(l.Type))) yield return $"unknown lamp type '{l.Type}' ({string.Join(", ", LampDef.Types)})";
        if (Lamps.Any(l => l.Range <= 0f || l.AngleDeg is <= 0f or >= 90f || l.Energy < 0f)) yield return "lamps need range > 0, angleDeg in (0, 90) and energy >= 0";
        foreach (var id in Lamps.Where(l => l.Id != null).GroupBy(l => l.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            yield return $"lamp '{id}' is defined more than once";
    }

    internal override Component Create(Machine machine) => new Lights(machine, this);
}

public sealed class LightsSave
{
    public List<string> On { get; set; } = [];
}

public sealed class Lights(Machine machine, LightsDef def) : MachineComponent<LightsDef, LightsSave>(machine, def)
{
    private readonly HashSet<string> _on = [];

    /// <summary>Lamp types switched on.</summary>
    public IReadOnlyCollection<string> On => _on;

    public bool IsOn(string type) => _on.Contains(type);

    /// <summary>Switches the lamps of a type on or off; false when the machine has none.</summary>
    public bool Switch(string type, bool on)
    {
        if (Def.Lamps.All(l => l.Type != type)) return false;
        if (on) _on.Add(type);
        else _on.Remove(type);
        return true;
    }

    protected override LightsSave Capture(ContentDatabase content) => new() { On = [.. _on.Order()] };

    protected override void Restore(LightsSave save, SaveContext context)
    {
        _on.Clear();
        foreach (var type in save.On) Switch(type, true);
    }
}
