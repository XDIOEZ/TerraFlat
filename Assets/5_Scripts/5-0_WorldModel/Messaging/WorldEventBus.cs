using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    public sealed class WorldEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers =
            new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            Type eventType = typeof(TEvent);
            if (!_handlers.TryGetValue(eventType, out List<Delegate> handlers))
            {
                handlers = new List<Delegate>();
                _handlers.Add(eventType, handlers);
            }
            if (!handlers.Contains(handler))
                handlers.Add(handler);
            return new Subscription<TEvent>(this, handler);
        }

        public void Publish<TEvent>(TEvent worldEvent)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers) || handlers.Count == 0)
                return;

            Delegate[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                ((Action<TEvent>)snapshot[i]).Invoke(worldEvent);
        }

        public int SubscriptionCount<TEvent>()
        {
            return _handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers)
                ? handlers.Count
                : 0;
        }

        public void Clear() => _handlers.Clear();

        private void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers))
                return;
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(typeof(TEvent));
        }

        private sealed class Subscription<TEvent> : IDisposable
        {
            private WorldEventBus _owner;
            private Action<TEvent> _handler;

            public Subscription(WorldEventBus owner, Action<TEvent> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_owner == null)
                    return;
                _owner.Unsubscribe(_handler);
                _owner = null;
                _handler = null;
            }
        }
    }

    public readonly struct ChunkDataStatusChanged
    {
        public ChunkDataStatusChanged(WorldAddress address, ChunkDataStatus previous, ChunkDataStatus current)
        {
            Address = address;
            Previous = previous;
            Current = current;
        }

        public WorldAddress Address { get; }
        public ChunkDataStatus Previous { get; }
        public ChunkDataStatus Current { get; }
    }

    public readonly struct ChunkSimulationStatusChanged
    {
        public ChunkSimulationStatusChanged(WorldAddress address, ChunkSimulationStatus previous,
            ChunkSimulationStatus current)
        {
            Address = address;
            Previous = previous;
            Current = current;
        }

        public WorldAddress Address { get; }
        public ChunkSimulationStatus Previous { get; }
        public ChunkSimulationStatus Current { get; }
    }

    public readonly struct ChunkPresentationStatusChanged
    {
        public ChunkPresentationStatusChanged(WorldAddress address, ChunkPresentationStatus previous,
            ChunkPresentationStatus current)
        {
            Address = address;
            Previous = previous;
            Current = current;
        }

        public WorldAddress Address { get; }
        public ChunkPresentationStatus Previous { get; }
        public ChunkPresentationStatus Current { get; }
    }

    public readonly struct ChunkCommitted
    {
        public ChunkCommitted(WorldAddress address, long requestVersion, ulong stableHash)
        {
            Address = address;
            RequestVersion = requestVersion;
            StableHash = stableHash;
        }

        public WorldAddress Address { get; }
        public long RequestVersion { get; }
        public ulong StableHash { get; }
    }

    public readonly struct ChunkEvicted
    {
        public ChunkEvicted(WorldAddress address) => Address = address;
        public WorldAddress Address { get; }
    }

}
