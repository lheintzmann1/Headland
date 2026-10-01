using Headland.Core.Machines;

namespace Headland.Core.Helpers;

/// <summary>Where a <see cref="HelperTask"/> is at.</summary>
public enum TaskState
{
    Running,
    /// <summary>It did what it was for.</summary>
    Done,
    /// <summary>Something keeps it from going on that the job may see to (a full tank: unload it), its <see cref="HelperTask.Reason"/>.</summary>
    Paused,
    /// <summary>Something keeps it from going on that only the player can see to (out of fuel), its <see cref="HelperTask.Reason"/>.</summary>
    Stopped,
}

/// <summary>
/// One step of a helper's job (FS: an AI task): work a field, wait for a trailer to unload into, collect bales, set them
/// down. It drives the vehicle while it runs and says when it's done, or paused for something its job may see to. Each
/// checks it can be done before the helper is hired.
/// </summary>
public abstract class HelperTask
{
    /// <summary>Its kind, as saves name it.</summary>
    public abstract string Kind { get; }

    public TaskState State { get; private set; }

    /// <summary>Why it paused or stopped.</summary>
    public MachineCondition? Reason { get; private set; }

    /// <summary>Why it can't be done, checked before the helper is hired (null: it can).</summary>
    public virtual string? Check(HelperJob job) => null;

    /// <summary>It starts, or starts again after another step of its job.</summary>
    internal void Begin(HelperJob job)
    {
        (State, Reason) = (TaskState.Running, null);
        Start(job);
    }

    /// <summary>It picks up where it was, from a save: it had started.</summary>
    internal void Resume() => (State, Reason) = (TaskState.Running, null);

    protected virtual void Start(HelperJob job)
    {
    }

    /// <summary>Drives the vehicle for <paramref name="dt"/> seconds.</summary>
    protected internal abstract VehicleInput Drive(HelperJob job, float dt);

    /// <summary>What it's doing, in a few words for the HUD and the helpers list: "lane 3/12", "waiting for a trailer".</summary>
    public abstract string Describe(HelperJob job);

    /// <summary>The helper leaves: what the task lowered or switched on, it raises and switches off.</summary>
    protected internal virtual void Release(HelperJob job)
    {
    }

    protected void Finish() => State = TaskState.Done;

    protected void Pause(MachineCondition reason) => (State, Reason) = (TaskState.Paused, reason);

    protected void Stop(MachineCondition reason) => (State, Reason) = (TaskState.Stopped, reason);

    protected static VehicleInput Brake => new() { Brake = true };
}
