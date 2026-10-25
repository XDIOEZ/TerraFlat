using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
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
            if (!TrySample(new Vector2(cell.x + 0.5f, cell.y + 0.5f), out sample))
            {
                soil = null;
                return false;
            }
            return TryReadSampled(sample, out soil, allowNaturalSoil);
        }

        /// <summary>光照已经取得的权威地块可直接读取水肥，避免同一植物重复寻址。</summary>
        internal bool TryReadSampled(RuntimeTerrainTileSample sample, out TileData_Farmland soil, bool allowNaturalSoil)
        {
            soil = null;
            if (!leased || !hasQuery || manager == null || !query.IsForWorld(manager.WorldRuntime) ||
                !CanUseSoilCell(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y, sample.Cell, sample.WorldCell, true) ||
                !CanReadSoil(sample, allowNaturalSoil))
                return false;
            template ??= (TileData_Farmland)RequireFarmlandDefinition().TileDataTemplate;
            snapshot ??= new TileData_Farmland();
            snapshot.CopyFrom(template);
            ReadSoilSnapshot(sample, snapshot);
            soil = snapshot;
            if (IsFarmland(sample.Cell) && TryAbsorbNearbyLiquid(sample, soil, this)) Commit(soil, allowNaturalSoil);
            return true;
        }

        internal void Commit(TileData_Farmland soil, bool allowNaturalSoil)
        {
            if (TrySample(new Vector2(soil.position.x + 0.5f, soil.position.y + 0.5f), out var sample))
                CommitSampled(sample, soil, allowNaturalSoil);
        }

        // 本轮已取得的地块直接提交，避免每株植物重复寻址同一格。
        internal void CommitSampled(RuntimeTerrainTileSample sample, TileData_Farmland soil, bool allowNaturalSoil)
        {
            if (leased && hasQuery && manager != null && query.IsForWorld(manager.WorldRuntime) && IsFarmland(sample.Cell))
                CommitSoil(sample, soil, allowNaturalSoil, normalizedWorldCell: true);
        }

        /// <summary>
        /// 原生植物本轮水肥结算没有数值变化时，仅已初始化且完整同步的格可跳过写回。
        /// 地理湿润度和肥力只读，动态状态只比较农业自身的权威层。
        /// </summary>
        internal static bool IsAlreadyCommitted(RuntimeTerrainTileSample sample, TileData_Farmland soil)
        {
            ChunkTerrainData terrain = sample.Terrain;
            if (terrain == null || terrain.IsDisposed || soil == null) return false;
            int x = sample.LocalCell.x, y = sample.LocalCell.y;
            float fertility = soil.Fertility;
            return terrain.TryGetEnvironmentValue(SourceLayer, x, y, out float source) && source > 0f &&
                terrain.TryGetEnvironmentValue(WaterLayer, x, y, out float water) && water.Equals(soil.waterValue) &&
                terrain.TryGetEnvironmentValue(WaterSupplyRemainderLayer, x, y, out float supply) &&
                    supply.Equals(soil.waterSupplyRemainder) &&
                terrain.TryGetEnvironmentValue(WaterConsumptionRemainderLayer, x, y, out float consumption) &&
                    consumption.Equals(soil.waterConsumptionRemainder) &&
                terrain.TryGetEnvironmentValue(FertilityLayer, x, y, out float storedFertility) &&
                    storedFertility.Equals(fertility);
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
