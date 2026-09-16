using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>
/// 世界散落物的水体入口。水体判定、入水转换、浮沉与漂流都从这里进入，
/// 不再依赖丢弃/抛物线模块；任何最终落点位于水格的可拾取世界物都可接入同一套逻辑。
/// </summary>
public static class WorldItemWaterSystem
{
    private static readonly HashSet<Item> PendingSpawnChecks = new();
    private static readonly List<Item> PendingSnapshot = new();

    /// <summary>入水处理结果；Transformed 表示源物品已回收并生成替代物。</summary>
    public enum EntryResult
    {
        None,
        Activated,
        Transformed
    }

    /// <summary>
    /// 世界物品实例化后延迟到 ItemMgr Update 再检查最终位置，
    /// 允许调用方先完成手持、投射或掉落轨迹等状态设置。
    /// </summary>
    public static void ScheduleSpawnCheck(Item item)
    {
        if (item != null)
            PendingSpawnChecks.Add(item);
    }

    /// <summary>物品被回收时取消尚未执行的环境检查。</summary>
    public static void CancelSpawnCheck(Item item)
    {
        if (item != null)
            PendingSpawnChecks.Remove(item);
    }

    /// <summary>由 ItemMgr 每帧统一结算新实例的最终地块环境。</summary>
    public static void ProcessPendingSpawnChecks()
    {
        if (PendingSpawnChecks.Count == 0)
            return;

        PendingSnapshot.Clear();
        PendingSnapshot.AddRange(PendingSpawnChecks);
        PendingSpawnChecks.Clear();

        for (int i = 0; i < PendingSnapshot.Count; i++)
        {
            Item item = PendingSnapshot[i];
            if (!IsLooseWorldItemCandidate(item))
                continue;

            // 兼容未声明掉落 API 的 MOD/旧存档；普通陆地掉落也移交 ECS，不再只处理水格。
            if (DroppedItemService.UsesEntities && DroppedItemService.TryConvertLooseItem(item))
                continue;

            if (!TryResolveWaterAt(item.transform.position, out bool isWater))
            {
                // 区块表现可能仍在分帧绑定；查询失败与“明确不是水”分开处理。
                PendingSpawnChecks.Add(item);
                continue;
            }

            if (isWater)
                TryEnterWater(item, requirePickable: true);
        }

        PendingSnapshot.Clear();
    }

    /// <summary>
    /// 抛掷、投射物落地等已经确认世界落点的入口。
    /// requirePickable=false 允许落地收尾帧尚未恢复拾取标记的物品进入水体。
    /// </summary>
    public static EntryResult TryEnterWater(Item item, bool requirePickable)
    {
        if (!CanUseWorldWaterRuntime(item, requirePickable))
            return EntryResult.None;

        if (!TryResolveWaterAt(item.transform.position, out bool isWater) || !isWater)
            return EntryResult.None;

        if (DroppedItemService.UsesEntities && DroppedItemService.TryConvertLooseItem(item))
            return EntryResult.Transformed;

        WorldItemWaterRuntime existingRuntime = item.GetComponent<WorldItemWaterRuntime>();
        if (existingRuntime != null && existingRuntime.IsActive)
            return EntryResult.Activated;

        if (GameNetwork.HasStateAuthority && TryTransformOnWaterEntry(item))
            return EntryResult.Transformed;

        ActivateExistingItem(item);
        return EntryResult.Activated;
    }

    /// <summary>按权威 WorldModel 地表判断当前位置是否为水；查询失败和明确陆地区分返回。</summary>
    public static bool TryResolveWaterAt(Vector2 worldPosition, out bool isWater)
    {
        isWater = false;
        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null ||
            !chunkMgr.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        isWater = (sample.Cell.Flags & TerrainCellFlags.Water) != 0;
        return true;
    }

    /// <summary>显式结束世界水体运行态；库存 ItemData 不保存这类临时表现。</summary>
    public static void ClearRuntimeState(Item item)
    {
        CancelSpawnCheck(item);
        item?.GetComponent<WorldItemWaterRuntime>()?.Deactivate();
    }

    private static bool IsLooseWorldItemCandidate(Item item)
    {
        return CanUseWorldWaterRuntime(item, requirePickable: true) &&
               !Mod_Droping.IsDropInProgress(item);
    }

    private static bool CanUseWorldWaterRuntime(Item item, bool requirePickable)
    {
        if (item == null || item.DestructionHandled || item.itemData?.Stack == null)
            return false;
        if (item.itemData.inHand || RuntimeAiEntityUtility.IsAiEntity(item))
            return false;
        if (item.GetComponentInChildren<TileEffectReceiver>(true) != null)
            return false;
        Mod_Projectile projectile = item.GetComponentInChildren<Mod_Projectile>(true);
        if (projectile != null && projectile.HasActiveWorldAttachment)
            return false;
        if (requirePickable && !item.itemData.Stack.CanBePickedUp)
            return false;

        return true;
    }

    /// <summary>建立水体运行态并绑定到当前 ChunkView 的临时物品节点。</summary>
    private static void ActivateExistingItem(Item item)
    {
        CancelSpawnCheck(item);
        ItemWorldPlacement.TryAttachWorldModelTransientItem(item, item.transform.position);

        WorldItemWaterRuntime runtime = item.GetComponent<WorldItemWaterRuntime>();
        if (runtime == null)
            runtime = item.gameObject.AddComponent<WorldItemWaterRuntime>();
        runtime.Activate(item);
    }

    /// <summary>按物品定义执行一次入水转换；替代物直接进入水体运行态，不再伪造 0 秒掉落。</summary>
    private static bool TryTransformOnWaterEntry(Item sourceItem)
    {
        if (sourceItem?.itemData == null)
            return false;

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null ||
            !gameRes.TryGetItemDefinition(sourceItem.itemData.IDName, out RuntimeItemDefinition definition) ||
            string.IsNullOrWhiteSpace(definition.WaterEntryTransformItemId))
        {
            return false;
        }

        Vector3 position = sourceItem.transform.position;
        float amount = sourceItem.itemData.Stack.Amount;
        ItemData replacementData = gameRes.CreateItemData(definition.WaterEntryTransformItemId);
        replacementData.Stack.Amount = amount;
        replacementData.Stack.CanBePickedUp = true;

        PlayWaterEntryTransformEffects(sourceItem);

        ItemMgr itemMgr = ItemMgr.Instance;
        CancelSpawnCheck(sourceItem);
        itemMgr.DespawnItem(sourceItem, saveData: false);

        Item replacement = itemMgr.InstantiateItem(
            replacementData,
            position,
            Quaternion.identity,
            Vector3.one);
        CancelSpawnCheck(replacement);
        ActivateExistingItem(replacement);
        return true;
    }

    /// <summary>源物品自行提供入水转换表现，水系统不感知火把等具体物品类型。</summary>
    private static void PlayWaterEntryTransformEffects(Item sourceItem)
    {
        MonoBehaviour[] behaviours = sourceItem.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IWaterEntryTransformEffect effect)
                effect.PlayWaterEntryTransformEffect();
        }
    }
}
