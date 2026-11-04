using System;
using System.Collections.Generic;
using Mirror;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 大地层列的可靠分片
        private readonly TerrainMessagePartAccumulator incomingTerrainParts = new();
        private static uint nextTerrainPartTransfer;

        private static void SendTerrainMessageOrParts(NetworkConnectionToClient connection, NetworkTerrainChunkMessage message)
        {
            int limit = Transport.active?.GetMaxPacketSize(Channels.Reliable) ?? 32 * 1024;
            if (TerrainMessageFitsTransport(message, limit)) { connection.Send(message, Channels.Reliable); return; }
            foreach (NetworkTerrainChunkPart part in CreateTerrainMessageParts(message, limit))
                connection.Send(part, Channels.Reliable);
        }

        internal static IEnumerable<NetworkTerrainChunkPart> CreateTerrainMessageParts(NetworkTerrainChunkMessage message, int limit)
        {
            using NetworkWriterPooled writer = NetworkWriterPool.Get();
            writer.Write(message);
            byte[] bytes = writer.ToArray();
            if (bytes.Length > NetworkGameplayProtocol.MaxSnapshotBytes)
                throw new InvalidOperationException("地层列超过区块快照容量。");
            int partBytes = Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, limit - 256);
            if (partBytes <= 0) throw new InvalidOperationException("地形分片传输容量不足。");
            uint transfer = ++nextTerrainPartTransfer;
            if (transfer == 0) transfer = ++nextTerrainPartTransfer;
            int count = (bytes.Length + partBytes - 1) / partBytes;
            for (int i = 0; i < count; i++)
            {
                var data = new byte[Math.Min(partBytes, bytes.Length - i * partBytes)];
                Buffer.BlockCopy(bytes, i * partBytes, data, 0, data.Length);
                yield return new NetworkTerrainChunkPart { TransferId = transfer, RealmRevision = message.RealmRevision,
                    Index = i, Count = count, TotalBytes = bytes.Length, PartBytes = partBytes, Data = data };
            }
        }

        private void OnClientTerrainChunkPart(NetworkTerrainChunkPart part)
        {
            if (NetworkServer.active || !PrepareTerrainClient(out _, out uint realm) || part.RealmRevision != realm) return;
            if (incomingTerrainParts.TryAccept(part, out NetworkTerrainChunkMessage message)) OnClientTerrainChunkMessage(message);
        }

        // 重组只产出完整消息，地形状态和修订仍由既有游标负责。
        internal sealed class TerrainMessagePartAccumulator
        {
            private sealed class Incoming
            {
                public byte[][] Parts;
                public int Received, TotalBytes, PartBytes;
            }
            private readonly Dictionary<uint, Incoming> pending = new();
            public void Clear() => pending.Clear();
            public bool TryAccept(NetworkTerrainChunkPart part, out NetworkTerrainChunkMessage message)
            {
                message = default;
                if (part.TotalBytes <= 0 || part.TotalBytes > NetworkGameplayProtocol.MaxSnapshotBytes || part.PartBytes <= 0 ||
                    part.PartBytes > NetworkGameplayProtocol.SnapshotChunkBytes || part.Count != (part.TotalBytes + part.PartBytes - 1) / part.PartBytes ||
                    part.Index < 0 || part.Index >= part.Count || part.Data == null ||
                    part.Data.Length != Math.Min(part.PartBytes, part.TotalBytes - part.Index * part.PartBytes)) return false;
                if (!pending.TryGetValue(part.TransferId, out Incoming entry))
                    pending.Add(part.TransferId, entry = new Incoming
                    { Parts = new byte[part.Count][], TotalBytes = part.TotalBytes, PartBytes = part.PartBytes });
                if (entry.TotalBytes != part.TotalBytes || entry.PartBytes != part.PartBytes || entry.Parts[part.Index] != null) return false;
                entry.Parts[part.Index] = part.Data;
                if (++entry.Received != entry.Parts.Length) return false;
                pending.Remove(part.TransferId);
                var bytes = new byte[part.TotalBytes];
                for (int i = 0; i < entry.Parts.Length; i++)
                    Buffer.BlockCopy(entry.Parts[i], 0, bytes, i * part.PartBytes, entry.Parts[i].Length);
                using NetworkReaderPooled reader = NetworkReaderPool.Get(bytes);
                message = reader.Read<NetworkTerrainChunkMessage>();
                return true;
            }
        }
        #endregion
    }
}
