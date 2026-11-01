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
        public bool HasFullState => hasTerrainFullState && hasFireFullState && hasLiquidFullState;
        public float NextRequestTime;
        private readonly long[] revisions;
        private readonly bool[] received;
        private readonly long[] fireRevisions;
        private readonly bool[] fireRevisionKnown, fireReceived;
        private readonly long[] liquidRevisions;
        private readonly bool[] liquidReceived;
        private int receivedCount, fireReceivedCount, liquidReceivedCount;
        private bool hasTerrainFullState, hasFireFullState, hasLiquidFullState;

        public NetworkTerrainChunkCursor(ChunkTerrainData terrain)
        {
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            revisions = new long[checked(terrain.Width * terrain.Height)];
            received = new bool[revisions.Length];
            fireRevisions = new long[revisions.Length];
            fireRevisionKnown = new bool[revisions.Length];
            fireReceived = new bool[revisions.Length];
            liquidRevisions = new long[revisions.Length];
            liquidReceived = new bool[revisions.Length];
        }

        public void BeginSnapshot(uint token)
        {
            if (token == 0) throw new ArgumentOutOfRangeException(nameof(token));
            RequestToken = token;
            hasTerrainFullState = hasFireFullState = hasLiquidFullState = false;
            receivedCount = fireReceivedCount = liquidReceivedCount = 0;
            Array.Clear(received, 0, received.Length);
            Array.Clear(fireReceived, 0, fireReceived.Length);
            Array.Clear(liquidReceived, 0, liquidReceived.Length);
        }

        // 基线已随持久区块恢复，只接收捕获之后的格子增量。
        public void SetPersistenceBaseline(long revision)
        {
            if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
            for (int i = 0; i < revisions.Length; i++) revisions[i] = liquidRevisions[i] = revision;
            hasTerrainFullState = hasLiquidFullState = true;
            hasFireFullState = false;
            Array.Clear(fireRevisionKnown, 0, fireRevisionKnown.Length);
            Array.Clear(fireReceived, 0, fireReceived.Length);
            fireReceivedCount = 0;
            RequestToken = 0;
        }

        public void RequireSnapshot()
        {
            hasTerrainFullState = hasFireFullState = hasLiquidFullState = false;
            RequestToken = 0;
            NextRequestTime = 0f;
        }

        public bool Apply(long revision, uint token, int totalCells, TerrainBuildingCellState[] cells,
            long fireRevision = 0, NetworkTerrainFireCellState[] fireCells = null,
            NetworkTerrainLiquidCellState[] liquidCells = null)
        {
            cells ??= Array.Empty<TerrainBuildingCellState>();
            fireCells ??= Array.Empty<NetworkTerrainFireCellState>();
            liquidCells ??= Array.Empty<NetworkTerrainLiquidCellState>();
            if (Terrain.IsDisposed || revision <= 0 || totalCells != revisions.Length ||
                cells.Length == 0 && fireCells.Length == 0 && liquidCells.Length == 0 ||
                cells.Length > 64 || fireCells.Length > 64 || liquidCells.Length > 64 ||
                token != 0 && (cells.Length == 0 || fireCells.Length == 0 || liquidCells.Length == 0) ||
                fireCells.Length > 0 && (fireRevision < 0 || token == 0 && fireRevision == 0) ||
                token != 0 && token != RequestToken)
                return false;
            // 整批先校验，坏坐标或数值不能留下半批已应用状态。
            foreach (TerrainBuildingCellState cell in cells)
                if (!cell.IsValid(revisions.Length)) return false;
            for (int i = 0; i < fireCells.Length; i++)
            {
                if (!fireCells[i].IsValid(revisions.Length)) return false;
                for (int j = 0; j < i; j++)
                    if (fireCells[j].Index == fireCells[i].Index) return false;
            }
            for (int i = 0; i < liquidCells.Length; i++)
            {
                if (!liquidCells[i].IsValid(Terrain)) return false;
                for (int j = 0; j < i; j++)
                    if (liquidCells[j].Index == liquidCells[i].Index) return false;
            }

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

            if (fireCells.Length > 0)
            {
                Span<WorldFireCellState> updates = stackalloc WorldFireCellState[64];
                int updateCount = 0;
                foreach (NetworkTerrainFireCellState cell in fireCells)
                    if (!fireRevisionKnown[cell.Index] || fireRevision > fireRevisions[cell.Index])
                        updates[updateCount++] = new WorldFireCellState { CellIndex = cell.Index, Intensity = cell.Intensity };
                if (updateCount > 0) Terrain.SetFireStatesBatch(updates.Slice(0, updateCount));

                foreach (NetworkTerrainFireCellState cell in fireCells)
                {
                    if (!fireRevisionKnown[cell.Index] || fireRevision > fireRevisions[cell.Index])
                    {
                        fireRevisions[cell.Index] = fireRevision;
                        fireRevisionKnown[cell.Index] = true;
                    }
                    if (token != 0 && !fireReceived[cell.Index])
                    {
                        fireReceived[cell.Index] = true;
                        fireReceivedCount++;
                    }
                }
            }

            if (liquidCells.Length > 0)
            {
                Span<LiquidCellUpdate> updates = stackalloc LiquidCellUpdate[64];
                int updateCount = 0;
                foreach (NetworkTerrainLiquidCellState cell in liquidCells)
                {
                    if (revision <= liquidRevisions[cell.Index]) continue;
                    var update = new LiquidCellUpdate(cell.Index, Terrain.LiquidTypes.GetIndex(cell.LiquidId), cell.Level);
                    int slot = updateCount++;
                    while (slot > 0 && updates[slot - 1].CellIndex > update.CellIndex)
                    { updates[slot] = updates[slot - 1]; slot--; }
                    updates[slot] = update;
                }
                if (updateCount > 0) Terrain.SetLiquidBatch(updates.Slice(0, updateCount));
                foreach (NetworkTerrainLiquidCellState cell in liquidCells)
                {
                    if (revision > liquidRevisions[cell.Index]) liquidRevisions[cell.Index] = revision;
                    if (token != 0 && !liquidReceived[cell.Index])
                    { liquidReceived[cell.Index] = true; liquidReceivedCount++; }
                }
            }

            if (token != 0)
            {
                hasTerrainFullState = receivedCount == received.Length;
                hasFireFullState = fireReceivedCount == fireReceived.Length;
                hasLiquidFullState = liquidReceivedCount == liquidReceived.Length;
            }
            return true;
        }
        #endregion
    }
}
