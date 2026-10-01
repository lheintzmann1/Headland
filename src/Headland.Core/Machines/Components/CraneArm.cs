using Headland.Core.Components;
using System.Numerics;
using Headland.Core.Content;
using Headland.Core.Input;

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
    /// <summary>
    /// The tool keys that move it (FS: a moving tool's axis): "y", up and down, or "x", left and right (up and left
    /// moving it positive); none, only the machine does.
    /// </summary>
    public string? Control { get; set; }
    /// <summary>The control group it's in (FS: controlGroups), from 1: the select key steps through a machine's groups.</summary>
    public int Group { get; set; } = 1;
    /// <summary>Its keys move it the other way.</summary>
    public bool Invert { get; set; }
}

/// <summary>
/// Two pitch joints moved by their tip (FS: easyArmControl): with the crane's tip control on, the first one's keys move
/// the tip up and down and the second one's in and out, the joints following.
/// </summary>
public sealed class CraneIkDef
{
    public string[] Joints { get; set; } = [];
    /// <summary>The tip, [x, y, z] in the second joint's space.</summary>
    public float[] Tip { get; set; } = [0f, 0f, 1f];
    /// <summary>How fast the tip moves, m/s.</summary>
    public float Speed { get; set; } = 1f;
}

/// <summary>
/// A chain of joints, each moving the ones after it: a forestry crane, a loader's arm and tool. The driver moves them
/// with the tool keys, a control group at a time; the machine moves them to targets. Tools and ropes hang on their
/// frames.
/// </summary>
public sealed class CraneArmDef : MachineComponentDef
{
    public CraneJointDef[] Joints { get; set; } = [];
    /// <summary>The names of its control groups, from the first ("arm", "grab"): the select key's hint shows them.</summary>
    public string[] Groups { get; set; } = [];
    public CraneIkDef? Ik { get; set; }

    public override IEnumerable<string> Roles => Joints.Select(j => j.Id);

    /// <summary>How many control groups the select key steps through.</summary>
    public int GroupCount => Math.Max(Groups.Length, Joints.Where(j => j.Control != null).Select(j => j.Group).DefaultIfEmpty(1).Max());

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
            if (j.Control is not (null or "x" or "y")) yield return $"joint '{j.Id}': control must be x or y";
            if (j.Group < 1) yield return $"joint '{j.Id}': group must be >= 1";
        }
        foreach (var g in Joints.Where(j => j.Control != null).GroupBy(j => (j.Group, j.Control)).Where(g => g.Count() > 1))
            yield return $"joints {string.Join(", ", g.Select(j => $"'{j.Id}'"))} share the {g.Key.Control} keys of group {g.Key.Group}";
        if (Ik is { } ik)
        {
            var index = ik.Joints.Select(id => Array.FindIndex(Joints, j => j.Id == id)).ToArray();
            if (index is not [var a, var b] || a < 0 || b <= a) yield return "ik: needs two of its joints, in order";
            else if (Joints[a] is not { Axis: "pitch", Control: not null } || Joints[b] is not { Axis: "pitch", Control: not null })
                yield return "ik: its joints must pitch, with keys";
            if (ik.Tip.Length != 3 || ik.Speed <= 0f) yield return "ik: tip is [x, y, z] and speed > 0";
        }
    }

    internal override Component Create(Machine machine) => new CraneArm(machine, this);
}

public sealed class CraneJoint(CraneJointDef def)
{
    public CraneJointDef Def { get; } = def;
    /// <summary>Where it is and where it's heading (degrees, or meters for extend).</summary>
    public float Value { get; internal set; } = def.Rest;
    public float Target { get; internal set; } = def.Rest;

    /// <summary>Its pivot and motion, from the previous joint's frame (row vectors: parent = local × this).</summary>
    internal Matrix4x4 Local => LocalAt(Value);

    /// <summary>Its pivot and motion at <paramref name="value"/>.</summary>
    internal Matrix4x4 LocalAt(float value)
    {
        var o = Def.Offset.Length == 3 ? new Vector3(Def.Offset[0], Def.Offset[1], Def.Offset[2]) : Vector3.Zero;
        var motion = Def.Axis switch
        {
            "yaw" => Matrix4x4.CreateRotationY(value * MathUtil.Deg2Rad),
            "pitch" => Matrix4x4.CreateRotationX(-value * MathUtil.Deg2Rad),
            _ => Matrix4x4.CreateTranslation(0f, 0f, value),
        };
        return motion * Matrix4x4.CreateTranslation(o);
    }
}

public sealed class CraneArmSave
{
    /// <summary>By joint id: [value, target].</summary>
    public Dictionary<string, float[]> Joints { get; set; } = new();
}

public sealed class CraneArm(Machine machine, CraneArmDef def) : MachineComponent<CraneArmDef, CraneArmSave>(machine, def), IActionSource, IReadoutSource
{
    public IReadOnlyList<CraneJoint> Joints { get; } = def.Joints.Select(j => new CraneJoint(j)).ToArray();

    /// <summary>The tool keys move its tip rather than each joint (<see cref="CraneArmDef.Ik"/>). The driver's choice: not saved.</summary>
    public bool TipControl { get; set; }

    public IEnumerable<Readout> Readouts(Simulation sim)
    {
        if (TipControl) yield return new Status("Tip control", Tone.Info);
    }

    public CraneJoint? Joint(string id) => Joints.FirstOrDefault(j => j.Def.Id == id);

