using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Ownership;

namespace Headland.Core.Machines;

public sealed class FillUnit(FillUnitDef def)
{
    public FillUnitDef Def { get; } = def;
    public string? FillType { get; set; } = def.StartLevel > 0 ? def.StartFillType : null;
    public float Level { get; set; } = def.StartLevel;
    public float Capacity => Def.Capacity;
    public float Free => Capacity - Level;
    public bool IsEmpty => Level <= 0.001f;
    public float Fraction => Capacity > 0 ? Level / Capacity : 0f;

    /// <summary>True if this unit can take the fill type right now (accepted, and not mixed with another type).</summary>
    public bool CanAccept(string fillType) =>
        Def.FillTypes.Contains(fillType) && (IsEmpty || FillType == fillType) && Free > 0.001f;

    public bool Accepts(string fillType) => Def.FillTypes.Contains(fillType);

    public float Add(string fillType, float amount)
    {
        if (!CanAccept(fillType)) return 0f;
        var added = MathF.Min(amount, Free);
        Level += added;
        FillType = fillType;
        return added;
    }

    public float Remove(float amount)
    {
        var removed = MathF.Min(amount, Level);
        Level -= removed;
        if (IsEmpty)
        {
            Level = 0f;
            FillType = null;
        }
        return removed;
    }
}

public struct VehicleInput
{
    /// <summary>-1..1. Forward accelerates; backward brakes, then reverses.</summary>
    public float Throttle;
    /// <summary>-1..1, positive turns left.</summary>
    public float Steer;
    /// <summary>Brake to a stop without reversing.</summary>
    public bool Brake;
}

public interface IVehicleController
{
    VehicleInput GetInput(Machine vehicle, float dt);
}

/// <summary>Input written by the presentation layer (keyboard/gamepad).</summary>
public sealed class ManualController : IVehicleController
{
    public VehicleInput Input;
    public VehicleInput GetInput(Machine vehicle, float dt) => Input;
}

public sealed class Machine : IOwnable
{
    public Machine(int id, MachineDef def, Vector2 position, float heading, int farmId = Farm.PlayerId)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        FillUnits = def.FillUnits.Select(u => new FillUnit(u)).ToArray();
    }

    public int Id { get; }
    public MachineDef Def { get; }
    /// <summary>Owning farm; only its members drive it or hitch to it (<see cref="Farm.None"/> = an NPC's).</summary>
    public int FarmId { get; set; }

    /// <summary>Center of the non-steered axle (the kinematic reference point).</summary>
    public Vector2 Position { get; set; }
    public float Heading { get; set; }

    /// <summary>Signed speed in m/s along the heading (root machines drive; children copy the root's speed).</summary>
    public float Speed { get; set; }
    /// <summary>Current steering angle in radians, positive = left turn.</summary>
    public float SteerAngle { get; set; }
    /// <summary>Distance rolled, for wheel rotation visuals.</summary>
    public float Distance { get; set; }

    public Machine? Parent { get; set; }
    public string? ParentJoint { get; set; }
    public Dictionary<string, Machine> Attached { get; } = new();

    public bool Lowered { get; set; }
    public bool TurnedOn { get; set; }
    public bool PipeOut { get; set; }
    public bool Tipping { get; set; }

    /// <summary>Smoothed 0..1 animation states for visuals (lift, pipe, tipper).</summary>
    public float LowerAnim { get; set; }
    public float PipeAnim { get; set; }
    public float TipAnim { get; set; }

    public FillUnit[] FillUnits { get; }

    /// <summary>Seeder: index into ContentDatabase.Crops.</summary>
    public int SelectedCrop { get; set; }

    public IVehicleController? Controller { get; set; }

    /// <summary>Latest warning for the HUD ("Out of seed", "Wrong header", ...), cleared each tick when fine.</summary>
    public string? Status { get; set; }

    // Previous work-area pose, so the swept region between ticks has no gaps.
    internal bool HasWorkPose;
    internal Vector2 PrevWorkCenter;
    internal float PrevWorkHeading;

    /// <summary>Hectares worked by this machine (statistics).</summary>
    public float WorkedHa { get; set; }

    public bool IsMotorized => Def.Motorized != null;
    public bool IsAttachable => Def.Attacher != null;
    public Machine Root => Parent?.Root ?? this;
    public Vector2 Forward => MathUtil.Forward(Heading);

    public FillUnit? Unit(string? id) => id == null ? null : Array.Find(FillUnits, u => u.Def.Id == id);

    public Vector2 LocalToWorld(Vector2 local) => MathUtil.LocalToWorld(Position, Heading, local);
    public Vector2 LocalToWorld(float x, float z) => LocalToWorld(new Vector2(x, z));

    public Obb Footprint => new(
        LocalToWorld(0f, Def.Size.CenterZ),
        new Vector2(Def.Size.Width * 0.5f, Def.Size.Length * 0.5f),
        Heading);

    public AttacherJointDef? Joint(string id) => Array.Find(Def.AttacherJoints, j => j.Id == id);

    /// <summary>This machine and everything attached below it, depth first.</summary>
    public IEnumerable<Machine> Chain()
    {
        yield return this;
        foreach (var child in Attached.Values)
        foreach (var m in child.Chain())
            yield return m;
    }

    public float SelfMassWithLoad(ContentDatabase content)
    {
        var mass = Def.Mass;
        foreach (var u in FillUnits)
            if (u.FillType != null && content.FillTypes.TryGetValue(u.FillType, out var ft))
                mass += u.Level * ft.MassPerUnit;
        return mass;
    }

    public override string ToString() => $"{Def.Name} #{Id}";
}
