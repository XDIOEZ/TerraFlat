#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public sealed class GameplayTestBotTransport : Transport
    {
        #region 独立假人连接与内存消息
        private sealed class BotConnection
        {
            public bool Connected;
            public readonly Unbatcher Unbatcher = new();
            public readonly List<byte[]> Received = new();
            public readonly ReadOnlyCollection<byte[]> View;
            public BotConnection() => View = Received.AsReadOnly();
        }

        private readonly Dictionary<int, BotConnection> bots = new();
        private bool running;

        // 每个假人只走正式服务端传输回调，不创建 NetworkClient 或真实网络端口。
        public void ConnectBot(int id)
        {
            if (!running || !NetworkServer.active || OnServerConnectedWithAddress == null)
                throw new InvalidOperationException("假人连接需要已启动的 Mirror 内存服务器。");
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "连接 0 保留给 Mirror 本地主机。");
            if (bots.TryGetValue(id, out BotConnection previous))
            {
                if (previous.Connected) throw new InvalidOperationException("假人连接身份已经使用：" + id);
                previous.Unbatcher.Clear();
            }
            bots[id] = new BotConnection { Connected = true };
            OnServerConnectedWithAddress.Invoke(id, "lua-bot:" + id);
        }

        // 返回值只表示消息进入正式传输入口，业务是否接受由真实回复和权威状态判断。
        public bool Receive<T>(int id, T message, int channel = Channels.Reliable) where T : struct, NetworkMessage
        {
            if (!running || !NetworkServer.active || !bots.TryGetValue(id, out BotConnection bot) || !bot.Connected ||
                !NetworkServer.connections.ContainsKey(id)) return false;
            if (OnServerDataReceived == null) throw new InvalidOperationException("Mirror 服务端消息入口没有绑定。");
            using NetworkWriterPooled content = NetworkWriterPool.Get();
            NetworkMessages.Pack(message, content);
            if (content.Position + Batcher.MaxMessageOverhead(content.Position) > GetMaxPacketSize(channel))
                throw new InvalidOperationException("假人消息超过内存传输允许的包大小。");
            var batcher = new Batcher(GetBatchThreshold(channel));
            try
            {
                batcher.AddMessage(content.ToArraySegment(), NetworkTime.localTime);
                using NetworkWriterPooled batch = NetworkWriterPool.Get();
                while (batcher.GetBatch(batch))
                {
                    OnServerDataReceived.Invoke(id, batch.ToArraySegment(), channel);
                    batch.Reset();
                }
            }
            finally { batcher.Clear(); }
            return true;
        }

        public bool IsBotConnected(int id) => running && bots.TryGetValue(id, out BotConnection bot) && bot.Connected;

        public IReadOnlyList<byte[]> Messages(int id) => bots.TryGetValue(id, out BotConnection bot)
            ? bot.View : Array.Empty<byte[]>();

        public int Count<T>(int id) where T : struct, NetworkMessage
        {
            int count = 0;
            foreach (byte[] bytes in Messages(id))
            {
                using NetworkReaderPooled reader = NetworkReaderPool.Get(bytes);
                if (!NetworkMessages.UnpackId(reader, out ushort messageId))
                    throw new InvalidOperationException("假人收包缺少 Mirror 消息头。");
                if (messageId == NetworkMessages.GetId<T>()) count++;
            }
            return count;
        }

        public IReadOnlyList<T> Read<T>(int id) where T : struct, NetworkMessage
        {
            var result = new List<T>();
            foreach (byte[] bytes in Messages(id))
            {
                using NetworkReaderPooled reader = NetworkReaderPool.Get(bytes);
                if (!NetworkMessages.UnpackId(reader, out ushort messageId))
                    throw new InvalidOperationException("假人收包缺少 Mirror 消息头。");
                if (messageId != NetworkMessages.GetId<T>()) continue;
                result.Add(reader.Read<T>());
                if (reader.Remaining != 0)
                    throw new InvalidOperationException("假人消息反序列化后仍有未消费数据：" + typeof(T).Name);
            }
            return result.AsReadOnly();
        }
        #endregion

        #region Mirror 传输实现与安全收尾
        public override bool Available() => true;
        public override bool ClientConnected() => false;
        public override void ClientConnect(string address) => throw new NotSupportedException("Lua 假人传输不启动 NetworkClient。");
        public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
            => throw new NotSupportedException("假人发送应使用带连接身份的 Receive。");
        public override void ClientDisconnect() { }
        public override Uri ServerUri() => new("memory://flatworld-lua");
        public override bool ServerActive() => running;
        public override int GetMaxPacketSize(int channelId = Channels.Reliable) => ushort.MaxValue;
        public override int GetBatchThreshold(int channelId = Channels.Reliable) => 1400;

        public override void ServerStart()
        {
            if (running) return;
            foreach (BotConnection bot in bots.Values) bot.Unbatcher.Clear();
            bots.Clear();
            running = true;
        }

        // 正式 SendToTransport 的批包按连接解开并复制，池化缓冲归还后采样仍保持有效。
        public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            if (!running || !bots.TryGetValue(connectionId, out BotConnection bot) || !bot.Connected) return;
            try
            {
                if (segment.Count > GetMaxPacketSize(channelId) || !bot.Unbatcher.AddBatch(segment))
                    throw new InvalidOperationException("服务端发送了无效或过大的 Mirror 批包。");
                while (bot.Unbatcher.GetNextMessage(out ArraySegment<byte> message, out _))
                {
                    if (message.Count < NetworkMessages.IdSize)
                        throw new InvalidOperationException("服务端发送的 Mirror 消息缺少类型头。");
                    byte[] copy = new byte[message.Count];
                    Buffer.BlockCopy(message.Array, message.Offset, copy, 0, message.Count);
                    bot.Received.Add(copy);
                }
            }
            catch { bot.Unbatcher.Clear(); throw; }
            OnServerDataSent?.Invoke(connectionId, segment, channelId);
        }

        public override void ServerDisconnect(int connectionId)
        {
            if (!bots.TryGetValue(connectionId, out BotConnection bot) || !bot.Connected) return;
            // 先撤连接再回调，正式断线清理再次请求 Disconnect 也不会重入。
            bot.Connected = false;
            bot.Unbatcher.Clear();
            OnServerDisconnected?.Invoke(connectionId);
        }

        public override string ServerGetClientAddress(int connectionId) => "lua-bot:" + connectionId;

        public override void ServerStop()
        {
            running = false;
            List<Exception> failures = null;
            foreach (int id in new List<int>(bots.Keys))
            {
                try { ServerDisconnect(id); }
                catch (Exception failure) { (failures ??= new()).Add(failure); }
            }
            foreach (BotConnection bot in bots.Values) bot.Unbatcher.Clear();
            if (failures != null) throw new AggregateException("假人正式断线清理失败。", failures);
        }

        public override void Shutdown() => ServerStop();
        private void OnDestroy() => ServerStop();
        #endregion
    }
}
#endif
