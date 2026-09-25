namespace FarmSim.Core;

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
}
