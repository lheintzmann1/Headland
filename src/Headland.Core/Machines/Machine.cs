using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Machines.Components;
using Headland.Core.Ownership;

namespace Headland.Core.Machines;

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

/// <summary>
/// A machine on the map: where it is, what it's hitched to, and the components its type is built from, which keep
/// the rest of its state (<see cref="Get{T}"/>).
/// </summary>
public sealed class Machine : IOwnable
{
    public Machine(int id, MachineDef def, Vector2 position, float heading, int farmId = Farm.PlayerId)
    {
        Id = id;
        Def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        Components = def.Components.Select(c => c.Create(this)).ToArray();
    }

    public int Id { get; }
    public MachineDef Def { get; }
    /// <summary>Owning farm; only its members drive it or hitch to it (<see cref="Farm.None"/> = an NPC's).</summary>
    public int FarmId { get; set; }
    /// <summary>The contract the machine is leased for (0: the farm's own); it goes back when the contract ends.</summary>
    public int LeaseContract { get; set; }

    public IReadOnlyList<MachineComponent> Components { get; }

    /// <summary>Its component of type <typeparamref name="T"/> (or implementing it), if it has one.</summary>
    public T? Get<T>() where T : class
    {
        foreach (var c in Components)
            if (c is T t) return t;
        return null;
    }

    public bool Has<T>() where T : class => Get<T>() != null;

    /// <summary>Center of the non-steered axle (the kinematic reference point).</summary>
    public Vector2 Position { get; set; }
    public float Heading { get; set; }

    /// <summary>Signed speed in m/s along the heading (root machines drive; children copy the root's speed).</summary>
    public float Speed { get; set; }

    public Machine? Parent { get; set; }
    public string? ParentJoint { get; set; }
    public Dictionary<string, Machine> Attached { get; } = new();

    /// <summary>Latest warning for the HUD ("Out of seed", "Wrong header", ...), cleared each tick when fine.</summary>
    public string? Status { get; set; }

    /// <summary>Hectares worked by this machine (statistics).</summary>
    public float WorkedHa { get; set; }

    /// <summary>Mechanical condition, 1 = like new, 0 = worn out. Restored at a repair POI.</summary>
    public float Condition { get; set; } = 1f;
    /// <summary>Dirt on the machine, 0 = clean, 1 = caked. Washed off at a wash POI.</summary>
    public float Dirt { get; set; }

    public Machine Root => Parent?.Root ?? this;
    public Vector2 Forward => MathUtil.Forward(Heading);

    /// <summary>Its fill units (none without a fillUnits component).</summary>
    public IReadOnlyList<FillUnit> FillUnits => Get<FillUnits>()?.Units ?? [];

    public FillUnit? Unit(string? id) => Get<FillUnits>()?.Unit(id);

    public AttacherJointDef? Joint(string id) => Def.Joints.FirstOrDefault(j => j.Id == id);

    public Vector2 LocalToWorld(Vector2 local) => MathUtil.LocalToWorld(Position, Heading, local);
    public Vector2 LocalToWorld(float x, float z) => LocalToWorld(new Vector2(x, z));

    public Obb Footprint => new(
        LocalToWorld(0f, Def.Size.CenterZ),
        new Vector2(Def.Size.Width * 0.5f, Def.Size.Length * 0.5f),
        Heading);

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
