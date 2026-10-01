using System.Numerics;
using Headland.Core.Helpers.Jobs;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Helpers;

/// <summary>
/// A kind of helper job (FS: an AI job type): which vehicles it's for, the steps it's made of, and which step follows
/// each, or follows one paused for something the job sees to. Registered in <see cref="HelperJobs"/>; Lua will add more.
/// </summary>
public abstract class HelperJobType(string id, string name)
{
    /// <summary>As saves name it.</summary>
    public string Id { get; } = id;
    /// <summary>What it's called: "Harvest".</summary>
    public string Name { get; } = name;

    /// <summary>Whether the vehicle and what hangs on it can do this job.</summary>
    public abstract bool Fits(Machine vehicle);

    /// <summary>Its steps, for <paramref name="job"/> (planned from where the vehicle stands, or restored from a save).</summary>
    protected internal abstract IReadOnlyList<HelperTask> Tasks(HelperJob job, JobSetup setup);

    /// <summary>A job of this type for <paramref name="vehicle"/> on <paramref name="field"/>, its steps set up (not hired yet).</summary>
    internal HelperJob Create(Simulation sim, Machine vehicle, FieldInfo field, JobSetup setup) => new(sim, this, vehicle, field, job => Tasks(job, setup));

    /// <summary>The step after step <paramref name="done"/>, done; null when the job is.</summary>
    protected internal virtual int? Next(HelperJob job, int done) => done + 1 < job.Tasks.Count ? done + 1 : null;

    /// <summary>The step that sees to what paused step <paramref name="paused"/>; null when nothing does, and the helper stops.</summary>
    protected internal virtual int? AfterPause(HelperJob job, int paused, MachineCondition reason) => null;

    /// <summary>The helper takes the vehicle over.</summary>
    protected internal virtual void TakeOver(HelperJob job)
    {
    }

    /// <summary>Each tick, before the step drives.</summary>
    protected internal virtual void Update(HelperJob job, float dt)
    {
    }

    /// <summary>The index of the job's first step of type <typeparamref name="T"/> (-1 without one).</summary>
    protected static int IndexOf<T>(HelperJob job) where T : HelperTask
    {
        for (var i = 0; i < job.Tasks.Count; i++)
            if (job.Tasks[i] is T)
                return i;
        return -1;
    }
}

/// <summary>
/// What a job is set up with besides its vehicle and field: field work's speed and lanes; back from a save, what its
/// steps kept (the route planned, the bales missed, where the bales go).
/// </summary>
public sealed record JobSetup
{
    /// <summary>Work speed asked for (0 = the implements' own).</summary>
    public float SpeedKmh { get; init; }
    /// <summary>Only the first lanes of the field (a strip of it).</summary>
    public int? MaxLanes { get; init; }
    public (float margin, FieldPath path)? Route { get; init; }
    public Dictionary<int, int>? Missed { get; init; }
    public (Vector2 spot, float heading, int loads)? Stacks { get; init; }
}

/// <summary>
/// The kinds of helper jobs, the first a vehicle fits being the one a helper hired for it does: collecting bales,
/// harvesting, baling, then any field work.
/// </summary>
public static class HelperJobs
{
    private static readonly List<HelperJobType> Types = [new CollectBalesJob(), new HarvestJob(), new BaleJob(), new FieldWorkJob()];

    public static IReadOnlyList<HelperJobType> Known => Types;

    public static HelperJobType? Find(string id) => Types.Find(t => t.Id == id);

    /// <summary>The job a helper hired for <paramref name="vehicle"/> does, or null when it can do none.</summary>
    public static HelperJobType? For(Machine vehicle) => Types.Find(t => t.Fits(vehicle));

    /// <summary>Adds a kind of job (a mod's), tried before the base game's.</summary>
    public static void Register(HelperJobType type)
    {
        if (Find(type.Id) != null) throw new InvalidOperationException($"helper job '{type.Id}' is already registered");
        Types.Insert(0, type);
    }
}
