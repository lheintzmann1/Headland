namespace Headland.Core.Events;

/// <summary>Marker for everything published on the <see cref="EventBus"/>.</summary>
public interface IGameEvent;

/// <summary>
/// Typed publish/subscribe for game events. Handlers run synchronously on the simulation thread, in subscription
/// order; subscribing or unsubscribing from inside a handler takes effect from the next event.
/// </summary>
public sealed class EventBus
{
    private readonly Dictionary<Type, Delegate[]> _handlers = new();
    private Action<IGameEvent>[] _any = [];

    /// <summary>Calls <paramref name="handler"/> for every <typeparamref name="T"/> event. Dispose to unsubscribe.</summary>
    public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
    {
        _handlers[typeof(T)] = [.. _handlers.GetValueOrDefault(typeof(T)) ?? [], handler];
        return new Subscription(() => _handlers[typeof(T)] = _handlers[typeof(T)].Where(h => !ReferenceEquals(h, handler)).ToArray());
    }

    /// <summary>Calls <paramref name="handler"/> for every event, after the typed handlers (logging, scripting).</summary>
    public IDisposable SubscribeAll(Action<IGameEvent> handler)
    {
        _any = [.. _any, handler];
        return new Subscription(() => _any = _any.Where(h => !ReferenceEquals(h, handler)).ToArray());
    }

    public void Publish<T>(T e) where T : IGameEvent
    {
        if (_handlers.TryGetValue(typeof(T), out var handlers))
            foreach (var h in handlers)
                ((Action<T>)h)(e);
        foreach (var h in _any) h(e);
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose()
        {
            _remove?.Invoke();
            _remove = null;
        }
    }
}
