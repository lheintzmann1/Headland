using System.Text.Json.Serialization;
using Headland.Core.Content;
using Headland.Core.Input;
using Headland.Core.Machines;
using Headland.Core.Machines.Components;

namespace Headland.Core.Components;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AnimatedPartDef
{
    /// <summary>
    /// The keys a machine's parts move on: folding, lowering, or a switch of their own on the parts key or, on a
    /// machine that doesn't turn on, the turn-on key (FS: a plow rotates on it).
    /// </summary>
    public static readonly string[] Keys = [InputActions.Fold, InputActions.Lower, InputActions.MoveParts, InputActions.TurnOn];

    /// <summary>Unique on the entity; also the model node role it moves.</summary>
    public string Id { get; set; } = "";
    /// <summary>What the key hints call a part on a switch of its own ("markers"): "Move markers". Its id by default.</summary>
    public string? Name { get; set; }
    /// <summary>Its moved pose, from its rest pose as modeled: turned by [x, y, z] degrees about its pivot, shifted by [x, y, z] meters.</summary>
    public float[] RotationDeg { get; set; } = [0f, 0f, 0f];
    public float[] Offset { get; set; } = [0f, 0f, 0f];
    /// <summary>Time from rest to moved.</summary>
    public float Seconds { get; set; } = 2f;
    /// <summary>
    /// The key it moves on, on a machine: <c>fold</c>, folding for transport; <c>lower</c>, raised and lowered with the
    /// machine; <c>move_parts</c> (the default) or <c>turn_on</c>, on a switch of its own (a cover, a marker, a plow
    /// rotating). For the first two its rest pose is the working one.
    /// </summary>
    public string? Key { get; set; }
    /// <summary>
    /// A part that folds, on a machine that lowers: where it stands (0 … 1) unfolded but raised, the lower key moving it
    /// between there and the working pose (FS: foldMiddleAnimTime), so both keys share its motion. 0: unfolded, it
    /// stands in its working pose.
    /// </summary>
    public float Middle { get; set; }
    /// <summary>Moves while someone is in this area, and back once they left (a door opening), rather than on a key.</summary>
    public TriggerDef? Trigger { get; set; }
    /// <summary>A support leg (FS: support animations): moved while the machine stands unhitched, back once it's hitched.</summary>
    public bool Support { get; set; }

    /// <summary>The key it moves on; none for a part following a trigger or the hitch.</summary>
    [JsonIgnore]
    public string? MovesOn => Trigger != null || Support ? null : Key ?? InputActions.MoveParts;

    [JsonIgnore]
    public bool Folds => MovesOn == InputActions.Fold;

    [JsonIgnore]
    public bool Lowers => MovesOn == InputActions.Lower;

    /// <summary>Moved by the driver on a switch of its own (a cover, a marker): it neither folds nor lowers.</summary>
    [JsonIgnore]
    public bool Commanded => MovesOn != null && !Folds && !Lowers;

    /// <summary>Moved as the machine is lowered and raised: a part lowering, or one folding with a middle pose.</summary>
    [JsonIgnore]
    public bool MovesDown => Lowers || Folds && Middle > 0f;

    /// <summary>The seconds it takes to go down to work once its machine is lowered.</summary>
    [JsonIgnore]
    public float LowerSeconds => Lowers ? Seconds : Folds ? Seconds * Middle : 0f;
}

/// <summary>
/// Parts that move between two poses: the wings of an implement folding for transport, a boom going down as its
/// machine is lowered, covers and markers the driver moves, support legs going down when the machine is unhitched, a
/// shed's door opening as someone comes by (FS: Foldable, AnimatedVehicle, PlaceableAnimatedObjects). A folded
/// machine (or one still unfolding) does not work and cannot go down.
/// </summary>
public sealed class AnimatedPartsDef : ComponentDef
{
    public AnimatedPartDef[] Parts { get; set; } = [];
    /// <summary>Comes folded (from the shop, on the map).</summary>
    public bool StartFolded { get; set; }

    public override IEnumerable<string> Roles => Parts.Select(p => p.Id);

    /// <summary>The fold key, and the keys its parts moving on switches of their own go on.</summary>
    public override IEnumerable<string> Toggles =>
        Parts.Where(p => p.Folds || p.Commanded).Select(p => p.MovesOn!).Where(AnimatedPartDef.Keys.Contains).Distinct();

    /// <summary>The seconds its parts take to go down to work once the machine is lowered.</summary>
    [JsonIgnore]
    public float LowerSeconds => Parts.Select(p => p.LowerSeconds).DefaultIfEmpty(0f).Max();

