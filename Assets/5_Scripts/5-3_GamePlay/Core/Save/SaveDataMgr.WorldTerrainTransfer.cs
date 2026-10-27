using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 已有世界地格移交事务
    // 只迁玩家覆盖和局部世界内容，天然地表与海水留在原世界；失败恢复两端的真实格子。
    internal WorldTerrainTransfer TransferWorldTerrain(string source, string destination,
        Predicate<Vector2> select, Func<Vector2, Vector2> project)
        => new(this, source, destination, select, project);

    internal sealed class WorldTerrainTransfer : IDisposable
    {
        private sealed class CellMove
        {
            internal string Source, Destination;
            internal ChunkRuntime SourceChunk, DestinationChunk;
            internal Vector2Int SourceLocal, DestinationLocal;
            internal TerrainCellTransferState OriginalSource, OriginalDestination, NextSource, NextDestination;
            internal ChunkAgricultureRenderer SourceCrops, DestinationCrops;
            internal bool SourceWritten, DestinationWritten, CropMoved;
        }

        private readonly SaveDataMgr save;
        private readonly List<CellMove> moves = new();
        private bool finished;

        internal WorldTerrainTransfer(SaveDataMgr save, string source, string destination,
            Predicate<Vector2> select, Func<Vector2, Vector2> project)
        {
            this.save = save;
            if (!GameNetwork.HasStateAuthority || source == destination || select == null || project == null ||
                !GameplayWorldRegistry.TryGet(source, out var from) || from.Chunks == null ||
                !GameplayWorldRegistry.TryGet(destination, out var to) || to.Chunks == null)
                throw new InvalidOperationException("地格移交的世界或权威尚未就绪。");
            bool localSource = from.UsesLocalCoordinates;
            var targets = new HashSet<(FlatWorld.WorldModel.WorldAddress, Vector2Int)>();
            using (GameplayWorldRegistry.UseWorld(source))
            {
                from.Chunks.CaptureRuntimeNaturalItemStates();
                foreach (ChunkRuntime chunk in from.Chunks.Chunks.Values)
                {
                    if (chunk?.Terrain == null || chunk.Terrain.IsDisposed) continue;
                    for (int y = 0; y < chunk.Terrain.Height; y++)
                    for (int x = 0; x < chunk.Terrain.Width; x++)
                    {
                        Vector2 center = new(chunk.Address.ChunkOrigin.X + x + .5f, chunk.Address.ChunkOrigin.Y + y + .5f);
                        if (!select(center)) continue;
                        var state = save.CaptureTerrainCellForTransfer(chunk, new Vector2Int(x, y));
                        LiquidCellValue baselineLiquid = chunk.Terrain.GetLiquidBaseline(x, y);
                        int liquid = localSource || baselineLiquid.LiquidTypeIndex != chunk.Terrain.GetLiquidTypeIndex(x, y)
                            ? state.Liquid.LiquidLevel : Math.Max(0, state.Liquid.LiquidLevel - baselineLiquid.LiquidLevel);
                        bool ground = localSource ? state.Cell.GroundTileId != 0 || state.Cell.BackTileId != 0
                            : state.Ground.Covers.Count != 0;
                        bool support = state.Support.TileId != 0 && !state.ExternallyOwned;
                        if (!ground && liquid == 0 && !support &&
                            (!localSource || state.Agriculture == null && state.Contamination == null && state.Snow == null)) continue;
                        Vector2 next = project(center);
                        RuntimeTerrainTileSample sample;
                        using (GameplayWorldRegistry.UseWorld(destination))
                            if (!to.Chunks.TryGetRuntimeTerrainTile(next, out sample))
                                throw new InvalidOperationException("目标地格尚未生成，不能丢弃源世界内容。");
                        if (!targets.Add((sample.Address, sample.LocalCell)))
                            throw new InvalidOperationException("地格移交映射重叠。");
                        if (!to.Chunks.TryGetChunkRuntime(sample.Address, out ChunkRuntime target))
                            throw new InvalidOperationException("目标区块已失效。");
                        TerrainCellTransferState targetState;
                        using (GameplayWorldRegistry.UseWorld(destination))
                            targetState = save.CaptureTerrainCellForTransfer(target, sample.LocalCell);
                        if ((ground || localSource && state.Agriculture != null) && targetState.Agriculture != null)
                            throw new InvalidOperationException("目标地格已有作物或耕地水肥，不能直接覆盖。");
                        ChunkAgricultureRenderer sourceCrops = null, destinationCrops = null;
                        if (state.Agriculture?.Crop != null && (ground || localSource))
                        {
                            sourceCrops = FindCropRenderer(from.Chunks, chunk);
                            sourceCrops?.CaptureState();
                            state = save.CaptureTerrainCellForTransfer(chunk, new Vector2Int(x, y));
                            if (state.Agriculture?.Crop == null) sourceCrops = null;
                            else
                            {
                                destinationCrops = FindCropRenderer(to.Chunks, target);
                                if (sourceCrops == null || destinationCrops == null)
                                    throw new InvalidOperationException("作物移交必须先绑定两端正式农业区块。");
                            }
                        }
                        var move = new CellMove { Source = source, Destination = destination,
                            SourceChunk = chunk, DestinationChunk = target, SourceLocal = new Vector2Int(x, y),
                            DestinationLocal = sample.LocalCell, OriginalSource = state, OriginalDestination = targetState,
                            NextSource = state.Copy(), NextDestination = targetState.Copy(),
                            SourceCrops = sourceCrops, DestinationCrops = destinationCrops };
                        PrepareTerrainCellMove(move, localSource, ground, support, liquid);
                        if (move.NextDestination.Agriculture?.Crop?.transform != null)
                        {
                            move.NextDestination.Agriculture.Crop.transform.position = new Vector3(sample.WorldCell.x + .5f, sample.WorldCell.y + .5f);
                            CropWorldRelocation.RelocateSavedCrop(move.NextDestination.Agriculture.Crop, sample.WorldCell);
                        }
                        moves.Add(move);
                    }
                }
            }
            try
            {
                foreach (CellMove move in moves)
                {
                    move.SourceWritten = true;
                    save.ApplyTransferredTerrainCell(move.Source, move.SourceChunk, move.SourceLocal, move.NextSource);
                    move.DestinationWritten = true;
                    save.ApplyTransferredTerrainCell(move.Destination, move.DestinationChunk, move.DestinationLocal, move.NextDestination);
                    if (move.SourceCrops != null)
                    {
                        using var scope = GameplayWorldRegistry.UseWorld(move.Source);
                        move.CropMoved = move.SourceCrops.TransferCropTo(move.SourceLocal, move.DestinationCrops, move.DestinationLocal);
                        if (!move.CropMoved) throw new InvalidOperationException("源作物快照没有对应的正式植株。");
                    }
                }
            }
            catch (Exception failure)
            {
                try { Dispose(); }
                catch (Exception rollback) { throw new AggregateException("地格移交及回滚失败，保留世界以便恢复。", failure, rollback); }
                throw;
            }
        }

        private static ChunkAgricultureRenderer FindCropRenderer(ChunkMgr manager, ChunkRuntime chunk)
        {
            manager.EnsureRuntimeChunkViewBoundForStateTransfer(chunk.Address);
            return manager.TryGetRuntimeChunkView(chunk.Address, out ChunkView view)
                ? view.GetComponentInChildren<ChunkAgricultureRenderer>(true) : null;
        }

        internal void Commit() => finished = true;
        public void Dispose()
        {
            if (finished) return;
            var failures = new List<Exception>();
            for (int i = moves.Count - 1; i >= 0; i--)
            {
                CellMove move = moves[i];
                try
                {
                    if (move.CropMoved)
                    {
                        using var scope = GameplayWorldRegistry.UseWorld(move.Destination);
                        if (!move.DestinationCrops.TransferCropTo(move.DestinationLocal, move.SourceCrops, move.SourceLocal))
                            throw new InvalidOperationException("回滚时作物已失去正式归属。");
                        move.CropMoved = false;
                    }
                    if (move.DestinationWritten)
                        save.ApplyTransferredTerrainCell(move.Destination, move.DestinationChunk, move.DestinationLocal, move.OriginalDestination);
                    if (move.SourceWritten)
                        save.ApplyTransferredTerrainCell(move.Source, move.SourceChunk, move.SourceLocal, move.OriginalSource);
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException("地格移交回滚未完成。", failures);
            finished = true;
        }

        private static void PrepareTerrainCellMove(CellMove move, bool localSource, bool ground, bool support, int liquid)
        {
            var source = move.NextSource;
            var target = move.NextDestination;
            if (liquid != 0)
            {
                if (target.Liquid.LiquidLevel != 0 && target.Liquid.LiquidId != source.Liquid.LiquidId ||
                    target.Liquid.LiquidLevel + liquid > LiquidCellValue.MaximumLevel)
                    throw new InvalidOperationException("目标地格液体类型或剩余容量冲突。");
                target.Liquid.LiquidId = source.Liquid.LiquidId;
                target.Liquid.LiquidLevel += liquid;
                source.Liquid.LiquidLevel -= liquid;
                if (source.Liquid.LiquidLevel == 0) source.Liquid.LiquidId = null;
            }
            if (support)
            {
                if (target.Support.TileId != 0)
                    throw new InvalidOperationException("目标地格已有独立支撑面。");
                target.Support = FastCloner.FastCloner.DeepClone(source.Support);
                target.ExternalVisual = false;
                target.ExternallyOwned = false;
                source.Support.TileId = 0;
            }
            if (!ground)
            {
                if (localSource) MoveSoilSurfaceState(source, target);
                return;
            }
            TerrainCell sourceCell = source.Cell.ToTerrainCell(), destinationCell = target.Cell.ToTerrainCell();
            TerrainCell under = localSource ? default : source.Ground.Covers[0].UnderlyingCell.ToTerrainCell();
            var covers = target.Ground.Covers;
            var incoming = source.Ground.Covers;
            covers.Add(new GroundCoverLayerSaveData { UnderlyingCell = RuntimeTileCellSaveDelta.Capture(move.DestinationLocal,
                new TerrainCell(destinationCell.GroundTileId, destinationCell.BackTileId, 0, destinationCell.BiomeId,
                    destinationCell.NavigationCost, destinationCell.Flags & TerrainCellFlags.Walkable),
                destinationCell.BlockingTileId == 0 ? target.Cell.AccumulatedDamage : 0), UnderlyingGrass = target.Ground.SurfaceGrass });
            for (int i = 1; i < incoming.Count; i++) covers.Add(incoming[i]);
            source.Ground.Covers = new List<GroundCoverLayerSaveData>();
            source.Ground.SurfaceGrass = localSource || incoming.Count == 0 ? (byte)0 : incoming[0].UnderlyingGrass;
            source.Cell = WithGround(move.SourceLocal, source.Cell, under,
                !localSource && incoming.Count != 0 ? incoming[0].UnderlyingCell.AccumulatedDamage : 0);
            target.Cell = WithGround(move.DestinationLocal, target.Cell, sourceCell,
                sourceCell.BlockingTileId == 0 ? move.OriginalSource.Cell.AccumulatedDamage : 0);
            target.Ground.SurfaceGrass = move.OriginalSource.Ground.SurfaceGrass;
            MoveSoilSurfaceState(source, target);
        }

        private static void MoveSoilSurfaceState(TerrainCellTransferState source, TerrainCellTransferState target)
        {
            if (source.Agriculture != null) { target.Agriculture = source.Agriculture; source.Agriculture = null; }
            if (source.Snow != null)
            {
                if (target.Snow == null) target.Snow = source.Snow;
                else
                {
                    target.Snow.Depth += source.Snow.Depth;
                    target.Snow.SeasonalDepth += source.Snow.SeasonalDepth;
                    target.Snow.WeatherDepth += source.Snow.WeatherDepth;
                    target.Snow.Edited |= source.Snow.Edited;
                }
                source.Snow = null;
            }
            if (source.Contamination == null) return;
            if (target.Contamination == null) target.Contamination = source.Contamination;
            else foreach (ContaminationValueSaveData value in source.Contamination.Values)
            {
                ContaminationValueSaveData existing = target.Contamination.Values.Find(c => c.ContaminationId == value.ContaminationId);
                if (existing == null) target.Contamination.Values.Add(value); else existing.Value += value.Value;
            }
            source.Contamination = null;
        }

        private static RuntimeTileCellSaveDelta WithGround(Vector2Int local, RuntimeTileCellSaveDelta current, TerrainCell ground, float groundProgress)
            => RuntimeTileCellSaveDelta.Capture(local, new TerrainCell(ground.GroundTileId, ground.BackTileId,
                current.BlockingTileId, current.BiomeId, ground.NavigationCost,
                (ground.Flags & TerrainCellFlags.Walkable) | (current.Flags & (TerrainCellFlags.Blocking | TerrainCellFlags.Occupied))),
                current.BlockingTileId != 0 ? current.AccumulatedDamage : groundProgress);
    }

    private sealed class TerrainCellTransferState
    {
        internal RuntimeTileCellSaveDelta Cell;
        internal GroundLayerCellSaveData Ground;
        internal AgricultureCellSaveData Agriculture;
        internal LiquidCellSaveData Liquid;
        internal SupportCellSaveData Support;
        internal SnowCellSaveData Snow;
        internal ContaminationCellSaveData Contamination;
        internal bool ExternalVisual;
        internal bool ExternallyOwned;
        internal TerrainCellTransferState Copy() => FastCloner.FastCloner.DeepClone(this);
    }

    private TerrainCellTransferState CaptureTerrainCellForTransfer(ChunkRuntime chunk, Vector2Int local)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        ChunkSaveRecord record = GetRuntimeChunkRecord(chunk.Address, false);
        terrain.TryGetEnvironmentValue(TerrainSupportLayer.CostLayer, local.x, local.y, out float cost);
        terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalVisualLayer, local.x, local.y, out float external);
        terrain.TryGetEnvironmentValue(TerrainSupportLayer.ExternalOwnershipLayer, local.x, local.y, out float externalOwner);
        var result = new TerrainCellTransferState {
            Cell = RuntimeTileCellSaveDelta.Capture(local, terrain.GetCell(local.x, local.y), ReadRuntimeBuildingDamage(terrain, local.x, local.y)),
            Ground = GroundTileLayerSystem.Capture(terrain, local),
            Agriculture = FastCloner.FastCloner.DeepClone(record?.FindAgricultureCell(local)),
            Liquid = LiquidCellSaveData.Capture(terrain, local.x, local.y),
            Support = new SupportCellSaveData { LocalPosition = local, TileId = TerrainSupportLayer.GetTileId(terrain, local.x, local.y), NavigationCost = (short)cost },
            ExternalVisual = external > 0f,
            ExternallyOwned = externalOwner > 0f,
            Snow = WorldSnowInteraction.Capture(terrain, local),
            Contamination = FastCloner.FastCloner.DeepClone(record?.ContaminationCells.Find(c => c.LocalPosition == local))
        };
        if (!result.Snow.Edited && result.Snow.Depth == 0 && result.Snow.WeatherDepth == 0 &&
            result.Snow.SeasonalDepth == 0 && result.Snow.IceMeltProgress == 0) result.Snow = null;
        if (result.Agriculture is { SourceTileId: > 0 } soil)
        {
            soil.Water = FarmlandSystem.ReadWaterLevel(terrain, local);
            soil.WaterSupplyRemainder = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterSupplyRemainderLayer);
            soil.WaterConsumptionRemainder = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterConsumptionRemainderLayer);
            soil.Fertility = FarmlandSystem.Read(terrain, local, FarmlandSystem.FertilityLayer);
        }
        return result;
    }

    private void ApplyTransferredTerrainCell(string world, ChunkRuntime chunk, Vector2Int local, TerrainCellTransferState state)
    {
        using var scope = GameplayWorldRegistry.UseWorld(world);
        var data = state.Copy();
        var terrain = chunk.Terrain;
        var record = GetRuntimeChunkRecord(chunk.Address, true);
        data.Cell.LocalPosition = data.Ground.LocalPosition = data.Liquid.LocalPosition = data.Support.LocalPosition = local;
        foreach (GroundCoverLayerSaveData cover in data.Ground.Covers) cover.UnderlyingCell.LocalPosition = local;
        if (data.Agriculture != null) data.Agriculture.LocalPosition = local;
        if (data.Snow != null) data.Snow.LocalPosition = local;
        if (data.Contamination != null) data.Contamination.LocalPosition = local;
        terrain.SetCell(local.x, local.y, data.Cell.ToTerrainCell());
        WriteRuntimeBuildingDamage(terrain, local.x, local.y, data.Cell.AccumulatedDamage);
        GroundTileLayerSystem.Restore(terrain, data.Ground);
        data.Liquid.Apply(terrain);
        TerrainSupportLayer.Set(terrain, local.x, local.y, data.Support.TileId, data.Support.NavigationCost,
            data.ExternalVisual, data.ExternallyOwned);
        foreach (string layer in new[] { FarmlandSystem.SourceLayer, FarmlandSystem.WaterLayer, FarmlandSystem.WaterSupplyRemainderLayer,
            FarmlandSystem.WaterConsumptionRemainderLayer, FarmlandSystem.FertilityLayer })
            if (terrain.TryGetEnvironmentValue(layer, local.x, local.y, out _)) terrain.SetEnvironmentValue(layer, local.x, local.y, 0);
        var previous = record.ContaminationCells.Find(c => c.LocalPosition == local);
        if (previous != null)
            foreach (ContaminationValueSaveData value in previous.Values)
                if (ContaminationSystem.TryGetDefinition(value.ContaminationId, out ContaminationDefinition definition))
                    ContaminationSystem.RestoreValue(terrain, local, value.ContaminationId, definition.DefaultValue);
        WorldSnowInteraction.Restore(terrain, data.Snow ?? new SnowCellSaveData { LocalPosition = local });
        ReplaceTransferredCell(record.AgricultureCells, local, data.Agriculture, c => c.LocalPosition);
        ReplaceTransferredCell(record.ContaminationCells, local, data.Contamination, c => c.LocalPosition);
        ReplaceTransferredCell(record.SnowCells, local, data.Snow, c => c.LocalPosition);
        RestoreAgricultureTerrain(chunk, new ChunkSaveRecord { AgricultureCells = data.Agriculture == null ? new() : new() { data.Agriculture } });
        RestoreContaminationTerrain(chunk, new ChunkSaveRecord { ContaminationCells = data.Contamination == null ? new() : new() { data.Contamination } });
        var sample = new RuntimeTerrainTileSample(chunk.Address, terrain,
            new Vector2Int(chunk.Address.ChunkOrigin.X + local.x, chunk.Address.ChunkOrigin.Y + local.y), local,
            terrain.GetCell(local.x, local.y), terrain.GetTopTileId(local.x, local.y));
        RecordGroundLayerCell(sample);
        RecordLiquidChange(sample);
        RecordSupportCell(sample);
        RecordRuntimeTerrainChange(new TileBuildingCell(chunk, sample.WorldCell, local, data.Cell.GroundTileId,
            FarmlandSystem.GetBlockId(data.Cell.GroundTileId)));
    }

    private static void ReplaceTransferredCell<T>(List<T> cells, Vector2Int local, T value, Func<T, Vector2Int> position) where T : class
    {
        cells.RemoveAll(c => position(c) == local);
        if (value != null) cells.Add(value);
    }
    #endregion
}
