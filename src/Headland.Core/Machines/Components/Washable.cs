using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Weather;

namespace Headland.Core.Machines.Components;

/// <summary>
/// Dirt (FS: washable): a machine gets dirty as it drives, faster on a field and the wetter the ground, and faster
/// again as it works the ground; rain rinses a standing machine, down to half dirty; a wash bay cleans it. It only shows.
/// </summary>
public sealed class WashableDef : MachineComponentDef
{
    /// <summary>Minutes of driving (real time, on a road) from clean to fully dirty.</summary>
    public float DirtMinutes { get; set; } = 90f;
    /// <summary>How much faster it gets dirty on a field, times 1 + the ground's wetness.</summary>
    public float FieldFactor { get; set; } = 2f;
    /// <summary>Added while its work areas work the ground (or its thresher threshes), at its full work speed.</summary>
    public float WorkFactor { get; set; } = 4f;
    /// <summary>Minutes of rain it takes to rinse it standing, down to half dirty.</summary>
    public float RainMinutes { get; set; } = 1f;

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (DirtMinutes <= 0f || RainMinutes <= 0f) yield return "dirtMinutes and rainMinutes must be > 0";
        if (FieldFactor < 1f || WorkFactor < 0f) yield return "fieldFactor must be >= 1, workFactor >= 0";
    }

    internal override Component Create(Machine machine) => new Washable(machine, this);
}

public sealed class WashableSave
{
    public double Dirt { get; set; }
}

public sealed class Washable(Machine machine, WashableDef def) : MachineComponent<WashableDef, WashableSave>(machine, def), IReadoutSource
{
    /// <summary>Below 1 km/h a machine stands: it gets no dirtier.</summary>
    private const float Standing = 1f / 3.6f;
    /// <summary>Rain rinses a machine down to this.</summary>
    private const double RinsedTo = 0.5;

    /// <summary>Kept in doubles: a tick's dirt is too little for a float's precision.</summary>
    private double _dirt;

    /// <summary>0 = clean, 1 = caked.</summary>
    public float Dirt
    {
        get => (float)_dirt;
        set => _dirt = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>How dirty it is, once that shows.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (Dirt >= 0.2f) yield return new Gauge("dirt", "Dirt", Dirt, $"{Dirt * 100f:0}%", Tone.Dim);
    }

    /// <summary>How much faster than on a road it gets dirty now (FS: the dirt multiplier); 0 standing.</summary>
    public float Rate(Simulation sim)
    {
        var m = Machine;
        var speed = MathF.Abs(m.Root.Speed);
        if (speed < Standing) return 0f;
        var (cx, cz) = sim.World.WorldToCell(m.Position);
        var rate = 1f;
        if (sim.World.InBounds(cx, cz) && sim.World.Layers.FieldId[sim.World.CellIndex(cx, cz)] > 0)
            rate *= Def.FieldFactor * (1f + sim.Weather.GroundWetness);
        // FS: a working implement adds its work factor, at the share of its work speed it goes.
        if (m.Get<Thresher>() is { On: true }) rate += Def.WorkFactor;
        else if (m.Get<WorkAreas>() is { } w && w.Def.Areas.FirstOrDefault(w.Working) is { } area)
            rate += Def.WorkFactor * MathF.Min(1f, speed * 3.6f / w.MaxSpeedKmh(area));
        return rate;
    }

    internal override void Update(Simulation sim, float dt)
    {
        var rate = Rate(sim);
        if (rate > 0f) _dirt = Math.Min(1.0, _dirt + dt / (Def.DirtMinutes * 60.0) * rate);
        else if (_dirt > RinsedTo && sim.Weather.Condition is WeatherCondition.Rain or WeatherCondition.Storm && sim.Weather.Temperature > 0f)
            _dirt = Math.Max(RinsedTo, _dirt - dt / (Def.RainMinutes * 60.0));
    }

    protected override WashableSave Capture(ContentDatabase content) => new() { Dirt = _dirt };

    protected override void Restore(WashableSave save, SaveContext context) => _dirt = Math.Clamp(save.Dirt, 0.0, 1.0);
}
