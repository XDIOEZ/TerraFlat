using System;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实单机新区块上验证建筑召唤器、虚影和动态占地。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const string GoldenBuildingSummonerId = "Wall_Wood_Summoner";
        private const string BuildingPreviewSortingLayer = "Shadow";
        private const int BuildingSearchRadius = 8;

        private static Item _buildingScenarioSummoner;
        private static Item _buildingScenarioPlacedBuilding;
        private static GameObject _buildingScenarioShadowObject;
        private static bool _buildingPlacementScenarioCompleted;

        #region 生命周期

        private static void ResetBuildingPlacementScenario()
        {
            _buildingScenarioSummoner = null;
            _buildingScenarioPlacedBuilding = null;
            _buildingScenarioShadowObject = null;
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
            _buildingPlacementScenarioCompleted = true;
            Debug.Log(
                $"[GoldenPath][Building] 新区块放置、动态占地与虚影验证通过：" +
                $"cell={placementCell}, lastRejected={lastReason ?? "无"}。");
            CleanupBuildingPlacementObjects();
        }

        private static void AssertBuildingPlacementScenarioCompleted()
        {
            if (!_buildingPlacementScenarioCompleted)
                throw new InvalidOperationException("完整黄金路径结束前未完成新区块建筑放置验证。");
        }

        private static void CleanupBuildingPlacementScenario()
        {
            CleanupBuildingPlacementObjects();
        }

        #endregion

        #region 放置与虚影

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
        }

        #endregion
    }
}
