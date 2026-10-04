using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 手持工具持续右键逐次采挖地表资源。进度存在区块格子差量中，换工具和读档后仍可继续；
/// 完成时先创建掉落物，再替换 TerrainCell，避免产物创建失败造成资源丢失。
/// </summary>
public static class GroundTileHarvestSystem
{
    #region 目标与资格
    /// <summary>按地块定义校验手持工具、光标、距离及未被占用的裸露地表。</summary>
    public static bool TryResolveTarget(Mod_ResourceToolBase tool, out RuntimeTerrainTileSample sample,
        out RuntimeTileDefinition definition, out GroundTileHarvestRule rule)
        => TryResolveTarget(tool, out sample, out definition, out rule, out _);

    /// <summary>返回采挖被拒绝的具体原因，供右键反馈使用。</summary>
    public static bool TryResolveTarget(Mod_ResourceToolBase tool, out RuntimeTerrainTileSample sample,
        out RuntimeTileDefinition definition, out GroundTileHarvestRule rule, out string reason)
    {
        sample = default;
        definition = null;
        rule = null;
        reason = null;
        if (!GameNetwork.HasStateAuthority || tool?.item == null ||
            tool.HarvestKind == ResourceToolKind.None || !tool.item.InHand ||
            tool.item.DestructionHandled || tool.item.Owner is not Player actor ||
            !actor.IsLocalProfile || actor.DestructionHandled ||
            !(actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f))
        {
            reason = "当前无法使用采挖工具。";
            return false;
        }

        Mod_GameController controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
        {
            reason = "当前无法指向世界地块。";
            return false;
        }

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        GameRes resources = GameRes.ExistingInstance;
        if (manager == null || resources == null || SaveDataMgr.Instance?.SaveData == null ||
            !manager.TryGetRuntimeTerrainTile(controller.GetMouseWorldPosition(), out sample))
        {
            reason = "目标地块尚未加载。";
            return false;
        }

        if (!resources.TryGetTileDefinition(sample.Cell.GroundTileId, out definition))
        {
            reason = "这里没有可挖掘的地表。";
            return false;
        }

        rule = definition.GroundHarvest;
        if (rule == null)
        {
            reason = $"{definition.DisplayName}无法开采。";
            return false;
        }
        if (tool.HarvestKind != rule.RequiredTool)
        {
            reason = "这块地不能用当前工具挖掘。";
            return false;
        }
        if (tool.HarvestTier < rule.MinimumTier)
        {
            reason = "采挖工具等级不足。";
            return false;
        }
        if (rule.NaturalLayerCount > 1 &&
            !GroundTileLayerSystem.TryGetUnderlying(sample.Terrain, sample.LocalCell, out _) &&
            GroundTileLayerSystem.ReadDepth(sample.Terrain, sample.LocalCell) >= rule.NaturalLayerCount)
        {
            reason = "天然石层已经采尽。";
            return false;
        }
        if (!FarmlandSystem.IsWithinReach(actor.transform.position, sample.WorldCell, rule.Reach))
        {
            reason = "距离地块太远。";
            return false;
        }
        if (!FarmlandSystem.IsOpen(sample) || FarmlandSystem.HasWorldPlant(sample.WorldCell))
        {
            reason = "地块被占用，无法挖掘。";
            return false;
        }
        return true;
    }

    /// <summary>白框与正式采挖共用资格判断，只提示当前工具能作用的地块。</summary>
    public static bool TryResolvePreview(Mod_ResourceToolBase tool, out RuntimeTerrainTileSample sample)
        => TryResolveTarget(tool, out sample, out _, out _);
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

    /// <summary>工具品质提高单次工作量，同时保证每层至少操作配置的最低次数。</summary>
    internal static int ResolveRequiredUses(Mod_ResourceToolBase tool, GroundTileHarvestRule rule)
    {
        float quality = Mathf.Max(tool.HarvestEfficiency, 1f + 0.5f * (tool.HarvestTier - 1));
        return Mathf.Max(rule.MinimumUsesPerTile,
            Mathf.CeilToInt(rule.BaseUsesPerTile / Mathf.Max(0.01f, quality)));
    }

    /// <summary>开采和进度表现共用下一层解析，玩家覆盖层优先露出原地块。</summary>
    public static bool TryResolveHarvestResult(RuntimeTerrainTileSample sample, RuntimeTileDefinition source,
        out RuntimeTileDefinition result)
    {
        result = null;
        GameRes resources = GameRes.ExistingInstance;
        GroundTileHarvestRule rule = source?.GroundHarvest;
        if (resources == null || rule == null) return false;
        if (GroundTileLayerSystem.TryGetUnderlying(sample.Terrain, sample.LocalCell, out GroundCoverLayerSaveData layer))
            return resources.TryGetTileDefinition(layer.UnderlyingCell.GroundTileId, out result);
        int depth = GroundTileLayerSystem.ReadDepth(sample.Terrain, sample.LocalCell);
        string id = rule.NaturalLayerCount > 1 && depth + 1 >= rule.NaturalLayerCount
            ? rule.ExhaustedTileId : rule.ReplacementTileId;
        return resources.TryGetTileDefinition(id, out result);
    }

