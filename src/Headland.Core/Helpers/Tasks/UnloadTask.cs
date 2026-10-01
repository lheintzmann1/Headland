using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Helpers.Tasks;

/// <summary>
/// A combine's helper unloading (FS: the pipe's automatic discharge): while it works, its pipe swings out by itself
/// over a trailer driven alongside, and back in a while after the trailer left (<see cref="OnTheGo"/>). Its tank full,
/// it stands with its pipe out and waits for a trailer, and goes on with the work once the tank is empty, or once the
/// trailer left it some room.
/// </summary>
public sealed class UnloadTask : HelperTask
{
    /// <summary>Seconds without a trailer under the pipe before it swings back in, or the helper goes on with room in its tank.</summary>
    private const float LeftSeconds = 3f;
    /// <summary>Share of the tank free for the helper to go on with the work without emptying it.</summary>
    private const float Room = 0.1f;
    private float _without;

    public override string Kind => "unload";

    private static Pipe? PipeOf(HelperJob job) => job.Vehicle.Chain().Select(m => m.Get<Pipe>()).FirstOrDefault(p => p != null);

    public override string? Check(HelperJob job) => PipeOf(job) == null ? $"The {job.Vehicle.Def.Name} has no pipe to unload with" : null;

    public override string Describe(HelperJob job) => PipeOf(job)?.Flowing == true ? "unloading" : "waiting for a trailer";

    protected override void Start(HelperJob job)
    {
        _without = 0f;
        if (PipeOf(job) is { } pipe) pipe.Out = true;
    }

    protected internal override VehicleInput Drive(HelperJob job, float dt)
    {
        if (PipeOf(job) is not { } pipe || pipe.Tank.IsEmpty)
        {
            Finish();
            return Brake;
        }
        pipe.Out = true;
        _without = pipe.Flowing ? 0f : _without + dt;
        if (_without >= LeftSeconds && pipe.Tank.Free >= Room * pipe.Tank.Capacity) Finish();
        return Brake;
    }

    /// <summary>While the combine works: the pipe out over a trailer under it, and in again a while after it left.</summary>
    internal void OnTheGo(HelperJob job, Simulation sim, float dt)
    {
        if (PipeOf(job) is not { } pipe) return;
        var tank = pipe.Tank;
        if (tank.FillType is { } ft && sim.Machines.FindReceiver(pipe.Outlet, ft, pipe.Machine) != null)
        {
            pipe.Out = true;
            _without = 0f;
            return;
        }
        _without += dt;
        if (pipe.Out && _without >= LeftSeconds) pipe.Out = false;
    }

    protected internal override void Release(HelperJob job)
    {
        if (PipeOf(job) is { } pipe) pipe.Out = false;
    }
}