    internal override IEnumerable<string> Errors(EntityDef owner, ContentDatabase content)
    {
        if (Parts.Length == 0) yield return "needs parts";
        foreach (var id in Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key)) yield return $"part '{id}' is defined more than once";
        var lowers = owner.Get<AttachableDef>() is { Lowerable: true };
        var turnsOn = owner.Components.Any(c => c != this && c.Toggles.Contains(InputActions.TurnOn));
        foreach (var p in Parts)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) yield return "a part has no id";
            if (p.Seconds <= 0f) yield return $"part '{p.Id}': seconds must be > 0";
            if (p.RotationDeg.Length != 3 || p.Offset.Length != 3) yield return $"part '{p.Id}': rotationDeg and offset are [x, y, z]";
            if (p.Key != null && !AnimatedPartDef.Keys.Contains(p.Key)) yield return $"part '{p.Id}': key must be {string.Join(", ", AnimatedPartDef.Keys)}";
            if (p.Key != null && (p.Trigger != null || p.Support)) yield return $"part '{p.Id}': a part moving on a key follows neither a trigger nor the hitch";
            if (p.Support && p.Trigger != null) yield return $"part '{p.Id}': a support leg moves with the hitch, not by a trigger";
            if (p.MovesOn != null && owner is not MachineDef) yield return $"part '{p.Id}': only a machine's parts move on keys; give it a trigger";
            if (p.Support && owner.Get<AttachableDef>() == null) yield return $"part '{p.Id}': only machines that hitch have support legs";
            if (p.Middle is < 0f or >= 1f) yield return $"part '{p.Id}': middle must be in [0, 1)";
            else if (p.Middle > 0f && !p.Folds) yield return $"part '{p.Id}': middle is for parts that fold";
            if (p.MovesDown && !lowers && owner is MachineDef) yield return $"part '{p.Id}': only a machine that lowers (attachable lowerable) moves parts as it's lowered";
            if (p.MovesOn == InputActions.TurnOn && turnsOn) yield return $"part '{p.Id}': the turn-on key turns the machine on; put the part on another key";
            if (p.Trigger?.Error() is { } error) yield return $"part '{p.Id}' trigger: {error}";
        }
        if (StartFolded && !Parts.Any(p => p.Folds)) yield return "startFolded needs parts that fold";
    }

    internal override Component Create(Entity owner) => new AnimatedParts(owner, this);
}

/// <summary>
/// A part's pose: 0 at rest … 1 moved. <see cref="Target"/> is where it's headed: moved (true) or at rest, folded or
/// not for a part that folds (unfolded, it stands at its middle pose while its machine is raised), raised or not for
/// one that lowers.
/// </summary>
public sealed class AnimatedPart(AnimatedPartDef def)
{
    public AnimatedPartDef Def { get; } = def;
    public bool Target { get; set; }
    public float Position { get; set; }

    /// <summary>The pose it's headed for, its machine lowered to work (<paramref name="down"/>) or not.</summary>
    internal float Goal(bool down) => Def.Folds && !Target ? (down ? 0f : Def.Middle) : Target ? 1f : 0f;
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
        // Folding parts start folded when it comes folded; support legs start down, as it stands unhitched; the rest
        // as for a machine raised.
        Parts = def.Parts.Select(p => new AnimatedPart(p) { Target = p.Folds && def.StartFolded || p.Support || p.Lowers }).ToArray();
        foreach (var p in Parts) p.Position = p.Goal(false);
    }

    public IReadOnlyList<AnimatedPart> Parts { get; }

    public AnimatedPart? Part(string id) => Parts.FirstOrDefault(p => p.Def.Id == id);

    public bool CanFold => Parts.Any(p => p.Def.Folds);

    /// <summary>Folded or folding for transport. Folding raises it.</summary>
    public bool Folded
    {
        get => Parts.Any(p => p.Def.Folds && p.Target);
        set
        {
            foreach (var p in Parts.Where(p => p.Def.Folds)) p.Target = value;
            if (value && Owner.Get<Attachable>() is { } a) a.Lowered = false;
        }
    }

    /// <summary>Unfolded, its parts that fold at least down to their middle pose: it may go down.</summary>
    public bool Unfolded => Parts.All(p => !p.Def.Folds || !p.Target && p.Position <= p.Def.Middle);

    /// <summary>Its parts that fold and lower all in their working pose: unfolded, and down if lowered.</summary>
    public bool InWorkingPose => Parts.All(p => !(p.Def.Folds || p.Def.Lowers) || p is { Target: false, Position: 0f });

    /// <summary>
    /// A machine's fold key when it folds for transport ("Fold sprayer"), and the keys its parts moving on switches of
    /// their own go on ("Move markers"), or its own words.
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Owner is not Machine machine) return;
        if (CanFold)
        {
            var (fold, unfold) = Def.WordsFor(InputActions.Fold, machine.Def.Named("Fold"), machine.Def.Named("Unfold"));
            actions.Toggle(InputActions.Fold, Folded, fold, unfold, folded =>
            {
                Folded = folded;
                return null;
            });
        }
        foreach (var key in Parts.Where(p => p.Def.Commanded).GroupBy(p => p.Def.MovesOn!))
        {
            var parts = key.ToList();
            var names = string.Join(", ", parts.Select(p => p.Def.Name ?? p.Def.Id).Distinct());
            var (move, back) = Def.WordsFor(key.Key, $"Move {names}", $"Move {names} back");
            actions.Toggle(key.Key, parts.Any(p => p.Target), move, back, moved =>
            {
                foreach (var p in parts) p.Target = moved;
                return null;
            });
        }
    }

    /// <summary>Moves a part moved on a switch of its own (<see cref="AnimatedPartDef.Commanded"/>) to its moved pose (true) or back.</summary>
    public bool Move(string id, bool moved)
    {
        if (Part(id) is not { Def.Commanded: true } p) return false;
        p.Target = moved;
        return true;
    }

    internal override void Update(Simulation sim, float dt)
    {
        // Lowered, it goes down once unfolded.
        var down = Owner.Get<Attachable>() is { Lowered: true } && Unfolded;
        foreach (var p in Parts)
        {
            if (p.Def.Trigger is { } trigger) p.Target = trigger.Occupied(Owner, sim);
            else if (p.Def.Support) p.Target = Owner is Machine { Parent: null };
            else if (p.Def.Lowers) p.Target = !down;
            p.Position = MathUtil.MoveToward(p.Position, p.Goal(down), dt / p.Def.Seconds);
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
