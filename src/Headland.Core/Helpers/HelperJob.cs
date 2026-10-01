using Headland.Core.Events;
using Headland.Core.Helpers.Tasks;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;
using Headland.Core.World;

namespace Headland.Core.Helpers;

/// <summary>
/// A helper at work (FS: an AI job): it drives its vehicle through the steps of its job one after the other (work the
/// field, unload, collect the bales…), its type deciding what follows each, and stops when the job is done or something
/// stops it that only the player can see to. Its parameters (the vehicle, the field) are checked before it's hired.
/// </summary>
public sealed class HelperJob : IVehicleController
{
    private bool _begun;
    private bool _done;

    internal HelperJob(Simulation sim, HelperJobType type, Machine vehicle, FieldInfo field, Func<HelperJob, IReadOnlyList<HelperTask>> tasks)
    {
        Sim = sim;
        Type = type;
        Vehicle = vehicle;
        Field = field;
        Tasks = tasks(this);
    }

    internal Simulation Sim { get; }
    public HelperJobType Type { get; }
    public Machine Vehicle { get; }
    /// <summary>The field it works: the vehicle's work stays inside it.</summary>
    public FieldInfo Field { get; }
    /// <summary>Its steps, in the order its type sets them up.</summary>
    public IReadOnlyList<HelperTask> Tasks { get; }
    /// <summary>The step it's at.</summary>
    public int TaskIndex { get; private set; }
    public HelperTask Current => Tasks[TaskIndex];

    /// <summary>Its field work, if its job has one: the route, the lanes done.</summary>
    public FieldWorkTask? FieldWork => Tasks.OfType<FieldWorkTask>().FirstOrDefault();

    public bool Stopped { get; private set; }
    /// <summary>What it stopped for (out of fuel, out of seed).</summary>
    public MachineCondition? StopReason { get; private set; }
    public bool Finished => _done || Stopped;

    /// <summary>
    /// The helper's number (FS: each hired helper has one), the lowest no other helper at work has when hired: "Helper 2"
    /// in the HUD, and on its vehicle on the map.
    /// </summary>
    public int Number { get; internal set; }

    /// <summary>Pay per hour of work, agreed when hired. Helpers drive in real time, so the clock speed doesn't change it.</summary>
    public float WagePerHour { get; internal set; }
    /// <summary>Real seconds worked on this job.</summary>
    public double WorkedSeconds { get; internal set; }
    /// <summary>What the helper earned on this job so far.</summary>
    public float Wages => (float)(WorkedSeconds * WagePerHour / 3600.0);
    /// <summary>The part of <see cref="Wages"/> paid already: whole dollars as they add up, the rest when the job ends.</summary>
    public float WagesPaid { get; internal set; }

    /// <summary>Why the job can't be done (the first of its steps to say so), checked before hiring; null if it can.</summary>
    public string? Check() => Tasks.Select(t => t.Check(this)).FirstOrDefault(why => why != null);

    /// <summary>What it's doing, in a few words: "lane 3/12", "waiting for a trailer".</summary>
    public string Describe() => Finished ? "done" : Current.Describe(this);

    /// <summary>
    /// Takes over the vehicle, steering it normally, and starts the first step, which takes over the implements (unfolds
    /// them, turns on what its work needs); each later step does as it starts.
    /// </summary>
    internal void TakeOver()
    {
        Vehicle.Get<RunningGear>()!.Mode = SteeringMode.Normal;
        Type.TakeOver(this);
        Current.Begin(this);
        _begun = true;
    }

    /// <summary>Leaves the vehicle: each step raises and switches off what it lowered or switched on.</summary>
    internal void Release()
    {
        foreach (var task in Tasks) task.Release(this);
    }

    /// <summary>Back from a save at step <paramref name="index"/>, which had started.</summary>
    internal void Restore(int index)
    {
        TaskIndex = Math.Clamp(index, 0, Tasks.Count - 1);
        Current.Resume();
        _begun = true;
    }

    public VehicleInput GetInput(Machine v, float dt)
    {
        if (Finished) return new VehicleInput { Brake = true };
        var task = Current;
        if (!_begun)
        {
            task.Begin(this);
            _begun = true;
        }
        Type.Update(this, dt);
        var input = task.Drive(this, dt);
        switch (task.State)
        {
            case TaskState.Done:
                GoTo(Type.Next(this, TaskIndex));
                break;
            case TaskState.Paused when Type.AfterPause(this, TaskIndex, task.Reason!) is { } next:
                // Seen to by another step: it waits (FS: the combine waiting for a trailer).
                GoTo(next);
                Sim.Events.Publish(new HelperWaiting(Vehicle, Field, Number, task.Reason!));
                break;
            case TaskState.Paused or TaskState.Stopped:
                Stopped = true;
                StopReason = task.Reason;
                break;
        }
        return input;
    }

    private void GoTo(int? next)
    {
        if (next is not { } index)
        {
            _done = true;
            return;
        }
        TaskIndex = index;
        _begun = false;
    }
}