    /// <summary>读取地格已积累的采挖比例，供裂纹表现使用。</summary>
    public static float ReadProgress(RuntimeTerrainTileSample sample)
    {
        Vector2Int local = sample.LocalCell;
        return sample.Terrain.TryGetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
            local.x, local.y, out float value) ? Mathf.Clamp01(value) : 0f;
    }

    /// <summary>一次有效挥动只增加一次工作量；完成时才产出物品并替换地块。</summary>
    public static bool TryWork(Mod_ResourceToolBase tool, out bool completed, out Vector2Int worldCell,
        out float useInterval)
        => TryWork(tool, out completed, out worldCell, out useInterval, out _);

    /// <summary>返回一次采挖的结果和拒绝原因，供工具向玩家显示。</summary>
    public static bool TryWork(Mod_ResourceToolBase tool, out bool completed, out Vector2Int worldCell,
        out float useInterval, out string failureReason)
    {
        completed = false;
        worldCell = default;
        useInterval = 0f;
        if (!TryResolveTarget(tool, out RuntimeTerrainTileSample sample,
                out RuntimeTileDefinition definition, out GroundTileHarvestRule rule, out failureReason))
            return false;

        GameRes resources = GameRes.ExistingInstance;
        if (!TryResolveHarvestResult(sample, definition, out RuntimeTileDefinition replacement) ||
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
                failureReason = "挖掘进度保存失败。";
                return false;
            }

            return true;
        }

        GroundLayerCellSaveData previousLayers = GroundTileLayerSystem.Capture(sample.Terrain, local);
        bool hasPlayerCover = GroundTileLayerSystem.TryGetUnderlying(sample.Terrain, local, out GroundCoverLayerSaveData underlying);
        Vector2 position = (Vector2)sample.WorldCell + Vector2.one * 0.5f;
        DroppedItemHandle drop = DroppedItemService.SpawnLoot(rule.ItemId, position, rule.Amount);
        if (!drop.IsValid)
        {
            completed = false;
            failureReason = "掉落物生成失败。";
            return false;
        }

        try
        {
            TerrainCell previous = sample.Cell;
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                local.x, local.y, 0f);
            if (hasPlayerCover)
            {
                GroundTileLayerSystem.PopCover(sample.Terrain, local);
                sample.Terrain.SetCell(local.x, local.y, underlying.UnderlyingCell.ToTerrainCell());
                sample.Terrain.SetGrass(local.x, local.y, underlying.UnderlyingGrass);
                sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                    local.x, local.y, underlying.UnderlyingCell.AccumulatedDamage);
            }
            else
            {
                if (rule.NaturalLayerCount > 1)
                    GroundTileLayerSystem.WriteDepth(sample.Terrain, local, previousLayers.ExcavatedLayers + 1);
                sample.Terrain.SetCell(local.x, local.y, new TerrainCell(
                    replacement.RuntimeTileId, 0, 0, previous.BiomeId,
                    previous.NavigationCost, previous.Flags));
            }
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(
                chunk, sample.WorldCell, local, replacement.RuntimeTileId, replacement.Id));
        }
        catch (Exception exception)
        {
            GroundTileLayerSystem.Restore(sample.Terrain, previousLayers);
            sample.Terrain.SetCell(local.x, local.y, sample.Cell);
            sample.Terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId,
                local.x, local.y, previousProgress);
            DroppedItemService.Remove(drop);
            SaveDataMgr.Instance.RecordGroundLayerCell(sample);
            SaveDataMgr.Instance.RecordRuntimeTerrainChange(new TileBuildingCell(
                chunk, sample.WorldCell, local, definition.RuntimeTileId, definition.Id));
            Debug.LogError($"[地表采挖] {definition.Id} 提交失败：{exception}");
            completed = false;
            failureReason = "地块更新失败。";
            return false;
        }

        // 地面已变更后同步移除独立草层，避免石地仍显示原来的草，并保存草层差量。
        if (!hasPlayerCover && sample.Terrain.GetGrass(local.x, local.y) != 0)
            RuntimeGrassClearing.Clear(sample);
        SaveDataMgr.Instance.RecordGroundLayerCell(sample);

        string layerFeedback = !hasPlayerCover && rule.NaturalLayerCount > 1
            ? previousLayers.ExcavatedLayers + 1 >= rule.NaturalLayerCount
                ? " 已露出无法开采的基岩。"
                : $" 下方还剩 {rule.NaturalLayerCount - previousLayers.ExcavatedLayers - 1} 层。"
            : string.Empty;
        ItemActionFeedback.Show(tool.item.Owner, $"挖掘完成，获得{product.DisplayName} ×{rule.Amount}。{layerFeedback}");
        return true;
    }
    #endregion
}
