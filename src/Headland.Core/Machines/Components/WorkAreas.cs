using Headland.Core.Components;
using System.Numerics;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class WorkAreaDef
{
    public static readonly string[] Types = ["cultivator", "seeder", "harvester"];

    /// <summary>What it does to the ground it passes over: one of <see cref="Types"/>.</summary>
    public string Type { get; set; } = "cultivator";
    public float Width { get; set; } = 3f;
    public float Length { get; set; } = 1f;
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>Works only while turned on (a seed drill). A harvester works while the thresher it hangs on is on.</summary>
    public bool RequiresOn { get; set; }
    public float MaxWorkSpeedKmh { get; set; } = 12f;
    public float RequiredPowerHp { get; set; } = 60f;
    /// <summary>Harvester: crop harvest groups it cuts.</summary>
    public string[] HarvestGroups { get; set; } = [];
    /// <summary>Seeder: the fill unit its seed comes from.</summary>
    public string? FillUnit { get; set; }
}

/// <summary>
/// Where an implement works the ground: a cultivator's tines, a drill's coulters, a header's cutter bar. A harvester
/// cuts for the thresher of the machine it hangs on; its reel turns while that one threshes.
/// </summary>
public sealed class WorkAreasDef : MachineComponentDef
{
    public WorkAreaDef[] Areas { get; set; } = [];

    public override IEnumerable<string> Roles => Areas.Any(a => a.Type == "harvester") ? ["reel"] : [];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Areas.Length == 0) yield return "needs areas";
        foreach (var a in Areas)
        {
            if (!WorkAreaDef.Types.Contains(a.Type))
            {
                yield return $"unknown type '{a.Type}'";
                continue;
            }
            if (a.Width <= 0f || a.Length <= 0f || a.MaxWorkSpeedKmh <= 0f || a.RequiredPowerHp < 0f)
                yield return $"{a.Type}: width, length and maxWorkSpeedKmh must be > 0, requiredPowerHp >= 0";
            if (a.Type == "seeder" && !HasUnit(machine, a.FillUnit)) yield return "a seeder needs the fillUnit its seed comes from";
            if (a.Type == "harvester" && a.HarvestGroups.Length == 0) yield return "a harvester needs harvestGroups";
            if (a.Type == "harvester" && machine.Get<AttachableDef>() == null) yield return "a harvester hangs on a thresher: it needs an attachable";
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

public sealed class WorkAreas : MachineComponent<WorkAreasDef, WorkAreasSave>, ISwitchable
{
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

    public bool Sows => Def.Areas.Any(a => a.Type == "seeder");
    /// <summary>Seeders: the crop sown, as an index into <see cref="ContentDatabase.Crops"/>.</summary>
    public int Crop { get; set; }

    /// <summary>The narrowest of its areas: lanes are laid out for it.</summary>
    public float MinWidth => Def.Areas.Min(a => a.Width);

    /// <summary>
    /// True if the work area does its work now: the implement unfolded and lowered (when it folds or lowers), and
    /// turned on (a harvester: the thresher it hangs on) when it needs to be.
    /// </summary>
    public bool Working(WorkAreaDef area)
    {
        if (Machine.Get<AnimatedParts>() is { Unfolded: false }) return false;
        if (Machine.Get<Attachable>() is { Def.Lowerable: true, Lowered: false }) return false;
        return area.Type == "harvester" ? Machine.Parent?.Get<Thresher>() is { On: true } : !area.RequiresOn || On;
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
