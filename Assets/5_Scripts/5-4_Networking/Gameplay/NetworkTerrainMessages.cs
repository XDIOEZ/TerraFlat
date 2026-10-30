using FlatWorld.WorldModel;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    #region 格子建筑复制协议
    public struct NetworkTerrainChunkRequest : NetworkMessage, INetworkWorldRequest
    {
        public uint RealmRevision;
        public uint WorldRevision => RealmRevision;
        public uint RequestToken;
        public string WorldKey, DimensionId;
        public int ChunkX, ChunkY;
    }

    public struct NetworkTerrainChunkMessage : NetworkMessage
    {
        public string WorldKey, DimensionId;
        public uint RealmRevision, RequestToken;
        public int ChunkX, ChunkY, TotalCells;
        public long Revision;
        public TerrainBuildingCellState[] Cells;
    }

    public struct NetworkPersistedChunkRequest : NetworkMessage
    {
        public string WorldKey, DimensionId;
        public int ChunkX, ChunkY, BootstrapTransfer;
        public long WorldEpoch;
        public uint RealmRevision, RequestToken;
    }

    public struct NetworkPersistedChunkPart : NetworkMessage
    {
        public string WorldKey, DimensionId;
        public int ChunkX, ChunkY, BootstrapTransfer, Index, Count, TotalBytes;
        public long WorldEpoch, Revision;
        public uint RealmRevision, RequestToken, Hash;
        public bool RequiresTerrainRefresh;
        public byte[] Data;
    }

    public struct NetworkChunkEntityBaselineRequest : NetworkMessage
    {
        public string WorldKey, DimensionId;
        public int ChunkX, ChunkY;
        public long WorldEpoch;
        public uint RealmRevision, RequestToken;
    }

    public struct NetworkChunkEntityBaselineComplete : NetworkMessage
    {
        public string WorldKey, DimensionId;
        public int ChunkX, ChunkY;
        public long WorldEpoch;
        public uint RealmRevision, RequestToken;
    }

    public struct NetworkLandingPreviewRequest : NetworkMessage
    {
        public string BodyId;
        public Vector2 Origin, Selected;
        public long Epoch;
        public uint RealmRevision, RequestToken;
        public bool Cancel;
    }

    public struct NetworkLandingPreviewPart : NetworkMessage
    {
        public long Epoch;
        public uint RealmRevision, RequestToken, TransferId, Hash;
        public int Index, Count, PartBytes, TotalBytes;
        public byte[] Data;
    }
    #endregion
}
