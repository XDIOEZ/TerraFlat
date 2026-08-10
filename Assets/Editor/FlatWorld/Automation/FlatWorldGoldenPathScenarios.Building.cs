using System;
using FlatWorld.WorldModel;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实单机新区块上验证建筑召唤器、虚影和动态占地。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private enum StoneWallOccluderVerificationPhase
        {
            None,
            WaitForPlacementRebuild,
            WaitForRemovalRebuild,
            Completed
        }

        private const string GoldenBuildingSummonerId = "Wall_Wood_Summoner";
        private const string GoldenStoneWallTileBlockId = "TileBase_BuiltStoneWall";
        private const int GoldenStoneWallRuntimeTileId = 8;
        private const string BuildingPreviewSortingLayer = "Shadow";
        private const int BuildingSearchRadius = 8;

        private static Item _buildingScenarioSummoner;
        private static Item _buildingScenarioPlacedBuilding;
        private static Item _buildingScenarioLegacyStoneWall;
        private static GameObject _buildingScenarioShadowObject;
        private static Vector3 _buildingScenarioPlacement;
        private static TileBuildingCell _buildingScenarioStoneCell;
        private static ChunkLightOccluderRenderer _buildingScenarioLightOccluder;
        private static StoneWallOccluderVerificationPhase _buildingScenarioOccluderPhase;
        private static int _buildingScenarioOriginalOccluderCount;
        private static int _buildingScenarioLastOccluderRebuildVersion;
        private static int _buildingScenarioTerrainChangeFrame;
        private static bool _buildingScenarioStonePlaced;
        private static bool _buildingScenarioStoneScenarioCompleted;
        private static bool _buildingPlacementScenarioCompleted;

        #region 生命周期

        private static void ResetBuildingPlacementScenario()
        {
            _buildingScenarioSummoner = null;
            _buildingScenarioPlacedBuilding = null;
            _buildingScenarioLegacyStoneWall = null;
            _buildingScenarioShadowObject = null;
            _buildingScenarioPlacement = default;
            _buildingScenarioStoneCell = default;
            _buildingScenarioLightOccluder = null;
            _buildingScenarioOccluderPhase = StoneWallOccluderVerificationPhase.None;
            _buildingScenarioOriginalOccluderCount = -1;
            _buildingScenarioLastOccluderRebuildVersion = -1;
            _buildingScenarioTerrainChangeFrame = -1;
            _buildingScenarioStonePlaced = false;
            _buildingScenarioStoneScenarioCompleted = false;
            _buildingPlacementScenarioCompleted = false;
        }

        private static void RunBuildingPlacementScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            if (context.Player == null || ItemMgr.Instance == null || GameRes.Instance == null)
                throw new InvalidOperationException("建筑黄金路径缺少真实玩家、ItemMgr 或 GameRes。");

            ItemData summonerData = GameRes.Instance.CreateItemData(GoldenBuildingSummonerId);
            if (summonerData == null)
                throw new InvalidOperationException($"找不到建筑召唤器：{GoldenBuildingSummonerId}。");

            Vector3 playerPosition = context.Player.transform.position;
            summonerData.inHand = true;
            _buildingScenarioSummoner = ItemMgr.Instance.InstantiateItem(
                summonerData, playerPosition, Quaternion.identity, Vector3.one);
            _buildingScenarioSummoner.Owner = context.Player;
            _buildingScenarioSummoner.Load();

            Mod_Building summonerModule = _buildingScenarioSummoner.itemMods?.
                GetMod_ByID<Mod_Building>(ModText.Building);
            if (summonerModule == null || !summonerModule.IsSummoner)
                throw new InvalidOperationException("真实建筑召唤器缺少有效的 Mod_Building。");

            Vector3 placement = FindValidBuildingPlacement(
                summonerModule, playerPosition, out string lastReason);
            _buildingScenarioPlacement = placement;
            if (!summonerModule.TryCreateInstalledBuilding(
                    placement, out _buildingScenarioPlacedBuilding, out string createReason))
            {
                throw new InvalidOperationException(
                    $"新区块权威地块通过校验后仍无法创建建筑：{createReason}。");
            }

            Mod_Building placedModule = _buildingScenarioPlacedBuilding.itemMods?.
                GetMod_ByID<Mod_Building>(ModText.Building);
            Vector2Int placementCell = WorldTopologyRuntime.NormalizeCell(
                Vector2Int.FloorToInt(placement));
            if (placedModule == null || !placedModule.IsInstalled() ||
                !BuildingOccupancyRegistry.IsOccupied(placementCell))
            {
                throw new InvalidOperationException(
                    $"建筑创建后未进入已安装状态或未注册动态占地：{placementCell}。");
            }

            VerifyBuildingShadow(summonerModule);
            // 先清理木墙实体，复用已验证的安全格测试新区块石墙，避免动态占地影响候选搜索。
            CleanupBuildingPlacementObjects();
            RunLegacyStoneWallPreviewScenario(context);
            CleanupBuildingPlacementObjects();
            BeginStoneWallChunkPlacementScenario(_buildingScenarioPlacement);
            _buildingPlacementScenarioCompleted = true;
            Debug.Log(
                $"[GoldenPath][Building] 新区块放置、动态占地与虚影验证通过，" +
                $"等待跨帧检查石墙遮挡：cell={placementCell}, " +
                $"lastRejected={lastReason ?? "无"}。");
        }

        private static void AssertBuildingPlacementScenarioCompleted()
        {
            if (!_buildingPlacementScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成新区块建筑放置验证。");

            if (!_buildingScenarioStoneScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成新区块石墙迁移验证。");
        }

        private static void CleanupBuildingPlacementScenario()
        {
            CleanupBuildingPlacementObjects();
        }

        #endregion

        #region 新区块格子建筑

        private static void BeginStoneWallChunkPlacementScenario(Vector3 authorityPosition)
        {
            ChunkMgr chunkManager = ChunkMgr.Instance;
            if (chunkManager?.RuntimeChunks == null)
                throw new InvalidOperationException("石墙黄金路径缺少新区块运行时。");

            Vector2Int anchor = WorldTopologyRuntime.NormalizeCell(Vector2Int.FloorToInt(authorityPosition));
            string lastReason = "未找到候选格子";
            for (int radius = 0; radius <= BuildingSearchRadius && !_buildingScenarioStonePlaced; radius++)
            {
                for (int y = -radius; y <= radius && !_buildingScenarioStonePlaced; y++)
                for (int x = -radius; x <= radius && !_buildingScenarioStonePlaced; x++)
                {
                    if (radius > 0 && Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        continue;

                    Vector2Int cell = WorldTopologyRuntime.NormalizeCell(anchor + new Vector2Int(x, y));
                    Vector3 placement = new(cell.x + 0.5f, cell.y + 0.5f, 0f);
                    if (!chunkManager.TryGetRuntimeTerrainTile(placement,
                            out RuntimeTerrainTileSample sample))
                    {
                        lastReason = "目标新区块尚未就绪";
                        continue;
                    }

                    if (BuildingOccupancyRegistry.IsOccupied(cell))
                    {
                        lastReason = $"地块 {cell} 已被建筑占用";
                        continue;
                    }

                    ChunkLightOccluderRenderer lightOccluder = null;
                    int originalOccluderCount = -1;
                    int originalRebuildVersion = -1;
                    if (chunkManager.TryGetRuntimeChunkView(
                            sample.Address,
                            out ChunkView existingView))
                    {
                        lightOccluder = existingView.GetComponentInChildren<
                            ChunkLightOccluderRenderer>(true);
                        originalOccluderCount = lightOccluder?.ActiveOccluderCount ?? -1;
                        originalRebuildVersion = lightOccluder?.RebuildVersion ?? -1;
                    }

                    if (!TileBuildingSystem.TryPlace(placement, GoldenStoneWallTileBlockId,
                            out _buildingScenarioStoneCell, out lastReason))
                        continue;

                    _buildingScenarioStonePlaced = true;
                    if (lightOccluder == null || !lightOccluder.IsBound)
                    {
                        throw new InvalidOperationException(
                            $"石墙写入后找不到已绑定的光照遮挡层：cell={cell}。");
                    }

                    TerrainCell placedTerrain = _buildingScenarioStoneCell.RuntimeChunk.Terrain.GetCell(
                        _buildingScenarioStoneCell.LocalPosition.x,
                        _buildingScenarioStoneCell.LocalPosition.y);
                    if (!_buildingScenarioStoneCell.UsesRuntimeTerrain ||
                        _buildingScenarioStoneCell.RuntimeTileId != GoldenStoneWallRuntimeTileId ||
                        placedTerrain.BlockingTileId != _buildingScenarioStoneCell.RuntimeTileId ||
                        (placedTerrain.Flags & TerrainCellFlags.Blocking) == 0 ||
                        sample.Terrain.IsWalkable(sample.LocalCell.x, sample.LocalCell.y))
                    {
                        throw new InvalidOperationException(
                            $"石墙没有写入新区块阻挡层：cell={cell}, tile={placedTerrain.BlockingTileId}。");
                    }

                    _buildingScenarioLightOccluder = lightOccluder;
                    _buildingScenarioOriginalOccluderCount = originalOccluderCount;
                    _buildingScenarioLastOccluderRebuildVersion = originalRebuildVersion;
                    _buildingScenarioTerrainChangeFrame = Time.frameCount;
                    _buildingScenarioOccluderPhase =
                        StoneWallOccluderVerificationPhase.WaitForPlacementRebuild;
                }
            }

            if (_buildingScenarioOccluderPhase == StoneWallOccluderVerificationPhase.None)
                throw new InvalidOperationException($"玩家附近没有可验证的新区块石墙格子：{lastReason}。");
        }

        /// <summary>等待 LateUpdate 完成遮挡重建，再分两帧验证新增与移除结果。</summary>
        private static bool TickBuildingPlacementScenario()
        {
            if (_buildingScenarioOccluderPhase == StoneWallOccluderVerificationPhase.Completed)
                return true;
            if (_buildingScenarioOccluderPhase == StoneWallOccluderVerificationPhase.None ||
                Time.frameCount <= _buildingScenarioTerrainChangeFrame)
            {
                return false;
            }
            if (_buildingScenarioLightOccluder == null ||
                !_buildingScenarioLightOccluder.IsBound)
            {
                throw new InvalidOperationException("跨帧检查石墙时光照遮挡层已经解绑或销毁。");
            }
            if (_buildingScenarioLightOccluder.RebuildVersion <=
                _buildingScenarioLastOccluderRebuildVersion)
            {
                return false;
            }

            if (_buildingScenarioOccluderPhase ==
                StoneWallOccluderVerificationPhase.WaitForPlacementRebuild)
            {
                if (_buildingScenarioLightOccluder.ActiveOccluderCount <=
                    _buildingScenarioOriginalOccluderCount)
                {
                    throw new InvalidOperationException(
                        $"石墙写入后一帧光照遮挡层没有增加：" +
                        $"before={_buildingScenarioOriginalOccluderCount}, " +
                        $"after={_buildingScenarioLightOccluder.ActiveOccluderCount}。");
                }

                if (!TileBuildingSystem.TryRemove(_buildingScenarioStoneCell,
                        spawnDrop: false, out string removeReason))
                    throw new InvalidOperationException($"石墙新区块回滚失败：{removeReason}。");

                _buildingScenarioStonePlaced = false;
                _buildingScenarioLastOccluderRebuildVersion =
                    _buildingScenarioLightOccluder.RebuildVersion;
                _buildingScenarioTerrainChangeFrame = Time.frameCount;
                _buildingScenarioOccluderPhase =
                    StoneWallOccluderVerificationPhase.WaitForRemovalRebuild;
                return false;
            }

            if (_buildingScenarioLightOccluder.ActiveOccluderCount !=
                _buildingScenarioOriginalOccluderCount)
            {
                throw new InvalidOperationException(
                    $"石墙移除后一帧光照遮挡层没有恢复：" +
                    $"expected={_buildingScenarioOriginalOccluderCount}, " +
                    $"actual={_buildingScenarioLightOccluder.ActiveOccluderCount}。");
            }

            _buildingScenarioOccluderPhase = StoneWallOccluderVerificationPhase.Completed;
            _buildingScenarioStoneScenarioCompleted = true;
            Debug.Log(
                $"[GoldenPath][Building] 石墙遮挡新增与移除跨帧验证通过：" +
                $"tileId={_buildingScenarioStoneCell.RuntimeTileId}。");
            return true;
        }

        #endregion

        #region 放置与虚影

        private static void RunLegacyStoneWallPreviewScenario(
            FlatWorldGoldenPathScenarioContext context)
        {
            ItemData legacyData = GameRes.Instance.CreateItemData("TileItem_StoneWall");
            if (legacyData == null)
                throw new InvalidOperationException("找不到旧石墙物品 TileItem_StoneWall。");

            legacyData.inHand = true;
            legacyData.Stack.Amount = Mathf.Max(2f, legacyData.Stack.Amount);
            _buildingScenarioLegacyStoneWall = ItemMgr.Instance.InstantiateItem(
                legacyData, context.Player.transform.position, Quaternion.identity, Vector3.one);
            _buildingScenarioLegacyStoneWall.Owner = context.Player;
            _buildingScenarioLegacyStoneWall.Load();

            Item_Tile_Grass legacyStoneWall = _buildingScenarioLegacyStoneWall as Item_Tile_Grass;
            Vector3 placement = default;
            string reason = null;
            if (legacyStoneWall == null ||
                !legacyStoneWall.TryRefreshPlacementPreview(
                    out placement, out reason) ||
                !legacyStoneWall.HasPlacementPreview)
            {
                throw new InvalidOperationException(
                    $"旧石墙右键预览没有生成：{reason ?? "未知原因"}。");
            }

            BuildingShadow shadow = legacyStoneWall.PlacementShadow;
            int shadowLayer = SortingLayer.NameToID(BuildingPreviewSortingLayer);
            if (shadow == null || shadow.ShadowRenderer == null ||
                !shadow.ShadowRenderer.enabled || shadow.ShadowRenderer.sprite == null ||
                shadow.ShadowRenderer.sortingLayerID != shadowLayer ||
                shadow.ShadowRenderer.sortingOrder <= 0)
            {
                throw new InvalidOperationException(
                    $"旧石墙预览不可见：placement={placement}。");
            }

            Debug.Log(
                $"[GoldenPath][Building] 旧石墙已生成可见虚影并接入新区块预览：placement={placement}。");
        }

        private static Vector3 FindValidBuildingPlacement(
            Mod_Building summonerModule,
            Vector3 authorityPosition,
            out string lastReason)
        {
            lastReason = null;
            Vector2Int anchor = Vector2Int.FloorToInt(authorityPosition);
            for (int radius = 0; radius <= BuildingSearchRadius; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (radius > 0 && Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                        continue;

                    Vector2Int cell = WorldTopologyRuntime.NormalizeCell(
                        anchor + new Vector2Int(x, y));
                    Vector3 candidate = new(cell.x + 0.5f, cell.y + 0.5f, 0f);
                    _buildingScenarioSummoner.transform.position = candidate;
                    if (_buildingScenarioSummoner.itemData?.transform != null)
                        _buildingScenarioSummoner.itemData.transform.position = candidate;
                    Physics2D.SyncTransforms();

                    if (summonerModule.ValidateAuthoritativePlacement(
                            authorityPosition, out lastReason))
                        return candidate;
                }
            }

            throw new InvalidOperationException(
                $"玩家附近没有可供真实建筑召唤器放置的新地块：{lastReason ?? "未知原因"}。");
        }

        private static void VerifyBuildingShadow(Mod_Building summonerModule)
        {
            _buildingScenarioShadowObject = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            BuildingShadow shadow = _buildingScenarioShadowObject != null
                ? _buildingScenarioShadowObject.GetComponentInChildren<BuildingShadow>(true)
                : null;
            if (shadow == null || summonerModule == null ||
                !summonerModule.TryGetBuildingPreviewVisual(
                    out SpriteRenderer source, out Transform sourceRoot, out Bounds footprint))
                throw new InvalidOperationException("建筑虚影预制体或建筑本体缺少 SpriteRenderer。");

            Material fallbackMaterial = shadow.ShadowRenderer.sharedMaterial;
            shadow.InitShadow(source, sourceRoot, footprint);
            Material expectedMaterial = source.sharedMaterial != null
                ? source.sharedMaterial
                : fallbackMaterial;
            int expectedLayerId = SortingLayer.NameToID(BuildingPreviewSortingLayer);
            if (!shadow.ShadowRenderer.enabled || shadow.ShadowRenderer.sprite != source.sprite ||
                shadow.ShadowRenderer.sharedMaterial == null ||
                shadow.ShadowRenderer.sharedMaterial != expectedMaterial ||
                shadow.ShadowRenderer.sortingLayerID != expectedLayerId)
            {
                throw new InvalidOperationException("建筑虚影没有继承本体图片/材质或未进入 Shadow 排序层。");
            }
        }

        #endregion

        #region 清理

        private static void CleanupBuildingPlacementObjects()
        {
            if (_buildingScenarioStonePlaced)
            {
                TileBuildingSystem.TryRemove(_buildingScenarioStoneCell,
                    spawnDrop: false, out _);
                _buildingScenarioStonePlaced = false;
            }

            if (_buildingScenarioShadowObject != null)
            {
                _buildingScenarioShadowObject.SetActive(false);
                UnityEngine.Object.Destroy(_buildingScenarioShadowObject);
                _buildingScenarioShadowObject = null;
            }

            if (_buildingScenarioPlacedBuilding != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(_buildingScenarioPlacedBuilding, false);
                _buildingScenarioPlacedBuilding = null;
            }

            if (_buildingScenarioSummoner != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(_buildingScenarioSummoner, false);
                _buildingScenarioSummoner = null;
            }

            if (_buildingScenarioLegacyStoneWall != null && ItemMgr.Instance != null)
            {
                ItemMgr.Instance.DespawnItem(_buildingScenarioLegacyStoneWall, false);
                _buildingScenarioLegacyStoneWall = null;
            }
        }

        #endregion
    }
}
