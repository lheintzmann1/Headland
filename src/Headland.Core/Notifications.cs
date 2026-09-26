using Headland.Core.Content;
using Headland.Core.Events;

namespace Headland.Core;

public enum Severity { Info, Good, Warning }

public sealed record Notification(string Text, Severity Severity, double RealTime);

/// <summary>Short HUD messages. The presentation drains and displays them.</summary>
public sealed class Notifications
{
    private readonly List<Notification> _items = [];
    private readonly Dictionary<string, double> _lastByText = new();

    public double Now { get; set; }

    public IReadOnlyList<Notification> Items => _items;

    /// <summary>Posts a message, de-duplicating identical text within <paramref name="cooldown"/> seconds.</summary>
    public void Post(string text, Severity severity = Severity.Info, double cooldown = 2.0)
    {
        if (_lastByText.TryGetValue(text, out var last) && Now - last < cooldown) return;
        _lastByText[text] = Now;
        _items.Add(new Notification(text, severity, Now));
        if (_items.Count > 32) _items.RemoveAt(0);
    }

    public void Expire(double maxAge) => _items.RemoveAll(n => Now - n.RealTime > maxAge);

    /// <summary>Posts a message for the events a player wants to hear about (sales, helpers, hitching).</summary>
    public void Follow(EventBus events, ContentDatabase content)
    {
        string Amount(string fillType, float amount)
        {
            var ft = content.FillTypes[fillType];
            return $"{amount:N0} {ft.Unit} {ft.Name}";
        }

        events.Subscribe<FillSold>(e => Post($"Sold {Amount(e.FillType, e.Amount)} for ${e.Income:N0}", Severity.Good, 0));
        events.Subscribe<FillStored>(e => Post($"Stored {Amount(e.FillType, e.Amount)} in {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<FillLoaded>(e => Post($"Loaded {Amount(e.FillType, e.Amount)} from {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<MachineDelivered>(e => Post($"{e.Machine.Def.Name} delivered at {e.Poi.Name}", Severity.Good, 0));
        events.Subscribe<FillBought>(e => Post($"Bought {Amount(e.FillType, e.Amount)} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<MachineRepaired>(e => Post($"Repaired {e.Machine.Def.Name} for ${e.Cost:N0}", Severity.Good));
        events.Subscribe<MachineWashed>(e => Post(e.Cost > 0.5f ? $"Washed {e.Machine.Def.Name} for ${e.Cost:N0}" : $"Washed {e.Machine.Def.Name}", Severity.Good));
        events.Subscribe<ImplementAttached>(e => Post($"Attached {e.Implement.Def.Name}", Severity.Good));
        events.Subscribe<ImplementDetached>(e => Post($"Detached {e.Implement.Def.Name}"));
        events.Subscribe<HelperHired>(e => Post($"Helper started on {e.Field.Label} ({e.Field.AreaHa:0.00} ha)", Severity.Good));
        events.Subscribe<HelperDismissed>(e =>
        {
            switch (e.End)
            {
                case HelperEnd.Finished: Post($"Helper finished {e.Field.Label}", Severity.Good); break;
                case HelperEnd.Stopped: Post($"Helper stopped on {e.Field.Label}: {e.Reason}", Severity.Warning); break;
                default: Post("Helper dismissed"); break;
            }
        });
    }
}
