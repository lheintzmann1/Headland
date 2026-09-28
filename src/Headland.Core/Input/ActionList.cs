namespace Headland.Core.Input;

/// <summary>What an action does now for the player: the label its key hint shows, and what pressing it does.</summary>
public sealed record ActionOffer(string Action, string Label, bool Hinted, Action Run);

/// <summary>
/// A component offering the player actions while they apply (FS: registerActionEvent): an implement's lower key
/// while it's hitched, a tipper's key in an unloading area. It's asked every time: what it adds is what applies now.
/// </summary>
public interface IActionSource
{
    void AddActions(ActionList actions, Simulation sim);
}

/// <summary>
/// The actions offered to the player now, one per action however many parts offer it. Commands offered by several
/// parts all run (each seeder changes seed). Toggles (lowered or raised, on or off) switch together: all to the state
/// none of them is in, so one key raises every lowered implement, or lowers them all when none is.
/// </summary>
public sealed class ActionList(Notifications notifications)
{
    private readonly List<Entry> _entries = [];
    private List<ActionOffer>? _offers;

    /// <summary>One part's offer: a command, or a toggle with its state and how to set it.</summary>
    private sealed record Entry(string Action, string Label, bool Hinted, Action? Run, bool On = false, string? OffLabel = null, Func<bool, string?>? Set = null);

    /// <summary>Offers a command; <paramref name="hinted"/> false keeps it out of the key hints (the help still lists it).</summary>
    public void Add(string action, string label, Action run, bool hinted = true)
    {
        _entries.Add(new Entry(action, label, hinted, run));
        _offers = null;
    }

    /// <summary>
    /// Offers a switch between two states: labeled <paramref name="turnOn"/> while none of the parts offering it is
    /// <paramref name="on"/>, <paramref name="turnOff"/> once one is. <paramref name="set"/> puts the part in a state,
    /// or says why it can't (the first reason is shown).
    /// </summary>
    public void Toggle(string action, bool on, string turnOn, string turnOff, Func<bool, string?> set, bool hinted = true)
    {
        _entries.Add(new Entry(action, turnOn, hinted, null, on, turnOff, set));
        _offers = null;
    }

    /// <summary>One offer per action, in the order the help lists the actions (others after, as offered).</summary>
    public IReadOnlyList<ActionOffer> Offers => _offers ??= Merge();

    public ActionOffer? Of(string action) => Offers.FirstOrDefault(o => o.Action == action);

    private List<ActionOffer> Merge()
    {
        var order = InputActions.Defs.Select(d => d.Id).ToList();
        return _entries
            .GroupBy(e => e.Action)
            .OrderBy(g => order.IndexOf(g.Key) is var i and >= 0 ? i : order.Count)
            .Select(Merge)
            .ToList();
    }

    private ActionOffer Merge(IGrouping<string, Entry> group)
    {
        var entries = group.ToList();
        var toggles = entries.Where(e => e.Set != null).ToList();
        var anyOn = toggles.Any(t => t.On);
        var shown = entries.FirstOrDefault(e => e.Hinted) ?? entries[0];
        var label = shown.Set != null && anyOn ? shown.OffLabel ?? shown.Label : shown.Label;
        return new ActionOffer(group.Key, label, entries.Any(e => e.Hinted), () =>
        {
            string? why = null;
            foreach (var t in toggles)
            {
                var cannot = t.Set!(!anyOn);
                why ??= cannot;
            }
            foreach (var c in entries.Where(e => e.Run != null)) c.Run!();
            if (why != null) notifications.Post(why);
        });
    }
}
