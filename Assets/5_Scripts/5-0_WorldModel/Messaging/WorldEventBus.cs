using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 世界模块里的“小广播站”。
    /// 其他代码可以先登记自己关心哪种消息；消息发生时，这里会立刻逐个通知它们。
    /// </summary>
    public sealed class WorldEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers =
            new Dictionary<Type, List<Delegate>>();

        /// <summary>
        /// 登记一个消息接收方法，并返回一张“订阅凭证”。
        /// 同一个方法不会重复登记；不想再接收时，释放这张凭证即可。
        /// </summary>
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

        /// <summary>把消息立刻告诉所有关心这种消息的接收者。</summary>
        public void Publish<TEvent>(TEvent worldEvent)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers) || handlers.Count == 0)
                return;

            // 先复制接收者名单。这样接收者在处理消息时增删订阅，也不会弄乱本轮通知。
            Delegate[] snapshot = handlers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                ((Action<TEvent>)snapshot[i]).Invoke(worldEvent);
        }

        /// <summary>看看现在有多少接收者在等某种消息，主要用于检查和测试。</summary>
        public int SubscriptionCount<TEvent>()
        {
            return _handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers)
                ? handlers.Count
                : 0;
        }

        /// <summary>清空所有订阅；以前拿到的订阅凭证以后再释放也不会出错。</summary>
        public void Clear() => _handlers.Clear();

        private void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out List<Delegate> handlers))
                return;
            handlers.Remove(handler);
            if (handlers.Count == 0)
                _handlers.Remove(typeof(TEvent));
        }

        /// <summary>一张订阅凭证，用完后负责把接收方法从广播站移除。</summary>
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

    /// <summary>通知大家：某个区块的数据状态变了。</summary>
    public readonly struct ChunkDataStatusChanged
    {
        public ChunkDataStatusChanged(WorldAddress address, ChunkDataStatus previous, ChunkDataStatus current)
        {
            Address = address;
            Previous = previous;
            Current = current;
        }

        /// <summary>哪个区块发生了变化。</summary>
        public WorldAddress Address { get; }
        /// <summary>变化前是什么状态。</summary>
        public ChunkDataStatus Previous { get; }
        /// <summary>变化后是什么状态。</summary>
        public ChunkDataStatus Current { get; }
    }

    /// <summary>通知大家：某个区块的游戏逻辑开始运行或进入休眠了。</summary>
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

    /// <summary>通知大家：某个区块的画面连接状态变了。</summary>
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

    /// <summary>通知大家：一个生成结果检查无误，已经正式装进区块了。</summary>
    public readonly struct ChunkCommitted
    {
        public ChunkCommitted(WorldAddress address, long requestVersion, ulong stableHash)
        {
            Address = address;
            RequestVersion = requestVersion;
            StableHash = stableHash;
        }

        /// <summary>哪个区块刚刚生成完成。</summary>
        public WorldAddress Address { get; }
        /// <summary>这次被接受的是第几个生成任务。</summary>
        public long RequestVersion { get; }
        /// <summary>新地形内容的“指纹”，方便检查两份地形是否一致。</summary>
        public ulong StableHash { get; }
    }

    /// <summary>通知大家：某个区块已经从世界里删除了。</summary>
    public readonly struct ChunkEvicted
    {
        public ChunkEvicted(WorldAddress address) => Address = address;
        public WorldAddress Address { get; }
    }

}
