using System.Numerics;
using System.Text.Json.Serialization;
using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines.Work;

namespace Headland.Core.Machines.Components;

public sealed class WorkAreaDef
{
    /// <summary>What it does to the ground it passes over: a <see cref="WorkTypes">work type</see>'s id.</summary>
    public string Type { get; set; } = "cultivator";
    public float Width { get; set; } = 3f;
    public float Length { get; set; } = 1f;
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Works only while turned on (a seed drill). A harvester works while the thresher it hangs on is on.</summary>
    public bool RequiresOn { get; set; }
    public float MaxWorkSpeedKmh { get; set; } = 12f;
    public float RequiredPowerHp { get; set; } = 60f;
    /// <summary>Harvester, mower: crop harvest groups it cuts.</summary>
    public string[] HarvestGroups { get; set; } = [];
    /// <summary>Seeder, spreader, sprayer: the fill unit what it sows or spreads comes from.</summary>
    public string? FillUnit { get; set; }
    /// <summary>Spreader, sprayer: units of its fill spread on a hectare.</summary>
    public float RatePerHa { get; set; }
    /// <summary>
    /// Mower, windrower: the width of the windrow it leaves in its middle (FS: a swath), what it cuts or rakes drawn in
    /// from the sides; 0: a mower leaves the grass where it cut it.
    /// </summary>
    public float WindrowWidth { get; set; }

    /// <summary>Its work type (known once the content is validated).</summary>
    [JsonIgnore]
    public WorkType Work => WorkTypes.Find(Type) ?? throw new InvalidOperationException($"Unknown work type '{Type}'");
}

/// <summary>
/// Where an implement works the ground: a cultivator's tines, a drill's coulters, a header's cutter bar, a spreader's
/// throw. What each area does is its work type's (<see cref="WorkTypes"/>). A harvester cuts for the thresher of the
/// machine it hangs on; its reel turns while that one threshes.
/// </summary>
public sealed class WorkAreasDef : MachineComponentDef, ISpecSource
{
    public WorkAreaDef[] Areas { get; set; } = [];

    public override IEnumerable<string> Roles =>
        Areas.Select(a => WorkTypes.Find(a.Type)).OfType<WorkType>().SelectMany(w => w.Roles).Distinct();

    /// <summary>The turn-on key, when an area works only turned on.</summary>
    public override IEnumerable<string> Toggles => Areas.Any(a => a.RequiresOn) ? [InputActions.TurnOn] : [];

    /// <summary>The width its areas cover side by side, how fast they work, the power they take, and the crops they cut.</summary>
    public IEnumerable<Spec> Specs(EntityDef owner, ContentDatabase content)
    {
        if (Areas.Length == 0) yield break;
        var width = Areas.Max(a => a.X + a.Width * 0.5f) - Areas.Min(a => a.X - a.Width * 0.5f);
        yield return new Spec("Working width", $"{width:0.#} m");
        yield return new Spec("Working speed", $"{Areas.Min(a => a.MaxWorkSpeedKmh):0} km/h");
        yield return new Spec("Power needed", $"{Areas.Sum(a => a.RequiredPowerHp):0} hp");
        var groups = Areas.SelectMany(a => a.HarvestGroups).ToHashSet();
        if (groups.Count > 0)
            yield return new Spec("Crops", string.Join(", ", content.Crops.Where(c => groups.Contains(c.HarvestGroup)).Select(c => c.Name.ToLowerInvariant())));
    }

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Areas.Length == 0) yield return "needs areas";
        foreach (var a in Areas)
        {
            if (WorkTypes.Find(a.Type) is not { } work)
            {
                yield return $"unknown type '{a.Type}' ({WorkTypes.Known})";
                continue;
            }
            if (a.Width <= 0f || a.Length <= 0f || a.MaxWorkSpeedKmh <= 0f || a.RequiredPowerHp < 0f || a.RatePerHa < 0f)
                yield return $"{a.Type}: width, length and maxWorkSpeedKmh must be > 0, requiredPowerHp and ratePerHa >= 0";
            if (a.WindrowWidth < 0f || a.WindrowWidth > a.Width) yield return $"{a.Type}: windrowWidth must be 0..width";
            foreach (var e in work.Errors(a, machine, content)) yield return e;
        }
    }

    internal override Component Create(Machine machine) => new WorkAreas(machine, this);
}

/// <summary>One work area of a machine, with where it was last tick so the ground swept in between has no gaps.</summary>
public sealed class WorkArea(WorkAreaDef def)
{
    public WorkAreaDef Def { get; } = def;
    internal bool HasPose;
    internal Vector2 PrevCenter;
    internal float PrevHeading;
}

public sealed class WorkAreasSave
{
    public bool On { get; set; }
    /// <summary>Seeders: the crop sown (its id).</summary>
    public string? Crop { get; set; }
    /// <summary>Per area, [x, z, heading] of its center last tick (null: it wasn't working).</summary>
    public List<float[]?> Poses { get; set; } = [];
}