    /// <summary>
    /// The mouse's hint on a crane the driver moves ("Move front loader": the right button held, the mouse moves it), and
    /// the tip control key on a crane with one (<see cref="CraneArmDef.Ik"/>).
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Def.Joints.Any(j => j.Control != null)) actions.Add(InputActions.ToolMouse, Machine.Def.Named("Move"), () => { });
        if (Def.Ik == null) return;
        actions.Toggle(InputActions.ToolIk, TipControl, "Crane: move its tip", "Crane: move each joint", on =>
        {
            TipControl = on;
            return null;
        }, hinted: false);
    }

    /// <summary>Sends a joint toward <paramref name="value"/>, within its travel. False when there is no such joint.</summary>
    public bool MoveTo(string id, float value)
    {
        if (Joint(id) is not { } j) return false;
        j.Target = Math.Clamp(value, j.Def.Min, j.Def.Max);
        return true;
    }

    /// <summary>
    /// The tool keys held for <paramref name="dt"/> seconds (<paramref name="y"/> up, <paramref name="x"/> left, -1..1),
    /// on control group <paramref name="group"/>: its joints move at their speed, or with the tip control on, the tip.
    /// </summary>
    public void Drive(int group, float y, float x, float dt)
    {
        float Input(CraneJointDef d) => (d.Control == "y" ? y : d.Control == "x" ? x : 0f) * (d.Invert ? -1f : 1f);
        var ik = TipControl && Def.Ik is { } k ? k.Joints.Select(id => Joint(id)!).ToArray() : [];
        foreach (var j in Joints.Where(j => j.Def.Group == group && j.Def.Control != null && !ik.Contains(j)))
            if (Input(j.Def) is var input and not 0f)
                j.Target = Math.Clamp(j.Target + input * j.Def.Speed * dt, j.Def.Min, j.Def.Max);
        if (ik is not [var a, var b]) return;
        // The first joint's keys move the tip up, the second one's out (FS: easy arm control).
        var up = a.Def.Group == group ? Input(a.Def) : 0f;
        var @out = b.Def.Group == group ? Input(b.Def) : 0f;
        if (up != 0f || @out != 0f) MoveTip(a, b, new Vector2(@out, up) * (Def.Ik!.Speed * dt), dt);
    }

    /// <summary>
    /// Moves the tip by <paramref name="by"/> (out, up) in the plane <paramref name="a"/> pitches in, solving the two
    /// joints' targets (a few Newton steps), within their travel; slower where the joints can't turn that fast.
    /// </summary>
    private void MoveTip(CraneJoint a, CraneJoint b, Vector2 by, float dt)
    {
        var tip = new Vector3(Def.Ik!.Tip[0], Def.Ik.Tip[1], Def.Ik.Tip[2]);
        var joints = Joints.ToList();
        var (ia, ib) = (joints.IndexOf(a), joints.IndexOf(b));
        // The tip in a's parent frame, as (z out, y up), for the joints' targets.
        Vector2 Tip(float va, float vb)
        {
            var frame = Matrix4x4.Identity;
            for (var i = ib; i > ia; i--)
            {
                var j = Joints[i];
                frame *= j == b ? j.LocalAt(vb) : j.LocalAt(j.Target);
            }
            var p = Vector3.Transform(tip, frame * a.LocalAt(va));
            return new Vector2(p.Z, p.Y);
        }
        var goal = Tip(a.Target, b.Target) + by;
        var (ta, tb) = (a.Target, b.Target);
        for (var step = 0; step < 6; step++)
        {
            var at = Tip(ta, tb);
            var miss = goal - at;
            if (miss.Length() < 1e-4f) break;
            const float h = 0.01f;
            var da = (Tip(ta + h, tb) - at) / h;
            var db = (Tip(ta, tb + h) - at) / h;
            var det = da.X * db.Y - db.X * da.Y;
            if (MathF.Abs(det) < 1e-6f) return;
            ta = Math.Clamp(ta + (miss.X * db.Y - db.X * miss.Y) / det, a.Def.Min, a.Def.Max);
            tb = Math.Clamp(tb + (da.X * miss.Y - miss.X * da.Y) / det, b.Def.Min, b.Def.Max);
        }
        // No faster than the joints turn: the tip slows down rather than run ahead of them.
        var (sa, sb) = (ta - a.Target, tb - b.Target);
        var k = MathF.Min(1f, MathF.Min(Fits(sa, a.Def.Speed * dt), Fits(sb, b.Def.Speed * dt)));
        (a.Target, b.Target) = (a.Target + sa * k, b.Target + sb * k);

        static float Fits(float step, float most) => MathF.Abs(step) > most ? most / MathF.Abs(step) : 1f;
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

    /// <summary>The crane's tip (<see cref="CraneArmDef.Ik"/>) in the machine's space, or null without one.</summary>
    public Vector3? Tip => Def.Ik is { } ik ? ToMachine(ik.Joints[1], new Vector3(ik.Tip[0], ik.Tip[1], ik.Tip[2])) : null;

    internal override void Update(Simulation sim, float dt)
    {
        var moving = false;
        foreach (var j in Joints)
        {
            moving |= j.Value != j.Target;
            j.Value = MathUtil.MoveToward(j.Value, j.Target, j.Def.Speed * dt);
        }
        // What hangs on its joints (a loader's tool) goes with them.
        if (moving && Machine.Def.Joints.Any(j => j.Crane != null && Machine.Attached.ContainsKey(j.Id))) sim.Machines.UpdateChildren(Machine);
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
