using Headland.Core.Components;
using Headland.Core.Content;
using Headland.Core.Input;

namespace Headland.Core.Machines.Components;

/// <summary>A cover over fill units: a hopper's lid, a trailer's tarp.</summary>
public sealed class CoverPartDef
{
    /// <summary>The parts of the machine's <c>animatedParts</c> it moves, open in their moved pose.</summary>
    public string[] Parts { get; set; } = [];
    /// <summary>The fill units it covers: nothing fills them while it's closed.</summary>
    public string[] FillUnits { get; set; } = [];
    /// <summary>Comes open (from the shop, on the map; FS: openOnBuy).</summary>
    public bool StartOpen { get; set; }
    /// <summary>Opens by itself at a fill trigger, and closes again once the machine leaves it (FS: autoReactToTrigger).</summary>
    public bool AutoOpen { get; set; } = true;
}

/// <summary>
/// Covers over fill units (FS: Cover), opened one at a time with the cover key: "Open cover", "Open next cover", "Close
/// cover". Nothing fills a unit while its cover is closed: a pipe, a silo's spout, a station. One opens by itself where a
/// station or its farm's silo could fill its units, closing again as the machine leaves if it had opened by itself,
/// and while its machine tips what it covers.
/// </summary>
public sealed class CoverDef : MachineComponentDef
{
    public CoverPartDef[] Covers { get; set; } = [];

    public override IEnumerable<string> HeldParts => Covers.SelectMany(c => c.Parts);

    /// <summary>The cover key, as a switch its words can name when there's one cover; several, it steps through them.</summary>
    public override IEnumerable<string> Toggles => Covers.Length == 1 ? [InputActions.Cover] : [];

    internal override IEnumerable<string> Errors(MachineDef machine, ContentDatabase content)
    {
        if (Covers.Length == 0) yield return "needs covers";
        var parts = machine.Get<AnimatedPartsDef>();
        for (var i = 0; i < Covers.Length; i++)
        {
            var c = Covers[i];
            var what = $"cover {i + 1}";
            if (c.Parts.Length == 0 || c.FillUnits.Length == 0) yield return $"{what}: needs parts and fillUnits";
            foreach (var id in c.Parts)
            {
                if (parts?.Parts.FirstOrDefault(p => p.Id == id) is not { } part) yield return $"{what}: part '{id}' missing in animatedParts";
                else if (part.HeldBy != Kind) yield return $"{what}: part '{id}' is moved by the {part.HeldBy}";
            }
            foreach (var unit in c.FillUnits.Where(u => !HasUnit(machine, u))) yield return $"{what}: fill unit '{unit}' missing";
            if (c.StartOpen && Covers.Take(i).Any(o => o.StartOpen)) yield return $"{what}: only one cover is open at a time";
        }
    }

    internal override Component Create(Machine machine) => new Cover(machine, this);
}

public sealed class CoverSave
{
    /// <summary>0: all closed; else the cover open, from 1.</summary>
    public int State { get; set; }
    /// <summary>It opened by itself at a fill trigger, and closes as the machine leaves.</summary>
    public bool Auto { get; set; }
}

public sealed class Cover : MachineComponent<CoverDef, CoverSave>, IActionSource
{
    /// <summary>At a fill trigger last tick: it reacts to going in and out.</summary>
    private bool _atTrigger;
    /// <summary>Its parts are where its state puts them (on the first tick for a new machine, or loaded).</summary>
    private bool _placed;

    public Cover(Machine machine, CoverDef def) : base(machine, def)
    {
        State = Array.FindIndex(def.Covers, c => c.StartOpen) + 1;
    }

    /// <summary>0: all closed; else the open cover, from 1 (FS: one open at a time).</summary>
    public int State { get; set; }

    /// <summary>It opened by itself at a fill trigger: it closes by itself as the machine leaves.</summary>
    public bool Auto { get; private set; }

    /// <summary>The cover over <paramref name="unit"/>, from 1, or 0 when none covers it.</summary>
    public int CoverOf(FillUnit unit) => Array.FindIndex(Def.Covers, c => c.FillUnits.Contains(unit.Def.Id)) + 1;

    /// <summary>A closed cover keeps fill out of <paramref name="unit"/>.</summary>
    public bool Shuts(FillUnit unit) => CoverOf(unit) > 0 && !(State > 0 && Def.Covers[State - 1].FillUnits.Contains(unit.Def.Id));

    /// <summary>
    /// The cover key: one cover opens and closes; several open one after the other, then all close (FS: "Open next
    /// cover").
    /// </summary>
    public void AddActions(ActionList actions, Simulation sim)
    {
        if (Def.Covers.Length == 1)
        {
            var (open, close) = Def.WordsFor(InputActions.Cover, "Open cover", "Close cover");
            actions.Toggle(InputActions.Cover, State > 0, open, close, on =>
            {
                Set(on ? 1 : 0);
                return null;
            });
            return;
        }
        var label = State == 0 ? "Open cover" : State == Def.Covers.Length ? "Close cover" : "Open next cover";
        actions.Add(InputActions.Cover, label, () => Set((State + 1) % (Def.Covers.Length + 1)));
    }

    /// <summary>The driver opens or closes it: it no longer closes by itself.</summary>
    private void Set(int state)
    {
        State = state;
        Auto = false;
    }

    internal override void Update(Simulation sim, float dt)
    {
        // Tipping what it covers opens it.
        if (Machine.Get<Tipper>() is { Tipping: true } tipper && CoverOf(tipper.Load) is var tipped and > 0 && State != tipped) State = tipped;

        // At a fill trigger it opens by itself, and closes again as it leaves if it had.
        var trigger = Def.Covers.Select((c, i) => (c, i)).FirstOrDefault(x =>
            x.c.AutoOpen && x.c.FillUnits.Any(u => Machine.Unit(u) is { } unit && sim.Pois.AtFillTrigger(Machine, unit)));
        var at = trigger.c != null;
        if (at && !_atTrigger && State != trigger.i + 1)
        {
            State = trigger.i + 1;
            Auto = true;
        }
        else if (!at && _atTrigger && Auto)
        {
            State = 0;
            Auto = false;
        }
        _atTrigger = at;

        if (Machine.Get<AnimatedParts>() is not { } parts) return;
        for (var i = 0; i < Def.Covers.Length; i++)
            foreach (var id in Def.Covers[i].Parts)
                parts.Hold(id, State == i + 1 ? 1f : 0f, now: !_placed);
        _placed = true;
    }

    protected override CoverSave Capture(ContentDatabase content) => new() { State = State, Auto = Auto };

    protected override void Restore(CoverSave save, SaveContext context)
    {
        State = Math.Clamp(save.State, 0, Def.Covers.Length);
        Auto = save.Auto && State > 0;
        _placed = true;
    }
}
