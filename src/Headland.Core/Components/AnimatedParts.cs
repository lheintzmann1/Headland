using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Components;

public sealed class AnimatedPartDef
{
    /// <summary>Unique on the entity; also the model node role it moves.</summary>
    public string Id { get; set; } = "";
    /// <summary>What the player calls it in key hints ("markers"); its id by default.</summary>
    public string? Name { get; set; }
    /// <summary>Its moved pose, from its rest pose as modeled: turned by [x, y, z] degrees about its pivot, shifted by [x, y, z] meters.</summary>
    public float[] RotationDeg { get; set; } = [0f, 0f, 0f];
    public float[] Offset { get; set; } = [0f, 0f, 0f];
    /// <summary>Time from rest to moved.</summary>
    public float Seconds { get; set; } = 2f;
    /// <summary>Moves when the machine folds for transport, rather than on its own; the rest pose is the working one.</summary>
    public bool Fold { get; set; }
    /// <summary>Moves while someone is in this area, and back once they left (a door opening), rather than on command.</summary>
    public TriggerDef? Trigger { get; set; }
    /// <summary>A support leg (FS: support animations): moved while the machine stands unhitched, back once it's hitched.</summary>
    public bool Support { get; set; }

    /// <summary>Moved by the driver's key: it neither folds, nor follows a trigger or the hitch (a cover, a marker).</summary>
    public bool Commanded => !Fold && Trigger == null && !Support;
}

/// <summary>
/// Parts that move between two poses: covers and markers the driver moves, support legs going down when the machine is
/// unhitched, the wings of an implement that folds for transport, a shed's door opening as someone comes by (FS:
/// PlaceableAnimatedObjects). A folded machine (or one still unfolding) does not work and cannot go down.
/// </summary>
public sealed class AnimatedPartsDef : ComponentDef
{
    public AnimatedPartDef[] Parts { get; set; } = [];
    /// <summary>Comes folded (from the shop, on the map).</summary>
    public bool StartFolded { get; set; }

    public override IEnumerable<string> Roles => Parts.Select(p => p.Id);

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        if (Parts.Length == 0) yield return "needs parts";
        foreach (var id in Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"part '{id}' is defined more than once";
        foreach (var p in Parts)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) yield return "a part has no id";
            if (p.Seconds <= 0f) yield return $"part '{p.Id}': seconds must be > 0";
            if (p.RotationDeg.Length != 3 || p.Offset.Length != 3) yield return $"part '{p.Id}': rotationDeg and offset are [x, y, z]";
            if (p.Fold && owner is not MachineDef) yield return $"part '{p.Id}': only machines fold";
            if (p.Fold && p.Trigger != null) yield return $"part '{p.Id}': a part that folds moves with the machine, not by a trigger";
            if (p.Support && (p.Fold || p.Trigger != null)) yield return $"part '{p.Id}': a support leg moves with the hitch, neither folds nor follows a trigger";
            if (p.Support && owner.Get<AttachableDef>() == null) yield return $"part '{p.Id}': only machines that hitch have support legs";
            if (p.Trigger?.Error() is { } error) yield return $"part '{p.Id}' trigger: {error}";
        }
        if (StartFolded && !Parts.Any(p => p.Fold)) yield return "startFolded needs parts that fold";
    }

    internal override Component Create(Entity owner) => new AnimatedParts(owner, this);
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

public sealed class AnimatedParts : Component<AnimatedPartsDef, AnimatedPartsSave>, IActionSource
{
    public AnimatedParts(Entity owner, AnimatedPartsDef def) : base(owner, def)
    {
        // Folded parts start folded when it comes folded; support legs start down, as it stands unhitched.
        Parts = def.Parts.Select(p => p.Fold && def.StartFolded || p.Support ? new AnimatedPart(p) { Target = true, Position = 1f } : new AnimatedPart(p)).ToArray();
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
            if (value && Owner.Get<Attachable>() is { } a) a.Lowered = false;
        }
    }

    /// <summary>Fully unfolded: in its working pose.</summary>
    public bool Unfolded => Parts.All(p => !p.Def.Fold || p is { Target: false, Position: 0f });

    /// <summary>A machine's fold key when it folds for transport, and the key moving its parts moved on command.</summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Owner is not Machine) return;
        if (CanFold)
            actions.Toggle(InputActions.Fold, Folded, "Fold", "Unfold", fold =>
            {
                Folded = fold;
                return null;
            });
        var commanded = Parts.Where(p => p.Def.Commanded).ToList();
        if (commanded.Count == 0) return;
        var names = string.Join(", ", commanded.Select(p => p.Def.Name ?? p.Def.Id).Distinct());
        actions.Toggle(InputActions.MoveParts, commanded.Any(p => p.Target), $"Move the {names}", $"Move the {names} back", moved =>
        {
            foreach (var p in commanded) p.Target = moved;
            return null;
        });
    }

    /// <summary>Moves a part moved on command (<see cref="AnimatedPartDef.Commanded"/>) to its moved pose (true) or back.</summary>
    public bool Move(string id, bool moved)
    {
        if (Part(id) is not { Def.Commanded: true } p) return false;
        p.Target = moved;
        return true;
    }

    internal override void Update(Simulation sim, float dt)
    {
        foreach (var p in Parts)
        {
            if (p.Def.Trigger is { } trigger) p.Target = trigger.Occupied(Owner, sim);
            else if (p.Def.Support) p.Target = Owner is Machine { Parent: null };
            p.Position = MathUtil.MoveToward(p.Position, p.Target ? 1f : 0f, dt / p.Def.Seconds);
        }
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
