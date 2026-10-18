using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class FarmlandSystem
{
    #region 同步土壤批次

    private static readonly Stack<SoilSamplingBatch> soilSamplingBatches = new();

    /// <summary>只共享本轮寻址和快照外壳，灌溉、土壤数值与提交仍按调用顺序执行。</summary>
    internal sealed class SoilSamplingBatch : IDisposable
    {
        private ChunkMgr manager;
        private ChunkMgr.RuntimeTerrainPresenceQuery query;
        private bool hasQuery, leased;
        private TileData_Farmland template, snapshot;

        internal void Begin()
        {
            leased = true;
            manager = ChunkMgr.ExistingInstance;
            query = default;
            hasQuery = manager != null && manager.TryCreateTerrainPresenceQuery(out query);
            template = null;
        }

        internal bool TrySample(Vector2 position, out RuntimeTerrainTileSample sample)
        {
            sample = default;
            return leased && hasQuery && manager != null &&
                query.IsForWorld(manager.WorldRuntime) && query.TrySample(position, out sample);
        }

        /// <summary>无表层水的邻格直接跳过，保留真实水格的完整读取及抽取事务。</summary>
        internal bool TrySampleSurfaceLiquid(Vector2 position, out RuntimeTerrainTileSample sample)
        {
            sample = default;
            return leased && hasQuery && manager != null &&
                query.IsForWorld(manager.WorldRuntime) && query.TrySampleSurfaceLiquid(position, out sample);
        }

        // 返回容器只借给当前调用者，下一次读取会覆盖，不能存入实体或跨回调保留。
        internal bool TryRead(Vector2Int cell, out TileData_Farmland soil,
            out RuntimeTerrainTileSample sample, bool allowNaturalSoil)
        {
            soil = null;
            if (!TrySample(new Vector2(cell.x + 0.5f, cell.y + 0.5f), out sample) ||
                !CanUseSoilCell(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, sample.Cell, sample.WorldCell, true) ||
                (!allowNaturalSoil && !IsFarmland(sample.Cell) && !HasSoilState(sample)))
                return false;
            template ??= (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
            snapshot ??= new TileData_Farmland();
            snapshot.CopyFrom(template);
            ReadSoilSnapshot(sample, snapshot);
            soil = snapshot;
            if (TryAbsorbNearbyLiquid(sample, soil, this)) Commit(soil, allowNaturalSoil);
            return true;
        }

        internal void Commit(TileData_Farmland soil, bool allowNaturalSoil)
        {
            if (TrySample(new Vector2(soil.position.x + 0.5f, soil.position.y + 0.5f), out var sample))
                CommitSoil(sample, soil, allowNaturalSoil, normalizedWorldCell: true);
        }

        public void Dispose()
        {
            if (!leased) return;
            leased = false;
            manager = null;
            template = null;
            query = default;
            soilSamplingBatches.Push(this);
        }
    }

    internal static SoilSamplingBatch CreateSoilSamplingBatch()
    {
        SoilSamplingBatch batch = soilSamplingBatches.Count > 0 ? soilSamplingBatches.Pop() : new SoilSamplingBatch();
        batch.Begin();
        return batch;
    }

    #endregion
}
