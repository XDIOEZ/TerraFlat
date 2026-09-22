using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Building
{
    private bool _fatalDamageWasHammer;
    private bool FatalDamageWasHammer => _fatalDamageWasHammer;

    /// <summary>记录造成致命伤害的工具类别；死亡回调本身不携带伤害来源。</summary>
    private void BindFatalDamageTracking()
    {
        _fatalDamageWasHammer = false;
        damageReceiver.OnDamageReceived += TrackFatalDamageSource;
    }

    private void UnbindFatalDamageTracking()
    {
        damageReceiver.OnDamageReceived -= TrackFatalDamageSource;
        _fatalDamageWasHammer = false;
    }

    private void TrackFatalDamageSource(DamageReceiverDamageInfo damageInfo)
    {
        if (damageInfo?.IsFatal != true)
            return;

        _fatalDamageWasHammer =
            damageInfo.DamageSender is Mod_Damage damage &&
            damage.TileDamageToolKind == TileDamageToolKind.Hammer;
    }

    /// <summary>非锤类伤害摧毁建筑时不生成完整召唤器，只按制作配方随机回收材料。</summary>
    private void DestroyFromNonHammerFatalDamage()
    {
        damageReceiver.ConsumeCurrentDeath();

        Vector3 position = NormalizePlacement(item.transform.position);
        string buildingId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
        string summonerId = ResolveSummonerPrefabId(buildingId, Data);
        if (!BuildingMaterialSalvage.TrySpawnRandomMaterialSalvage(
                summonerId,
                position,
                out string reason))
        {
            Debug.LogWarning($"[建筑摧毁] {reason}", item);
        }

        BuildingOccupancyRegistry.Unregister(this);
        ItemMgr.Instance.DespawnItem(item, false);
    }

    /// <summary>单机拆除直接生成带完整快照的 ECS 库存载体，不为地面召唤器装配建筑模块或碰撞体。</summary>
    private bool TryCreateDismantledEcsDrop(out string reason)
    {
        reason = null;
        if (item?.itemData == null || Data?.Role != BuildingRole.PlacedBuilding)
        {
            reason = "当前对象不是可拆除的世界建筑";
            return false;
        }
        if (!TryCapturePlacedSnapshot(out string snapshotBase64, out reason)) return false;
        try
        {
            Vector3 position = NormalizePlacement(item.transform.position);
            string buildingId = ResolveBuildingPrefabId(item.itemData.IDName, Data);
            string summonerId = ResolveSummonerPrefabId(buildingId, Data);
            ItemData carrier = GameRes.ExistingInstance.CreateItemData(summonerId);
            carrier.Guid = GenerateUniqueRuntimeGuid();
            carrier.inHand = false;
            carrier.Stack.Amount = 1f;
            carrier.Stack.CanBePickedUp = true;
            carrier.ItemSpecialData = StatefulSummonerPrefix + carrier.Guid;
            BuildingModuleStateTransfer.Copy(item.itemData, carrier, Data.SharedModuleIds);
            CopySharedDurability(item.itemData, carrier, Data.SharedModuleIds);
            if (!WriteBuildingData(carrier, state =>
                {
                    state.Version = CurrentDataVersion;
                    state.Role = BuildingRole.Summoner;
                    state.State = BuildingState.Uninstalled;
                    state.SnapshotBase64 = snapshotBase64;
                    state.BuildingPrefabId = buildingId;
                    state.SummonerPrefabId = summonerId;
                }))
                throw new InvalidOperationException($"建筑召唤器缺少状态载荷：{summonerId}");
            Vector2 end = (Vector2)position + UnityEngine.Random.insideUnitCircle.normalized * 1.2f;
            DroppedItemService.Spawn(carrier, position, end, 0.5f,
                rotation: NormalizeBuildingRotation(item.transform.rotation).eulerAngles.z);
            return true;
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
    }
}

/// <summary>
/// 建筑被非锤类伤害摧毁时的材料回收规则。
/// 当前只从唯一、单件产出、全部为精确物品输入的普通制作配方反推材料，并按每份 50% 概率独立回收。
/// </summary>
public static class BuildingMaterialSalvage
{
    public const float DefaultRecoveryChance = 0.5f;

    /// <summary>判断物品是否为可手持的建筑召唤器。</summary>
    public static bool IsBuildingSummoner(string itemId)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null ||
            string.IsNullOrWhiteSpace(itemId) ||
            !resources.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition))
        {
            return false;
        }

        ItemData data = definition.CreateItemData();
        return Mod_Building.TryReadBuildingData(data, out _, out Mod_Building.Building_Data state) &&
               state?.Role == BuildingRole.Summoner;
    }

    /// <summary>按配方材料逐份独立投掷回收概率；随机为 0 份也是一次合法结算。</summary>
    public static bool TrySpawnRandomMaterialSalvage(
        string buildingSummonerItemId,
        Vector2 position,
        out string reason,
        float recoveryChance = DefaultRecoveryChance)
    {
        reason = null;
        if (!IsBuildingSummoner(buildingSummonerItemId))
        {
            reason = $"{buildingSummonerItemId} 不是建筑召唤器";
            return false;
        }

        if (!TryResolveSalvageRecipe(buildingSummonerItemId, out RuntimeRecipe recipe, out reason))
            return false;

        recoveryChance = Mathf.Clamp01(recoveryChance);
        Dictionary<string, int> materialAmounts = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<RuntimeRecipeIngredient> ingredients = recipe.inputs.RowItems_List;
        for (int i = 0; i < ingredients.Count; i++)
        {
            RuntimeRecipeIngredient ingredient = ingredients[i];
            if (ingredient == null || ingredient.amount <= 0)
                continue;

            materialAmounts.TryGetValue(ingredient.ItemName, out int current);
            materialAmounts[ingredient.ItemName] = current + ingredient.amount;
        }

        foreach (KeyValuePair<string, int> material in materialAmounts)
        {
            int recovered = 0;
            for (int unit = 0; unit < material.Value; unit++)
            {
                if (UnityEngine.Random.value < recoveryChance)
                    recovered++;
            }

            if (recovered <= 0)
                continue;

            try
            {
                DroppedItemService.SpawnLoot(material.Key, position, recovered);
            }
            catch (Exception exception)
            {
                reason = $"回收材料 {material.Key} 失败：{exception.Message}";
                return false;
            }
        }

        return true;
    }

    /// <summary>只接受能无歧义还原“建造这一件物品所消耗材料”的制作配方。</summary>
    private static bool TryResolveSalvageRecipe(
        string buildingSummonerItemId,
        out RuntimeRecipe recipe,
        out string reason)
    {
        recipe = null;
        reason = null;
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null)
        {
            reason = "游戏资源目录尚未加载";
            return false;
        }

        foreach (RuntimeRecipe candidate in resources.recipeById.Values)
        {
            if (!IsExactSingleBuildingRecipe(candidate, buildingSummonerItemId))
                continue;

            if (recipe != null)
            {
                reason = $"建筑 {buildingSummonerItemId} 存在多个可用于回收的制作配方";
                return false;
            }

            recipe = candidate;
        }

        if (recipe != null)
            return true;

        reason = $"找不到建筑 {buildingSummonerItemId} 的唯一精确制作配方";
        return false;
    }

    private static bool IsExactSingleBuildingRecipe(RuntimeRecipe recipe, string buildingSummonerItemId)
    {
        if (recipe?.inputs?.recipeType != RecipeType.Crafting ||
            recipe.outputs?.results == null ||
            recipe.inputs.RowItems_List == null)
        {
            return false;
        }

        bool producesOneBuilding = false;
        for (int i = 0; i < recipe.outputs.results.Count; i++)
        {
            RuntimeRecipeResult result = recipe.outputs.results[i];
            if (result != null &&
                result.amount == 1 &&
                string.Equals(result.ItemName, buildingSummonerItemId, StringComparison.OrdinalIgnoreCase))
            {
                producesOneBuilding = true;
                break;
            }
        }

        if (!producesOneBuilding)
            return false;

        bool hasConsumedMaterial = false;
        for (int i = 0; i < recipe.inputs.RowItems_List.Count; i++)
        {
            RuntimeRecipeIngredient ingredient = recipe.inputs.RowItems_List[i];
            if (ingredient == null || ingredient.amount <= 0)
                continue;

            if (ingredient.matchMode != MatchMode.ExactItem ||
                string.IsNullOrWhiteSpace(ingredient.ItemName))
            {
                return false;
            }

            hasConsumedMaterial = true;
        }

        return hasConsumedMaterial;
    }
}
