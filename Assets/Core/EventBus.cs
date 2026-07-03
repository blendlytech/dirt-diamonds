namespace DirtAndDiamonds.Core;

/// <summary>
/// The asynchronous event dispatcher bus — per CLAUDE.md the ONLY legal
/// communication channel between the Life simulation and the Baseball
/// simulation (alongside the database itself). Neither side may reference the
/// other's types; both reference Core and talk through struct events.
///
/// Dispatch is deferred: <see cref="Publish{T}"/> only enqueues. Events run
/// when the driver — <see cref="GameManager"/> once per frame, or a headless
/// harness loop — calls <see cref="DispatchPending"/>. Publication order is
/// preserved globally across event types (single FIFO), so a subscriber never
/// observes day N+1 before another subscriber has seen day N.
///
/// Allocation profile: the publish path allocates nothing once a channel's
/// queue has grown to its steady-state capacity — struct payloads live inside
/// typed <see cref="Queue{T}"/>s (no boxing) and the global order queue holds
/// channel references. Subscribe/unsubscribe rebuild a copy-on-write handler
/// array and are meant for wiring time (_Ready/_ExitTree), not hot loops.
///
/// Threading: Publish/Subscribe/Unsubscribe are safe from any thread (the
/// Gritty Event dispatcher will publish from its polling loop in Phase 7);
/// DispatchPending is single-consumer — the sim/main thread. Handlers run
/// outside the internal lock, so a handler may publish follow-up events; they
/// join the same drain, bounded by <see cref="MaxEventsPerPump"/> to turn an
/// accidental event cycle into a loud failure instead of a hang.
/// </summary>
public sealed class EventBus
{
    /// <summary>Cascade bound per <see cref="DispatchPending"/> call — generous for a year of daily ticks, far below an infinite loop.</summary>
    public const int MaxEventsPerPump = 1_000_000;

    private readonly object _gate = new();
    private readonly Dictionary<Type, Channel> _channels = new();
    private readonly Queue<Channel> _dispatchOrder = new(256);
    private bool _pumping;

    /// <summary>Events queued and not yet dispatched.</summary>
    public int PendingCount
    {
        get { lock (_gate) { return _dispatchOrder.Count; } }
    }

    public void Subscribe<T>(Action<T> handler) where T : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            GetChannel<T>().Add(handler);
        }
    }

    /// <summary>Removes a handler; returns false when it was not subscribed. Takes effect for events dispatched after the call.</summary>
    public bool Unsubscribe<T>(Action<T> handler) where T : struct, IGameEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            return _channels.TryGetValue(typeof(T), out Channel? channel)
                && ((Channel<T>)channel).Remove(handler);
        }
    }

    /// <summary>
    /// Enqueues an event for the next <see cref="DispatchPending"/>. Never
    /// invokes handlers inline — publishers cannot be reentered by their
    /// subscribers, and a mid-batch publisher cannot drag a subscriber's
    /// database writes into its own transaction.
    /// </summary>
    public void Publish<T>(in T gameEvent) where T : struct, IGameEvent
    {
        lock (_gate)
        {
            Channel<T> channel = GetChannel<T>();
            channel.Enqueue(in gameEvent);
            _dispatchOrder.Enqueue(channel);
        }
    }

    /// <summary>
    /// Drains the queue in publication order, including events published by
    /// handlers during the drain. Returns the number dispatched. Reentrant
    /// calls (a handler pumping the bus) throw.
    /// </summary>
    public int DispatchPending()
    {
        lock (_gate)
        {
            if (_pumping)
            {
                throw new InvalidOperationException(
                    "DispatchPending is not reentrant — a handler must publish, never pump.");
            }
            _pumping = true;
        }

        try
        {
            int dispatched = 0;
            while (true)
            {
                Channel channel;
                lock (_gate)
                {
                    if (_dispatchOrder.Count == 0)
                    {
                        break;
                    }
                    channel = _dispatchOrder.Dequeue();
                }

                channel.DispatchOne();

                if (++dispatched >= MaxEventsPerPump)
                {
                    throw new InvalidOperationException(
                        $"Dispatched {dispatched} events in one pump — likely an event cycle between handlers.");
                }
            }
            return dispatched;
        }
        finally
        {
            lock (_gate)
            {
                _pumping = false;
            }
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private Channel<T> GetChannel<T>() where T : struct, IGameEvent
    {
        if (!_channels.TryGetValue(typeof(T), out Channel? channel))
        {
            channel = new Channel<T>(_gate);
            _channels.Add(typeof(T), channel);
        }
        return (Channel<T>)channel;
    }

    private abstract class Channel
    {
        /// <summary>Dequeues this channel's oldest pending event and invokes its handlers.</summary>
        public abstract void DispatchOne();
    }

    private sealed class Channel<T> : Channel where T : struct, IGameEvent
    {
        private readonly object _gate;
        private readonly Queue<T> _pending = new(64);
        // Copy-on-write so DispatchOne can invoke without holding the lock and
        // a handler list mutated mid-dispatch never shifts under the iterator.
        private Action<T>[] _handlers = Array.Empty<Action<T>>();

        public Channel(object gate)
        {
            _gate = gate;
        }

        /// <summary>Caller must hold the gate.</summary>
        public void Enqueue(in T gameEvent) => _pending.Enqueue(gameEvent);

        /// <summary>Caller must hold the gate.</summary>
        public void Add(Action<T> handler)
        {
            var next = new Action<T>[_handlers.Length + 1];
            Array.Copy(_handlers, next, _handlers.Length);
            next[^1] = handler;
            _handlers = next;
        }

        /// <summary>Caller must hold the gate.</summary>
        public bool Remove(Action<T> handler)
        {
            int index = Array.IndexOf(_handlers, handler);
            if (index < 0)
            {
                return false;
            }
            var next = new Action<T>[_handlers.Length - 1];
            Array.Copy(_handlers, next, index);
            Array.Copy(_handlers, index + 1, next, index, next.Length - index);
            _handlers = next;
            return true;
        }

        public override void DispatchOne()
        {
            T gameEvent;
            Action<T>[] handlers;
            lock (_gate)
            {
                gameEvent = _pending.Dequeue();
                handlers = _handlers;
            }
            for (int i = 0; i < handlers.Length; i++)
            {
                handlers[i](gameEvent);
            }
        }
    }
}
