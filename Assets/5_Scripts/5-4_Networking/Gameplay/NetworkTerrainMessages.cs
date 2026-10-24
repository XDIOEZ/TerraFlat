using FlatWorld.WorldModel;
using Mirror;

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
    #endregion
}
