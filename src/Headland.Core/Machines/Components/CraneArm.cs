using System.Numerics;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class CraneJointDef
{
    public static readonly string[] Axes = ["yaw", "pitch", "extend"];

    /// <summary>Unique on the machine; also the model node role it moves.</summary>
    public string Id { get; set; } = "";
    /// <summary>
    /// How it moves: "yaw" turns about the vertical (positive to the left), "pitch" lifts about the left axis
    /// (positive up), "extend" slides out along its own +z.
    /// </summary>
    public string Axis { get; set; } = "pitch";
    /// <summary>Its pivot [x, y, z]: in the machine's space for the first joint, else in the previous joint's.</summary>
    public float[] Offset { get; set; } = [0f, 0f, 0f];
    /// <summary>Travel limits and rest position (degrees, or meters for extend), and how fast it moves per second.</summary>
    public float Min { get; set; } = -45f;
    public float Max { get; set; } = 45f;
    public float Rest { get; set; }
    public float Speed { get; set; } = 30f;
}

/// <summary>
/// A chain of joints, each moving the ones after it: a forestry crane, a loader's boom and bucket. The joints are
/// driven to targets (directly, or by inverse kinematics later); tools and ropes hang on their frames.
/// </summary>
public sealed class CraneArmDef : ComponentDef
{
    public CraneJointDef[] Joints { get; set; } = [];

    public override IEnumerable<string> Roles => Joints.Select(j => j.Id);

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Joints.Length == 0) yield return "needs joints";
        foreach (var id in Joints.GroupBy(j => j.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"joint '{id}' is defined more than once";
        foreach (var j in Joints)
        {
            if (string.IsNullOrWhiteSpace(j.Id)) yield return "a joint has no id";
            if (!CraneJointDef.Axes.Contains(j.Axis)) yield return $"joint '{j.Id}': axis must be yaw, pitch or extend";
            if (j.Min > j.Rest || j.Rest > j.Max) yield return $"joint '{j.Id}': needs min <= rest <= max";
            if (j.Speed <= 0f) yield return $"joint '{j.Id}': speed must be > 0";
            if (j.Offset.Length != 3) yield return $"joint '{j.Id}': offset is [x, y, z]";
        }
    }

    internal override MachineComponent Create(Machine machine) => new CraneArm(machine, this);
}

public sealed class CraneJoint(CraneJointDef def)
{
    public CraneJointDef Def { get; } = def;
    /// <summary>Where it is and where it's heading (degrees, or meters for extend).</summary>
    public float Value { get; internal set; } = def.Rest;
    public float Target { get; internal set; } = def.Rest;

    /// <summary>Its pivot and motion, from the previous joint's frame (row vectors: parent = local × this).</summary>
    internal Matrix4x4 Local
    {
        get
        {
            var o = Def.Offset.Length == 3 ? new Vector3(Def.Offset[0], Def.Offset[1], Def.Offset[2]) : Vector3.Zero;
            var motion = Def.Axis switch
            {
                "yaw" => Matrix4x4.CreateRotationY(Value * MathUtil.Deg2Rad),
                "pitch" => Matrix4x4.CreateRotationX(-Value * MathUtil.Deg2Rad),
                _ => Matrix4x4.CreateTranslation(0f, 0f, Value),
            };
            return motion * Matrix4x4.CreateTranslation(o);
        }
    }
}

public sealed class CraneArmSave
{
    /// <summary>By joint id: [value, target].</summary>
    public Dictionary<string, float[]> Joints { get; set; } = new();
}

public sealed class CraneArm(Machine machine, CraneArmDef def) : MachineComponent<CraneArmDef, CraneArmSave>(machine, def)
{
    public IReadOnlyList<CraneJoint> Joints { get; } = def.Joints.Select(j => new CraneJoint(j)).ToArray();

    public CraneJoint? Joint(string id) => Joints.FirstOrDefault(j => j.Def.Id == id);

    /// <summary>Sends a joint toward <paramref name="value"/>, within its travel. False when there is no such joint.</summary>
    public bool MoveTo(string id, float value)
    {
        if (Joint(id) is not { } j) return false;
        j.Target = Math.Clamp(value, j.Def.Min, j.Def.Max);
        return true;
    }

    /// <summary>
    /// The frame of joint <paramref name="id"/> (after its motion) in the machine's space: a point p in the joint's
    /// space is at p × frame. Null or unknown: the machine's own.
    /// </summary>
    public Matrix4x4 Frame(string? id)
    {
        var frame = Matrix4x4.Identity;
        var k = Joints.Count - 1;
        while (k >= 0 && Joints[k].Def.Id != id) k--;
        for (var i = k; i >= 0; i--) frame *= Joints[i].Local;
        return frame;
    }

    /// <summary>A point given in joint <paramref name="id"/>'s space, in the machine's (x left, y up, z forward).</summary>
    public Vector3 ToMachine(string? id, Vector3 local) => Vector3.Transform(local, Frame(id));

    internal override void Update(Simulation sim, float dt)
    {
        foreach (var j in Joints) j.Value = MathUtil.MoveToward(j.Value, j.Target, j.Def.Speed * dt);
    }

    protected override CraneArmSave Capture(ContentDatabase content) =>
        new() { Joints = Joints.ToDictionary(j => j.Def.Id, j => new[] { j.Value, j.Target }) };

    protected override void Restore(CraneArmSave save, SaveContext context)
    {
        foreach (var (id, state) in save.Joints)
        {
            if (Joint(id) is not { } j || state is not [var value, var target]) continue;
            j.Value = Math.Clamp(value, j.Def.Min, j.Def.Max);
            j.Target = Math.Clamp(target, j.Def.Min, j.Def.Max);
        }
    }
}
