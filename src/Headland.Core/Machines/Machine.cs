using System.Numerics;
using Headland.Core.Components;
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
    /// <summary>
    /// The tool keys, -1..1 (the mouse's motion beyond): up (+) and down, left (+) and right, for the selected tool's
    /// crane joints, in the selected control group or that many groups after it (the mouse with Ctrl or Shift).
    /// </summary>
    public float ToolY;
    public float ToolX;
    public int ToolGroupOffset;
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
/// the rest of its state (<see cref="Entity.Get{T}"/>).
/// </summary>
public sealed class Machine : Entity
{
    private MachineDef _def;

    public Machine(int id, MachineDef def, Vector2 position, float heading, int farmId = Farm.PlayerId)
    {
        Id = id;
        _def = def;
        Position = position;
        Heading = heading;
        FarmId = farmId;
        CreateComponents();
    }

    public int Id { get; }
    /// <summary>Its type, with the options it has (see <see cref="MachineDef.Configure"/>).</summary>
    public override MachineDef Def => _def;
    /// <summary>The contract the machine is leased for (0: the farm's own); it goes back when the contract ends.</summary>
    public int LeaseContract { get; set; }

    /// <summary>Leased at the shop (null: the farm's own, or leased for a contract): it costs its hours as it runs.</summary>
    public MachineLease? Lease { get; set; }

    /// <summary>Hours it ran (FS: operating hours), in real time as a helper's: see <see cref="Operating"/>.</summary>
    public double OperatingHours { get; set; }

    /// <summary>Months since it was new (FS: age), counted as each month starts.</summary>
    public int AgeMonths { get; set; }

    /// <summary>It runs (FS: operating): its engine does, or that of the vehicle it hangs on.</summary>
    public bool Operating => Root.Get<Motor>()?.Running == true;

    /// <summary>
    /// Gives the machine other options: <paramref name="def"/>, a def of its type. Its components are built anew, and
    /// each takes back what it kept, as from a save (fill levels, a lowered implement, who drives).
    /// </summary>
    internal void Reconfigure(MachineDef def, ContentDatabase content)
    {
        var states = SaveComponents(content);
        var driver = Get<Drivable>()?.Controller;
        _def = def;
        CreateComponents();
        LoadComponents(states, new SaveContext(content, []));
        if (Get<Drivable>() is { } seat) seat.Controller = driver;
    }

    /// <summary>Signed speed in m/s along the heading (root machines drive; children copy the root's speed).</summary>
    public float Speed { get; set; }

    public Machine? Parent { get; set; }
    public string? ParentJoint { get; set; }
    public Dictionary<string, Machine> Attached { get; } = new();

    /// <summary>What keeps it from working as it should (out of seed, a full tank…), as its components report it.</summary>
    public IEnumerable<MachineCondition> Conditions => Components.OfType<IConditionSource>().SelectMany(c => c.Conditions);

    /// <summary>Hectares worked by this machine (statistics).</summary>
    public float WorkedHa { get; set; }

    /// <summary>Dirt on the machine, 0 = clean, 1 = caked. Washed off at a wash POI.</summary>
    public float Dirt { get; set; }

    public Machine Root => Parent?.Root ?? this;
    public Vector2 Forward => MathUtil.Forward(Heading);

    public AttacherJointDef? Joint(string id) => Def.Joints.FirstOrDefault(j => j.Id == id);

    /// <summary>Its <see cref="Cover"/> is closed over <paramref name="unit"/>: nothing fills it.</summary>
    public bool ClosedOver(FillUnit unit) => Get<Cover>()?.Shuts(unit) == true;

    /// <summary>
    /// Where a point of the machine is, and which way the part it's on points: on an articulated machine, parts ahead
    /// of the hinge swing with the front frame.
    /// </summary>
    public (Vector2 position, float heading) PartToWorld(float x, float z)
    {
        if (Get<RunningGear>() is not { } gear) return (LocalToWorld(x, z), Heading);
        var (sx, sz, angle) = gear.Swing(x, z);
        return (LocalToWorld(sx, sz), Heading + angle);
    }

    /// <summary>The box it takes up (with its front frame straight, on an articulated machine).</summary>
    public Obb Footprint => new(
        LocalToWorld(0f, Def.Size.CenterZ),
        new Vector2(Def.Size.Width * 0.5f, Def.Size.Length * 0.5f),
        Heading);

    /// <summary>What it bumps into: its box, or an articulated machine's two frames, the front one swung.</summary>
    public (Obb rear, Obb? front) Boxes
    {
        get
        {
            var s = Def.Size;
            var (back, ahead) = (s.CenterZ - s.Length * 0.5f, s.CenterZ + s.Length * 0.5f);
            if (Get<RunningGear>()?.Def.Articulation is not { } h || h.Z <= back || h.Z >= ahead) return (Footprint, null);
            var half = s.Width * 0.5f;
            var rear = new Obb(LocalToWorld(0f, (back + h.Z) * 0.5f), new Vector2(half, (h.Z - back) * 0.5f), Heading);
            var (center, heading) = PartToWorld(0f, (h.Z + ahead) * 0.5f);
            return (rear, new Obb(center, new Vector2(half, (ahead - h.Z) * 0.5f), heading));
        }
    }

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
