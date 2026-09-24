using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 手持铲子右键逐次采挖地表资源。进度存在区块格子差量中，换工具和读档后仍可继续；
/// 完成时先创建掉落物，再替换 TerrainCell，避免产物创建失败造成资源丢失。
/// </summary>
public static class GroundTileHarvestSystem
{
    #region 目标与资格
    /// <summary>按地块定义校验手持工具、光标、距离及未被占用的裸露地表。</summary>
    public static bool TryResolveTarget(Mod_Damage tool, out RuntimeTerrainTileSample sample,
        out RuntimeTileDefinition definition, out GroundTileHarvestRule rule)
    {
        sample = default;
        definition = null;
        rule = null;
        if (!GameNetwork.HasStateAuthority || tool?.item == null ||
            tool.HarvestKind == ResourceToolKind.None || !tool.item.InHand ||
            tool.item.DestructionHandled || tool.item.Owner is not Player actor ||
            !actor.IsLocalProfile || actor.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<DamageReceiver>(ModText.Hp)?.Hp > 0f))
            return false;

        GameController controller = actor.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
            return false;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        GameRes resources = GameRes.ExistingInstance;
        if (manager == null || resources == null || SaveDataMgr.Instance?.SaveData == null ||
            !manager.TryGetRuntimeTerrainTile(controller.GetMouseWorldPosition(), out sample) ||
            !FarmlandSystem.IsOpen(sample) ||
            !resources.TryGetTileDefinition(sample.Cell.GroundTileId, out definition))
            return false;

        rule = definition.GroundHarvest;
        return rule != null && tool.HarvestKind == rule.RequiredTool &&
               tool.HarvestTier >= rule.MinimumTier &&
               FarmlandSystem.IsWithinReach(actor.transform.position, sample.WorldCell, rule.Reach) &&
               !FarmlandSystem.HasWorldPlant(sample.WorldCell);
    }
    #endregion

    #region 权威提交
    /// <summary>判断无阻挡地格的工作进度是否应随运行时地形差量恢复。</summary>
    public static bool IsHarvestableGround(int groundTileId)
    {
        GameRes resources = GameRes.ExistingInstance;
        return groundTileId > 0 && resources != null &&
               resources.TryGetTileDefinition(groundTileId, out RuntimeTileDefinition definition) &&
               definition.GroundHarvest != null;
    }

    /// <summary>工具品质提高单次工作量，同时保证任意铲子至少要操作两次。</summary>
    private static int ResolveRequiredUses(Mod_Damage tool, GroundTileHarvestRule rule)
    {
        float quality = Mathf.Max(tool.HarvestEfficiency, 1f + 0.5f * (tool.HarvestTier - 1));
        return Mathf.Max(rule.MinimumUsesPerTile,
            Mathf.CeilToInt(rule.BaseUsesPerTile / Mathf.Max(0.01f, quality)));
    }

    /// <summary>读取地格已积累的采挖比例，供裂纹表现使用。</summary>
    public static float ReadProgress(RuntimeTerrainTileSample sample)
    {
        Vector2Int local = sample.LocalCell;
        return sample.Terrain.TryGetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
            local.x, local.y, out float value) ? Mathf.Clamp01(value) : 0f;
    }

    /// <summary>一次右键只增加一次工作量；完成时才产出物品并替换地块。</summary>
    public static bool TryWork(Mod_Damage tool, out bool completed, out Vector2Int worldCell,
        out float useInterval)
    {
        completed = false;
        worldCell = default;
        useInterval = 0f;
        if (!TryResolveTarget(tool, out RuntimeTerrainTileSample sample,
                out RuntimeTileDefinition definition, out GroundTileHarvestRule rule))
            return false;

        GameRes resources = GameRes.ExistingInstance;
        if (!resources.TryGetTileDefinition(rule.ReplacementTileId, out RuntimeTileDefinition replacement) ||
            replacement.RuntimeTileId <= 0 ||
            !resources.TryGetItemDefinition(rule.ItemId, out RuntimeItemDefinition product) ||
            product.IsActor ||
            !ChunkMgr.ExistingInstance.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            throw new InvalidOperationException($"地块 {definition.Id} 的采挖产物或替换地表未登记。");

        Vector2Int local = sample.LocalCell;
        worldCell = sample.WorldCell;
        useInterval = rule.UseInterval;
        float previousProgress = ReadProgress(sample);
        int requiredUses = ResolveRequiredUses(tool, rule);
        float progress = Mathf.Min(1f, previousProgress + 1f / requiredUses);
        completed = progress >= 0.9999f;
        if (!completed)
        {
            try
            {
                sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                    local.x, local.y, progress);
                SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(
                    chunk, sample.WorldCell, local, definition.RuntimeTileId, definition.Id));
            }
            catch (Exception exception)
            {
                sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                    local.x, local.y, previousProgress);
                Debug.LogError($"[地表采挖] {definition.Id} 进度保存失败：{exception}");
                return false;
            }

            return true;
        }

        Vector2 position = (Vector2)sample.WorldCell + Vector2.one * 0.5f;
        DroppedItemHandle drop = DroppedItemService.SpawnLoot(rule.ItemId, position, rule.Amount);
        if (!drop.IsValid)
        {
            completed = false;
            return false;
        }

        try
        {
            TerrainCell previous = sample.Cell;
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                local.x, local.y, 0f);
            sample.Terrain.SetCell(local.x, local.y, new TerrainCell(
                replacement.RuntimeTileId, 0, 0, previous.BiomeId,
                previous.NavigationCost, previous.Flags));
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(
                chunk, sample.WorldCell, local, replacement.RuntimeTileId, replacement.Id));
        }
        catch (Exception exception)
        {
            sample.Terrain.SetCell(local.x, local.y, sample.Cell);
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                local.x, local.y, previousProgress);
            DroppedItemService.Remove(drop);
            Debug.LogError($"[地表采挖] {definition.Id} 提交失败：{exception}");
            completed = false;
            return false;
        }

        ItemActionFeedback.Show(tool.item.Owner, $"挖掘完成，获得{product.DisplayName} ×{rule.Amount}。");
        return true;
    }
    #endregion
}
