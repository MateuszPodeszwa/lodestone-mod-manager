using System.Collections.Concurrent;
using Lodestone.Application.Messaging;

namespace Lodestone.Infrastructure.Messaging;

/// <summary>
/// A thread-safe, in-process publish/subscribe bus (lightweight Mediator). Handlers are invoked
/// synchronously on the publishing thread; the UI layer marshals to the dispatcher as needed.
/// </summary>
public sealed class InMemoryMessageBus : IMessageBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly object _lock = new();

    public void Publish<TMessage>(TMessage message)
    {
        if (!_handlers.TryGetValue(typeof(TMessage), out List<Delegate>? handlers))
        {
            return;
        }

        Delegate[] snapshot;
        lock (_lock)
        {
            snapshot = handlers.ToArray();
        }

        foreach (Delegate handler in snapshot)
        {
            ((Action<TMessage>)handler)(message);
        }
    }

    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
    {
        List<Delegate> handlers = _handlers.GetOrAdd(typeof(TMessage), _ => []);
        lock (_lock)
        {
            handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                handlers.Remove(handler);
            }
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
