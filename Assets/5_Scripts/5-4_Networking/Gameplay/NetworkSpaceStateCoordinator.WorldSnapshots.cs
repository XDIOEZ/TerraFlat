using System;
using System.IO;
using System.IO.Compression;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public struct NetworkDimensionWorldSnapshotBegin : NetworkMessage
    {
        public ulong Sequence;
        public string WorldKey;
        public uint RealmRevision;
        public int CompressedBytes, ExpandedBytes, ChunkCount;
        public uint Hash;
    }
    public struct NetworkDimensionWorldSnapshotChunk : NetworkMessage
    {
        public ulong Sequence;
        public int Index;
        public byte[] Data;
    }
    public sealed partial class NetworkSpaceStateCoordinator
    {
        #region 目标世界的有限分片资料
        private sealed class WorldIncoming
        {
            public NetworkDimensionWorldSnapshotBegin Header;
            public byte[][] Parts;
            public int Received;
            public float Deadline;
        }
        private WorldIncoming worldIncoming;
        private ulong worldSnapshotSequence, lastWorldSnapshotSequence;
        private string lastMetadataWorld;
        private uint lastMetadataRealm;
        private bool hasMetadataIdentity;
        private bool worldSnapshotInvalid;
        private void StartWorldSnapshotClientBridge()
        {
            NetworkClient.RegisterHandler<NetworkDimensionWorldSnapshotBegin>(OnWorldSnapshotBegin, true);
            NetworkClient.RegisterHandler<NetworkDimensionWorldSnapshotChunk>(OnWorldSnapshotChunk, true);
        }
        private void StopWorldSnapshotClientBridge()
        {
            NetworkClient.UnregisterHandler<NetworkDimensionWorldSnapshotBegin>();
            NetworkClient.UnregisterHandler<NetworkDimensionWorldSnapshotChunk>();
            worldIncoming = null; worldSnapshotInvalid = false; lastWorldSnapshotSequence = 0;
            lastMetadataWorld = null; lastMetadataRealm = 0; hasMetadataIdentity = false;
        }
        // 客户端保留的目标区块先合入最新正式差量，随后才处理乘员归属快照。
        private void SendWorldSnapshot(NetworkConnectionToClient connection, string worldKey, uint realmRevision)
        {
            byte[] raw = SaveDataMgr.Instance.CaptureNetworkWorldSnapshot(worldKey);
            if (raw.Length > MaxExpandedBytes) throw new InvalidDataException("目标世界资料超过解压限额。");
            byte[] compressed;
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) gzip.Write(raw, 0, raw.Length);
                compressed = output.ToArray();
            }
            if (compressed.Length > MaxCompressedBytes) throw new InvalidDataException("目标世界资料超过压缩限额。");
            if (++worldSnapshotSequence == 0) ++worldSnapshotSequence;
            int count = (compressed.Length + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes;
            connection.Send(new NetworkDimensionWorldSnapshotBegin { Sequence = worldSnapshotSequence, WorldKey = worldKey,
                RealmRevision = realmRevision,
                CompressedBytes = compressed.Length, ExpandedBytes = raw.Length, ChunkCount = count,
                Hash = NetworkGameplayProtocol.CalculatePayloadHash(compressed) });
            for (int i = 0; i < count; i++)
            {
                int offset = i * NetworkGameplayProtocol.SnapshotChunkBytes;
                var part = new byte[Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, compressed.Length - offset)];
                Buffer.BlockCopy(compressed, offset, part, 0, part.Length);
                connection.Send(new NetworkDimensionWorldSnapshotChunk { Sequence = worldSnapshotSequence, Index = i, Data = part });
            }
        }
        private void OnWorldSnapshotBegin(NetworkDimensionWorldSnapshotBegin header)
        {
            if (NetworkServer.active || header.Sequence <= lastWorldSnapshotSequence || worldIncoming != null && header.Sequence <= worldIncoming.Header.Sequence) return;
            if (string.IsNullOrWhiteSpace(header.WorldKey) || header.WorldKey.Length > 256 ||
                header.CompressedBytes <= 0 || header.CompressedBytes > MaxCompressedBytes ||
                header.ExpandedBytes <= 0 || header.ExpandedBytes > MaxExpandedBytes || header.ChunkCount !=
                (header.CompressedBytes + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes)
            { RejectWorldSnapshot("目标世界资料头无效。"); return; }
            worldIncoming = new WorldIncoming { Header = header, Parts = new byte[header.ChunkCount][], Deadline = Time.unscaledTime + 30f };
        }
        private void OnWorldSnapshotChunk(NetworkDimensionWorldSnapshotChunk message)
        {
            WorldIncoming incomingWorld = worldIncoming;
            if (incomingWorld == null || message.Sequence != incomingWorld.Header.Sequence || (uint)message.Index >= (uint)incomingWorld.Parts.Length) return;
            int expected = Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes,
                incomingWorld.Header.CompressedBytes - message.Index * NetworkGameplayProtocol.SnapshotChunkBytes);
            if (message.Data?.Length != expected)
            { RejectWorldSnapshot("目标世界分片长度无效。"); return; }
            // 已接收的分片以首份为准，整包哈希负责完整性；重复分片不应中断下载。
            if (incomingWorld.Parts[message.Index] != null) return;
            incomingWorld.Parts[message.Index] = message.Data;
            incomingWorld.Deadline = Time.unscaledTime + 30f;
            if (++incomingWorld.Received < incomingWorld.Parts.Length) return;
            worldIncoming = null;
            try
            {
                var compressed = new byte[incomingWorld.Header.CompressedBytes];
                int offset = 0;
                foreach (byte[] part in incomingWorld.Parts) { Buffer.BlockCopy(part, 0, compressed, offset, part.Length); offset += part.Length; }
                if (NetworkGameplayProtocol.CalculatePayloadHash(compressed) != incomingWorld.Header.Hash) throw new InvalidDataException("目标世界资料校验失败。");
                var raw = new byte[incomingWorld.Header.ExpandedBytes];
                using (var source = new MemoryStream(compressed, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    offset = 0;
                    while (offset < raw.Length)
                    {
                        int count = gzip.Read(raw, offset, raw.Length - offset);
                        if (count == 0) break;
                        offset += count;
                    }
                    if (offset != raw.Length || gzip.ReadByte() != -1) throw new InvalidDataException("目标世界解压长度不匹配。");
                }
                bool changingRealm = hasMetadataIdentity
                    ? lastMetadataWorld != incomingWorld.Header.WorldKey || lastMetadataRealm != incomingWorld.Header.RealmRevision
                    : GameplayWorldRegistry.LocalPresentationWorldKey != incomingWorld.Header.WorldKey;
                if (changingRealm && GameplayWorldRegistry.TryGet(incomingWorld.Header.WorldKey, out GameplayWorldRuntime target))
                    using (GameplayWorldRegistry.UseWorld(incomingWorld.Header.WorldKey))
                        if (!target.Chunks.InvalidateRetainedClientChunksForNetworkTransfer())
                            throw new InvalidOperationException("目标世界仍有未释放的区块租约。");
                string worldKey = SaveDataMgr.Instance.ApplyNetworkWorldSnapshot(raw, incomingWorld.Header.WorldKey);
                if (worldKey != incomingWorld.Header.WorldKey) throw new InvalidDataException("目标世界载荷身份不匹配。");
                lastWorldSnapshotSequence = incomingWorld.Header.Sequence; worldSnapshotInvalid = false;
                lastMetadataWorld = worldKey; lastMetadataRealm = incomingWorld.Header.RealmRevision; hasMetadataIdentity = true;
                Player actor = ItemMgr.Instance?.User_Player;
                if (actor?.Data?.CurrentSceneName == worldKey && DimensionManager.ExistingInstance?.IsTransitioning != true)
                    StartCoroutine(DimensionManager.Instance.ApplyNetworkPresentation(actor, WorldAddress.FromWorldKey(worldKey), actor.Data.transform.position));
            }
            catch (Exception error) { RejectWorldSnapshot(error.Message); }
        }
        private void RejectWorldSnapshot(string reason)
        {
            worldIncoming = null; worldSnapshotInvalid = true;
            needsSnapshot = true; nextClientRequest = 0f;
            Debug.LogWarning("[联机维度] 目标世界资料无效，正在重新请求：" + reason);
        }
        private void UpdateWorldSnapshotTimeout()
        {
            if (worldIncoming != null && Time.unscaledTime > worldIncoming.Deadline) RejectWorldSnapshot("目标世界资料接收超时。");
        }
        #endregion
    }
}
