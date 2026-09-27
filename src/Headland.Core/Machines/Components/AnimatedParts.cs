using Headland.Core.Components;
using Headland.Core.Content;

namespace Headland.Core.Machines.Components;

public sealed class AnimatedPartDef
{
    /// <summary>Unique on the machine; also the model node role it moves.</summary>
    public string Id { get; set; } = "";
    /// <summary>Its moved pose, from its rest pose as modeled: turned by [x, y, z] degrees about its pivot, shifted by [x, y, z] meters.</summary>
    public float[] RotationDeg { get; set; } = [0f, 0f, 0f];
    public float[] Offset { get; set; } = [0f, 0f, 0f];
    /// <summary>Time from rest to moved.</summary>
    public float Seconds { get; set; } = 2f;
    /// <summary>Moves when the machine folds for transport, rather than on its own; the rest pose is the working one.</summary>
    public bool Fold { get; set; }
}

/// <summary>
/// Parts that move between two poses: covers, support legs, and the wings of an implement that folds for transport.
/// A folded machine (or one still unfolding) does not work and cannot go down; lowering it unfolds it first.
/// </summary>
public sealed class AnimatedPartsDef : MachineComponentDef
{
    public AnimatedPartDef[] Parts { get; set; } = [];
    /// <summary>Comes folded (from the shop, on the map).</summary>
    public bool StartFolded { get; set; }

    public override IEnumerable<string> Roles => Parts.Select(p => p.Id);

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Parts.Length == 0) yield return "needs parts";
        foreach (var id in Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"part '{id}' is defined more than once";
        foreach (var p in Parts)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) yield return "a part has no id";
            if (p.Seconds <= 0f) yield return $"part '{p.Id}': seconds must be > 0";
            if (p.RotationDeg.Length != 3 || p.Offset.Length != 3) yield return $"part '{p.Id}': rotationDeg and offset are [x, y, z]";
        }
        if (StartFolded && !Parts.Any(p => p.Fold)) yield return "startFolded needs parts that fold";
    }

    internal override Component Create(Machine machine) => new AnimatedParts(machine, this);
}

/// <summary>A part's pose: 0 at rest … 1 moved, heading for <see cref="Target"/>.</summary>
public sealed class AnimatedPart(AnimatedPartDef def)
{
    public AnimatedPartDef Def { get; } = def;
    public bool Target { get; set; }
    public float Position { get; set; }
}

public sealed class AnimatedPartsSave
{
    /// <summary>By part id: [target (0 or 1), position].</summary>
    public Dictionary<string, float[]> Parts { get; set; } = new();
}

public sealed class AnimatedParts : MachineComponent<AnimatedPartsDef, AnimatedPartsSave>
{
    public AnimatedParts(Machine machine, AnimatedPartsDef def) : base(machine, def)
    {
        Parts = def.Parts.Select(p => new AnimatedPart(p) { Target = p.Fold && def.StartFolded, Position = p.Fold && def.StartFolded ? 1f : 0f }).ToArray();
    }

    public IReadOnlyList<AnimatedPart> Parts { get; }

    public AnimatedPart? Part(string id) => Parts.FirstOrDefault(p => p.Def.Id == id);

    public bool CanFold => Parts.Any(p => p.Def.Fold);

    /// <summary>Folded or folding for transport. Folding raises it.</summary>
    public bool Folded
    {
        get => Parts.Any(p => p.Def.Fold && p.Target);
        set
        {
            foreach (var p in Parts.Where(p => p.Def.Fold)) p.Target = value;
            if (value && Machine.Get<Attachable>() is { } a) a.Lowered = false;
        }
    }

    /// <summary>Fully unfolded: in its working pose.</summary>
    public bool Unfolded => Parts.All(p => !p.Def.Fold || p is { Target: false, Position: 0f });

    /// <summary>Moves a part that doesn't fold to its moved pose (true) or back.</summary>
    public bool Move(string id, bool moved)
    {
        if (Part(id) is not { Def.Fold: false } p) return false;
        p.Target = moved;
        return true;
    }

    internal override void Update(Simulation sim, float dt)
    {
        foreach (var p in Parts) p.Position = MathUtil.MoveToward(p.Position, p.Target ? 1f : 0f, dt / p.Def.Seconds);
    }

    protected override AnimatedPartsSave Capture(ContentDatabase content) =>
        new() { Parts = Parts.ToDictionary(p => p.Def.Id, p => new[] { p.Target ? 1f : 0f, p.Position }) };

    protected override void Restore(AnimatedPartsSave save, SaveContext context)
    {
        foreach (var (id, state) in save.Parts)
        {
            if (Part(id) is not { } p || state is not [var target, var position]) continue;
            p.Target = target > 0.5f;
            p.Position = Math.Clamp(position, 0f, 1f);
        }
    }
}
