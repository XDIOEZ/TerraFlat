using System;
using FlatWorld.WorldModel;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>按格拒绝过期数据；完整快照分批到达时不覆盖更新的实时差量。</summary>
    public sealed class NetworkTerrainChunkCursor
    {
        #region 区块复制代际
        public ChunkTerrainData Terrain { get; }
        public uint RequestToken { get; private set; }
        public bool HasFullState { get; private set; }
        public float NextRequestTime;
        private readonly long[] revisions;
        private readonly bool[] received;
        private int receivedCount;

        public NetworkTerrainChunkCursor(ChunkTerrainData terrain)
        {
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            revisions = new long[checked(terrain.Width * terrain.Height)];
            received = new bool[revisions.Length];
        }

        public void BeginSnapshot(uint token)
        {
            if (token == 0) throw new ArgumentOutOfRangeException(nameof(token));
            RequestToken = token;
            HasFullState = false;
            receivedCount = 0;
            Array.Clear(received, 0, received.Length);
        }

        public void RequireSnapshot()
        {
            HasFullState = false;
            RequestToken = 0;
            NextRequestTime = 0f;
        }

        public bool Apply(long revision, uint token, int totalCells, TerrainBuildingCellState[] cells)
        {
            if (Terrain.IsDisposed || revision <= 0 || totalCells != revisions.Length ||
                cells == null || cells.Length == 0 || cells.Length > 64 ||
                token != 0 && token != RequestToken)
                return false;
            // 整批先校验，坏坐标或数值不能留下半批已应用状态。
            foreach (TerrainBuildingCellState cell in cells)
                if (!cell.IsValid(revisions.Length)) return false;
            foreach (TerrainBuildingCellState cell in cells)
            {
                if (revision > revisions[cell.Index])
                {
                    cell.Apply(Terrain);
                    revisions[cell.Index] = revision;
                }
                if (token != 0 && !received[cell.Index])
                {
                    received[cell.Index] = true;
                    receivedCount++;
                }
            }
            if (token != 0) HasFullState = receivedCount == received.Length;
            return true;
        }
        #endregion
    }
}
