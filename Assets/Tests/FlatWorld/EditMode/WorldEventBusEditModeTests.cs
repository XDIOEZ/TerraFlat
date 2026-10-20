using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using NUnit.Framework;

namespace FlatWorld.Tests.EditMode
{
    public sealed class WorldEventBusEditModeTests
    {
        #region 广播快照与分配回归

        [Test]
        public void Publish_StableSubscribers_AllocatesNoMemoryAfterWarmup()
        {
            var bus = new WorldEventBus();
            int delivered = 0;
            using var subscription = bus.Subscribe<ChunkDataStatusChanged>(_ => delivered++);
            var address = new WorldAddress("surface", new Int2(0, 0));
            var message = new ChunkDataStatusChanged(address, ChunkDataStatus.Requested, ChunkDataStatus.Ready);

            for (int i = 0; i < 32; i++) bus.Publish(message);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) bus.Publish(message);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(delivered, Is.EqualTo(288));
            Assert.That(allocated, Is.Zero, "稳定订阅时，每条值类型世界消息不应产生 GC 分配");
        }

        [Test]
        public void Publish_UnsubscribingAndReentering_KeepsOuterSnapshot()
        {
            var bus = new WorldEventBus();
            var delivered = new List<string>();
            IDisposable secondSubscription = null;
            Action<int> third = value => delivered.Add("third:" + value);
            bus.Subscribe<int>(value =>
            {
                delivered.Add("first:" + value);
                if (value != 1) return;
                secondSubscription.Dispose();
                bus.Subscribe(third);
                bus.Publish(2);
            });
            secondSubscription = bus.Subscribe<int>(value => delivered.Add("second:" + value));

            bus.Publish(1);

            Assert.That(delivered, Is.EqualTo(new[]
                { "first:1", "first:2", "third:2", "second:1" }));
            Assert.That(bus.SubscriptionCount<int>(), Is.EqualTo(2));
        }

        [Test]
        public void AddressSubscriptions_AreIsolatedAndKeepDuplicateSubscriptionBehavior()
        {
            var bus = new WorldEventBus();
            var firstAddress = new WorldAddress("surface", new Int2(0, 0));
            var secondAddress = new WorldAddress("surface", new Int2(16, 0));
            int committed = 0;
            int evicted = 0;
            Action<ChunkCommitted> callback = _ => committed++;
            using var first = bus.SubscribeChunkCommitted(firstAddress, callback);
            using var second = bus.SubscribeChunkCommitted(firstAddress, callback);
            using var unrelated = bus.SubscribeChunkEvicted(firstAddress, _ => evicted++);

            bus.Publish(new ChunkCommitted(secondAddress, 1));
            bus.Publish(new ChunkCommitted(firstAddress, 1));
            Assert.That(committed, Is.EqualTo(2));

            first.Dispose();
            bus.Publish(new ChunkCommitted(firstAddress, 2));
            bus.Publish(new ChunkEvicted(firstAddress));
            Assert.That(committed, Is.EqualTo(3));
            Assert.That(evicted, Is.EqualTo(1));

            bus.Clear();
            second.Dispose();
            bus.Publish(new ChunkCommitted(firstAddress, 3));
            Assert.That(committed, Is.EqualTo(3));
        }

        #endregion
    }
}