public sealed class WorkAreas : MachineComponent<WorkAreasDef, WorkAreasSave>, ISwitchable, IConditionSource, IActionSource, IReadoutSource
{
    private readonly List<MachineCondition> _conditions = [];

    public WorkAreas(Machine machine, WorkAreasDef def) : base(machine, def)
    {
        Areas = def.Areas.Select(a => new WorkArea(a)).ToArray();
        if (def.Areas is [var only])
        {
            Bounds = (new Vector2(only.X, only.Z), only.Width, only.Length);
            return;
        }
        if (def.Areas.Length == 0) return;
        var (x0, x1) = (def.Areas.Min(a => a.X - a.Width * 0.5f), def.Areas.Max(a => a.X + a.Width * 0.5f));
        var (z0, z1) = (def.Areas.Min(a => a.Z - a.Length * 0.5f), def.Areas.Max(a => a.Z + a.Length * 0.5f));
        Bounds = (new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f), x1 - x0, z1 - z0);
    }

    public IReadOnlyList<WorkArea> Areas { get; }

    /// <summary>The rectangle around all its areas, in the machine's space: center, width (x) and length (z).</summary>
    public (Vector2 center, float width, float length) Bounds { get; }

    public bool CanTurnOn => Def.Areas.Any(a => a.RequiresOn);
    public bool On { get; set; }

    public bool Sows => Def.Areas.Any(a => a.Work.Sows);
    /// <summary>Seeders: the crop sown, as an index into <see cref="ContentDatabase.Crops"/>.</summary>
    public int Crop { get; set; }

    /// <summary>On or off, when it works turned on; what a seeder sows.</summary>
    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (CanTurnOn) yield return On ? new Status("On", Tone.Good) : new Status("Off", Tone.Dim);
        if (Sows) yield return new Status($"Sows {sim.Content.Crops[Crop].Name.ToLowerInvariant()}");
    }

    /// <summary>The turn-on key when it must be on to work; a seeder's seed key.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        actions.AddSwitch(this);
        if (!Sows) return;
        actions.Add(InputActions.CycleSeed, "Change seed", () =>
        {
            Crop = (Crop + 1) % sim.Content.Crops.Count;
            var crop = sim.Content.Crops[Crop];
            sim.Notifications.Post($"Seeder: {crop.Name} (sow {MachineSystem.Months(crop.SowingMonths)})");
        });
    }

    /// <summary>
    /// What kept its areas from working on their last pass over the ground (out of seed, the wrong header…); running
    /// out lasts until it's filled up again.
    /// </summary>
    public IEnumerable<MachineCondition> Conditions => _conditions.Where(c => c is not OutOf || Def.Areas.All(a => Machine.Unit(a.FillUnit) is not { IsEmpty: false }));

    /// <summary>Its areas are working the ground again: what kept them from it before is forgotten.</summary>
    internal void ClearConditions() => _conditions.Clear();

    internal void Report(MachineCondition condition)
    {
        if (!_conditions.Contains(condition)) _conditions.Add(condition);
    }

    /// <summary>How fast <paramref name="area"/> works at most: its work speed, less what wear took (<see cref="Wearable"/>).</summary>
    public float MaxSpeedKmh(WorkAreaDef area) => area.MaxWorkSpeedKmh * Machine.SpeedFactor();

    /// <summary>The narrowest of its areas: lanes are laid out for it.</summary>
    public float MinWidth => Def.Areas.Min(a => a.Width);

    /// <summary>
    /// True if the work area does its work now: the implement unfolded and lowered (when it folds or lowers, its parts
    /// down too), and turned on (a harvester: the thresher it hangs on) when it needs to be.
    /// </summary>
    public bool Working(WorkAreaDef area)
    {
        if (Machine.Get<AnimatedParts>() is { InWorkingPose: false }) return false;
        if (Machine.Get<Attachable>() is { Def.Lowerable: true, Lowered: false }) return false;
        return area.Work.Working(this, area);
    }

    internal void ForgetPoses()
    {
        foreach (var a in Areas) a.HasPose = false;
    }

    internal override void OnHitched() => ForgetPoses();

    internal override void OnDetached()
    {
        On = false;
        ForgetPoses();
    }

    protected override WorkAreasSave Capture(ContentDatabase content) => new()
    {
        On = On,
        Crop = Sows ? content.Crops[Crop].Id : null,
        Poses = Areas.Select(a => a.HasPose ? new[] { a.PrevCenter.X, a.PrevCenter.Y, a.PrevHeading } : null).ToList(),
    };

    protected override void Restore(WorkAreasSave save, SaveContext context)
    {
        On = save.On && CanTurnOn;
        if (save.Crop != null && context.Content.CropIndex(save.Crop) is var crop and >= 0) Crop = crop;
        for (var i = 0; i < Math.Min(Areas.Count, save.Poses.Count); i++)
        {
            if (save.Poses[i] is not [var x, var z, var heading]) continue;
            Areas[i].HasPose = true;
            Areas[i].PrevCenter = new Vector2(x, z);
            Areas[i].PrevHeading = heading;
        }
    }
}
