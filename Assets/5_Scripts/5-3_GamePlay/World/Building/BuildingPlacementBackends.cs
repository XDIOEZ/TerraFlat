using System;
using UnityEngine;
using FlatWorld.WorldModel;
using Building_Data = Mod_Building.Building_Data;

namespace FlatWorld.Gameplay.Building
{
    internal static class BuildingPlacementCandidate
    {
        #region 候选数据
        public static ItemData Create(BuildingPlacementRequest request, out bool restoredSnapshot)
        {
            if (!Mod_Building.TryCreatePlacementCandidateData(request.SourceData, request.Position,
                    out ItemData data, out restoredSnapshot, out string reason))
                throw new InvalidOperationException(reason);
            if (Mod_Building.SupportsHorizontalMirrorPlacement(request.SourceData) &&
                !Mod_Building.TrySetHorizontalMirrorPlacement(data, request.HorizontalMirrorX))
                throw new InvalidOperationException("建筑左右朝向数据无效");
            request.Extension?.PreparePlacedData(data);
            request.CandidatePrepared?.Invoke(data);
            return data;
        }
        #endregion
    }

    internal sealed class TileBuildingPlacementBackend : IBuildingPlacementBackend
    {
        #region 格子建筑
        public bool CanHandle(BuildingPlacementRequest request)
            => Mod_Building.TryReadBuildingData(request.SourceData, out _, out Building_Data state) &&
               !string.IsNullOrWhiteSpace(state.TileBlockId);

        public BuildingPlacementResult Place(BuildingPlacementRequest request)
        {
            Mod_Building.TryReadBuildingData(request.SourceData, out _, out Building_Data state);
            if (!Mod_Building.ValidateTileDataPlacement(request.SourceData, request.Position, out string reason))
                throw new InvalidOperationException(reason);
            TileBuildingCell cell = default;
            try
            {
                if (!TileBuildingSystem.TryPlace(request.Position, state.TileBlockId, out cell, out reason))
                    throw new InvalidOperationException(reason);
                return new BuildingPlacementResult(request.BuildingId, () => Remove(cell),
                    () => RuntimeGrassClearing.ClearAt(request.Position));
            }
            catch
            {
                // 地形已写入后通知可能抛异常，后端在交出结果前也承担撤销责任。
                if ((cell.Map != null || cell.RuntimeChunk != null) && !Remove(cell))
                    throw new InvalidOperationException("格子候选未能撤销");
                throw;
            }
        }

        private static bool Remove(TileBuildingCell cell)
        {
            try
            {
                if (TileBuildingSystem.TryRemove(cell, false, out string reason)) return true;
                Debug.LogError("[建筑安装] 格子撤销报告失败：" + reason);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            // 地形通知可能在删除完成后报错，直接核对权威数据再决定是否退料。
            if (cell.RuntimeChunk != null)
            {
                ChunkTerrainData terrain = cell.RuntimeChunk.Terrain;
                if (terrain == null || terrain.IsDisposed) return false;
                Vector2Int local = cell.LocalPosition;
                return cell.ReplacedGroundCell.HasValue
                    ? TerrainSupportLayer.GetTileId(terrain, local.x, local.y) != cell.RuntimeTileId
                    : terrain.GetCell(local.x, local.y).BlockingTileId != cell.RuntimeTileId;
            }
            return cell.Map != null && !BlockingTilemapLayer.IsBlockingTile(cell.Map.Data.GetTopTile(cell.Position));
        }
        #endregion
    }

    internal sealed class MachineBuildingPlacementBackend : IBuildingPlacementBackend
    {
        #region 纯数据机器
        public bool CanHandle(BuildingPlacementRequest request) => MachineCatalog.Get(request.BuildingId) != null;

        public BuildingPlacementResult Place(BuildingPlacementRequest request)
        {
            ItemData data = BuildingPlacementCandidate.Create(request, out _);
            MachineDefinition definition = MachineCatalog.Get(data.IDName);
            if (request.RotationQuarterTurns.HasValue)
            {
                MachineState state = MachineWorld.ReadMachineState(data);
                state.RotationQuarterTurns = definition.Rotatable ? request.RotationQuarterTurns.Value & 3 : 0;
                MachineWorld.WriteMachineState(data, state);
            }
            if (!Mod_Building.ValidateMechanicalDataPlacement(data, request.AuthorityPosition,
                    request.MaximumDistance, out string reason))
                throw new InvalidOperationException(reason);
            Mod_Building.SetInstalledDataState(data);
            if (MachineWorld.GetById(data.Guid) != null)
                throw new InvalidOperationException("机械候选 GUID 已被占用");
            try
            {
                MachineEntity machine = MachineWorld.Place(data);
                return new BuildingPlacementResult(data.IDName, () => MachineWorld.DiscardPlacementCandidate(machine.Id),
                    () => RuntimeGrassClearing.ClearAt(request.Position), machine: machine, placedData: data);
            }
            catch
            {
                if (!MachineWorld.DiscardPlacementCandidate(data.Guid))
                    throw new InvalidOperationException("机械候选未能撤销");
                throw;
            }
        }
        #endregion
    }

    internal sealed class ItemBuildingPlacementBackend : IBuildingPlacementBackend
    {
        #region Item 建筑
        public bool CanHandle(BuildingPlacementRequest request) => true;

        public BuildingPlacementResult Place(BuildingPlacementRequest request)
        {
            if (ItemMgr.Instance == null) throw new InvalidOperationException("物品管理器尚未就绪");
            ItemData data = BuildingPlacementCandidate.Create(request, out bool restoredSnapshot);
            Item candidate = null;
            try
            {
                candidate = ItemMgr.Instance.InstantiateItem(data, data.transform.position,
                    data.transform.rotation, data.transform.scale);
                candidate.Load();
                Mod_Building building = candidate.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
                if (building == null) throw new MissingComponentException(data.IDName + " 缺少建筑模块");
                if (!building.ValidateAuthoritativePlacement(request.AuthorityPosition, out string reason, request.Actor))
                    throw new InvalidOperationException(reason);
                building.SetAsInstalled(initializeHealth: !restoredSnapshot);
                return new BuildingPlacementResult(data.IDName, () => Remove(candidate),
                    () =>
                    {
                        building.ClearPlacementGrass();
                        BuildingPlacementLifecycle.NotifyCommitted(candidate);
                    }, item: candidate, placedData: data);
            }
            catch
            {
                if (!Remove(candidate)) throw new InvalidOperationException("Item 建筑候选未能撤销");
                throw;
            }
        }

        private static bool Remove(Item candidate)
        {
            if (candidate == null || candidate.DestructionHandled) return true;
            try { candidate.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.ReleasePlacementOccupancy(); }
            catch (Exception exception) { Debug.LogException(exception); }
            if (ItemMgr.Instance == null) return false;
            try { ItemMgr.Instance.DespawnItem(candidate, false); }
            catch (Exception exception) { Debug.LogException(exception); }
            return candidate == null || candidate.DestructionHandled;
        }
        #endregion
    }
}
