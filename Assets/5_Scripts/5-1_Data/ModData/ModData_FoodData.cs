using FastCloner.Code;
using MemoryPack;
using System;
using UnityEngine;
using Sirenix.OdinInspector;

[System.Serializable]
[MemoryPackable]
public partial class ModData_FoodData : ModuleData
{
    #region 持久化字段

    [FoldoutGroup("持久化字段")]
    [Tooltip("食物模块完整数据")]
    [InlineEditor(ObjectFieldMode = InlineEditorObjectFieldModes.Boxed)]
    public Food FoodData = new Food();

    [FoldoutGroup("持久化字段")]
    [Tooltip("是否启用食物腐败")]
    [LabelText("启用腐败")]
    public bool EnableSpoilage = true;

    [FoldoutGroup("持久化字段")]
    [Tooltip("食物腐败累计时间（秒）")]
    [ReadOnly]
    public float SpoilageElapsedSeconds = 0f;

    [FoldoutGroup("持久化字段")]
    [Tooltip("食物腐败触发间隔（秒）")]
    public float SpoilageIntervalSeconds = 1800f;

    [FoldoutGroup("持久化字段")]
    [Tooltip("食物腐败后替换目标物品ID")]
    [LabelText("腐败目标ID")]
    public string SpoilageTargetItemID = "Meat_Rotten";

    [FoldoutGroup("持久化字段")]
    [Tooltip("多次使用食物时已累计的使用次数")]
    [ReadOnly]
    [LabelText("使用进度")]
    public float EatingProgress = 0f;

    #endregion

    #region 运行时上下文

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [FastClonerIgnore]
    [Tooltip("本次更新推进的腐败秒数")]
    [ShowInInspector]
    [ReadOnly]
    public float RuntimeSpoilageDeltaSeconds;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [FastClonerIgnore]
    [Tooltip("本次更新解析后的有效腐败间隔")]
    [ShowInInspector]
    [ReadOnly]
    public float RuntimeResolvedIntervalSeconds;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("本次更新解析后的目标物品ID")]
    [ShowInInspector]
    [ReadOnly]
    public string RuntimeResolvedTargetItemID;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("当前物品ID，用于避免自替换")]
    [ShowInInspector]
    [ReadOnly]
    public string RuntimeCurrentItemID;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("本次是否触发腐败替换")]
    [ShowInInspector]
    [ReadOnly]
    public bool RuntimeTriggered;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("本次是否出现无效配置")]
    [ShowInInspector]
    [ReadOnly]
    public bool RuntimeInvalid;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("无效配置的原因文本")]
    [ShowInInspector]
    [ReadOnly]
    public string RuntimeInvalidReason;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [Tooltip("运行时产物构建函数（由外部注入）")]
    [HideInInspector]
    public Func<string, ItemData> RuntimeCreateTargetItemData;

    [FoldoutGroup("运行时上下文")]
    [MemoryPackIgnore]
    [HideInInspector]
    public static Func<string, ItemData> SharedCreateTargetItemData;

    #endregion

    #region 数据同步

    /// <summary>
    /// 确保持久化 FoodData 始终可用。
    /// </summary>
    public Food EnsureFoodData()
    {
        FoodData ??= new Food();
        return FoodData;
    }

    /// <summary>
    /// 同步 Food 本体数据引用，不处理腐败字段。
    /// </summary>
    public void SyncFromFood(Food source)
    {
        if (source == null)
        {
            return;
        }

        FoodData = source;
    }

    /// <summary>
    /// 将当前 ModData 的腐败字段回写到 FoodData。
    /// </summary>
    public void ApplyToFoodData()
    {
        WriteBackToFood(EnsureFoodData());
    }

    /// <summary>
    /// Food 不承载腐败字段，此方法保留为兼容入口。
    /// </summary>
    public void WriteBackToFood(Food target)
    {
        if (target == null)
        {
            return;
        }
    }

    #endregion

    #region 更新驱动

    /// <summary>
    /// 食物腐败主更新：推进计时、阈值判定，并在容器上下文内执行腐败结果。
    /// </summary>
    public override void DataUpdate(float deltaTime)
    {
        // 每帧先重置本次运行状态。
        RuntimeTriggered = false;
        RuntimeInvalid = false;
        RuntimeInvalidReason = string.Empty;
        RuntimeSpoilageDeltaSeconds = Mathf.Max(0f, deltaTime);

        if (!EnableSpoilage)
        {
            return;
        }

        // 仅当模块能拿到容器上下文时才推进腐败。
        if (RuntimeOwnerInventoryData == null || RuntimeOwnerSlot == null || RuntimeOwnerItemData == null)
        {
            return;
        }

        // 物品不在当前槽位时，跳过处理，避免脏上下文误改。
        if (!ReferenceEquals(RuntimeOwnerSlot.itemData, RuntimeOwnerItemData))
        {
            return;
        }

        if (RuntimeOwnerItemData.Stack == null)
        {
            RuntimeInvalid = true;
            RuntimeInvalidReason = "当前物品Stack为空";
            return;
        }

        // 间隔优先使用运行时解析结果，没有则回退到自身配置。
        float intervalSeconds = RuntimeResolvedIntervalSeconds > 0f
            ? RuntimeResolvedIntervalSeconds
            : SpoilageIntervalSeconds;

        if (intervalSeconds <= 0f)
        {
            RuntimeInvalid = true;
            RuntimeInvalidReason = "腐败间隔无效";
            return;
        }

        // 目标ID优先使用运行时解析结果。
        string targetItemID = string.IsNullOrWhiteSpace(RuntimeResolvedTargetItemID)
            ? SpoilageTargetItemID
            : RuntimeResolvedTargetItemID;

        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            RuntimeInvalid = true;
            RuntimeInvalidReason = "腐败目标物品ID为空";
            SpoilageElapsedSeconds = 0f;
            return;
        }

        // 当前ID与目标ID一致时不允许触发替换，直接清空累计。
        if (!string.IsNullOrWhiteSpace(RuntimeCurrentItemID) && RuntimeCurrentItemID == targetItemID)
        {
            SpoilageElapsedSeconds = 0f;
            return;
        }

        // 推进累计时间并判定是否达到阈值。
        SpoilageElapsedSeconds += RuntimeSpoilageDeltaSeconds;
        if (SpoilageElapsedSeconds < intervalSeconds)
        {
            return;
        }

        // 达到阈值后重置累计并标记触发。
        SpoilageElapsedSeconds = 0f;
        if (!TryApplySpoilageToInventory(targetItemID, out string failedReason))
        {
            RuntimeInvalid = true;
            RuntimeInvalidReason = failedReason;
            return;
        }

        RuntimeTriggered = true;
        RuntimeResolvedTargetItemID = targetItemID;
    }

    private bool TryApplySpoilageToInventory(string targetItemID, out string failedReason)
    {
        failedReason = string.Empty;

        Inventory_Data inventoryData = RuntimeOwnerInventoryData;
        ItemSlot sourceSlot = RuntimeOwnerSlot;
        ItemData sourceItemData = RuntimeOwnerItemData;
        int sourceSlotIndex = RuntimeOwnerSlotIndex;

        if (inventoryData == null || inventoryData.itemSlots == null)
        {
            failedReason = "容器数据为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            failedReason = "腐败目标ID为空";
            return false;
        }

        float sourceAmount = Mathf.Max(0f, sourceItemData.Stack.Amount);
        if (sourceAmount <= 0f)
        {
            failedReason = "当前物品数量不足";
            return false;
        }

        // 单槽容器：直接把该槽位整叠替换为腐败目标。
        if (inventoryData.itemSlots.Count <= 1)
        {
            ItemData replaceData = CreateTargetItemData(targetItemID);
            if (!PrepareSpoiledItemData(replaceData, sourceItemData, sourceAmount, true))
            {
                failedReason = $"目标物品构建失败，目标ID={targetItemID}";
                return false;
            }

            sourceSlot.itemData = replaceData;
            NotifySlotChanged(inventoryData, sourceSlot, Mathf.Max(0, sourceSlotIndex));
            Debug.Log($"[FoodSpoilage] 单槽容器全量替换，槽位={sourceSlotIndex}, 原物品={sourceItemData.IDName}, 新物品={replaceData.IDName}, 数量={sourceAmount:F0}");
            return true;
        }

        // 多槽容器：当前槽位减少1，并在同容器内新增1个腐败物。
        sourceItemData.Stack.Amount -= 1f;
        if (sourceItemData.Stack.Amount <= 0f)
        {
            sourceSlot.itemData = null;
        }
        NotifySlotChanged(inventoryData, sourceSlot, Mathf.Max(0, sourceSlotIndex));

        ItemData spoiledUnitData = CreateTargetItemData(targetItemID);
        if (!PrepareSpoiledItemData(spoiledUnitData, sourceItemData, 1f, false))
        {
            // 构建失败时回滚数量，避免无损耗但丢产物。
            if (sourceSlot.itemData == null)
            {
                sourceSlot.itemData = sourceItemData;
            }
            sourceItemData.Stack.Amount += 1f;
            NotifySlotChanged(inventoryData, sourceSlot, Mathf.Max(0, sourceSlotIndex));
            failedReason = $"目标物品构建失败，目标ID={targetItemID}";
            return false;
        }

        bool addSuccess = inventoryData.TryAddItem(spoiledUnitData, true);
        if (!addSuccess)
        {
            // 容器无法放入新腐败物时回滚。
            if (sourceSlot.itemData == null)
            {
                sourceSlot.itemData = sourceItemData;
            }
            sourceItemData.Stack.Amount += 1f;
            NotifySlotChanged(inventoryData, sourceSlot, Mathf.Max(0, sourceSlotIndex));
            failedReason = "容器没有可用空间添加腐败产物";
            return false;
        }

        RefreshContainerUI(inventoryData);

        Debug.Log($"[FoodSpoilage] 单个腐败成功，槽位={sourceSlotIndex}, 原物品={sourceItemData.IDName}, 新增腐败物={spoiledUnitData.IDName}");
        return true;
    }

    /// <summary>
    /// 将当前物品在原库存槽位替换为目标物品，保留数量、GUID、位置和拾取状态。
    /// </summary>
    public bool TryReplaceCurrentItem(string targetItemID, out string failedReason)
    {
        failedReason = string.Empty;

        Inventory_Data inventoryData = RuntimeOwnerInventoryData;
        ItemSlot sourceSlot = RuntimeOwnerSlot;
        ItemData sourceItemData = RuntimeOwnerItemData;
        int sourceSlotIndex = RuntimeOwnerSlotIndex;

        if (inventoryData == null || inventoryData.itemSlots == null)
        {
            failedReason = "容器数据为空";
            return false;
        }

        if (sourceSlot == null || sourceItemData == null || sourceItemData.Stack == null)
        {
            failedReason = "当前物品槽位或Stack为空";
            return false;
        }

        if (!ReferenceEquals(sourceSlot.itemData, sourceItemData))
        {
            failedReason = "当前槽位物品已发生变化";
            return false;
        }

        if (!inventoryData.itemSlots.Contains(sourceSlot))
        {
            failedReason = "当前槽位不属于目标容器";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetItemID))
        {
            failedReason = "替换目标ID为空";
            return false;
        }

        float sourceAmount = Mathf.Max(0f, sourceItemData.Stack.Amount);
        if (sourceAmount <= 0f)
        {
            failedReason = "当前物品数量不足";
            return false;
        }

        ItemData replacementData = CreateTargetItemData(targetItemID);
        if (!PrepareSpoiledItemData(replacementData, sourceItemData, sourceAmount, true))
        {
            failedReason = $"目标物品构建失败，目标ID={targetItemID}";
            return false;
        }

        sourceSlot.itemData = replacementData;
        NotifySlotChanged(inventoryData, sourceSlot, Mathf.Max(0, sourceSlotIndex));

        // 替换完成后清理旧物品的运行时上下文，避免旧模块继续驱动槽位。
        RuntimeOwnerInventoryData = null;
        RuntimeOwnerSlot = null;
        RuntimeOwnerItemData = null;
        RuntimeOwnerSlotIndex = -1;

        Debug.Log($"[FoodReplacement] 原槽位替换，槽位={sourceSlotIndex}, 原物品={sourceItemData.IDName}, 新物品={replacementData.IDName}, 数量={sourceAmount:F0}");
        return true;
    }

    private ItemData CreateTargetItemData(string targetItemID)
    {
        Func<string, ItemData> factory = RuntimeCreateTargetItemData ?? SharedCreateTargetItemData;
        if (factory == null)
        {
            return null;
        }

        return factory.Invoke(targetItemID);
    }

    private static void NotifySlotChanged(Inventory_Data inventoryData, ItemSlot slot, int slotIndex)
    {
        slot?.RefreshUI();
        inventoryData?.Event_RefreshUI?.Invoke(slotIndex);
        if (slot != null)
        {
            inventoryData?.Event_OnDataChanged?.Invoke(slot);
        }
    }

    private static void RefreshContainerUI(Inventory_Data inventoryData)
    {
        if (inventoryData == null || inventoryData.itemSlots == null)
        {
            return;
        }

        for (int i = 0; i < inventoryData.itemSlots.Count; i++)
        {
            ItemSlot slot = inventoryData.itemSlots[i];
            slot?.RefreshUI();
            inventoryData.Event_RefreshUI?.Invoke(i);
        }
    }

    private static bool PrepareSpoiledItemData(ItemData spoiledData, ItemData sourceItemData, float amount, bool copySourceIdentity)
    {
        if (sourceItemData == null || sourceItemData.Stack == null)
        {
            return false;
        }

        if (spoiledData == null || spoiledData.Stack == null)
        {
            return false;
        }

        spoiledData.Stack.Amount = Mathf.Max(1f, amount);
        spoiledData.Stack.CanBePickedUp = sourceItemData.Stack.CanBePickedUp;

        if (copySourceIdentity)
        {
            spoiledData.Guid = sourceItemData.Guid;
            spoiledData.transform = sourceItemData.transform;
            spoiledData.inHand = sourceItemData.inHand;
        }

        return true;
    }

    #endregion

    #region 迁移与构建

    /// <summary>
    /// 从任意 ModuleData 构建 FoodModData，并兼容旧 Ex_ModData_MemoryPackable。
    /// </summary>
    public static ModData_FoodData FromModuleData(ModuleData source)
    {
        if (source is ModData_FoodData current)
        {
            current.ApplyToFoodData();
            return current;
        }

        ModData_FoodData result = new ModData_FoodData();
        if (source == null)
        {
            result.ApplyToFoodData();
            return result;
        }

        result.Name = source.Name;
        result.ID = source.ID;
        result.isRunning = source.isRunning;
        result.Type = source.Type;

        if (source is Ex_ModData_MemoryPackable legacyData)
        {
            Food legacyFood = null;
            legacyData.ReadData(ref legacyFood);
            if (legacyFood != null)
            {
                result.SyncFromFood(legacyFood);
            }
        }

        result.ApplyToFoodData();
        return result;
    }

    /// <summary>
    /// 从 Food 构建 FoodModData，便于运行时桥接。
    /// </summary>
    public static ModData_FoodData FromFood(Food source)
    {
        ModData_FoodData result = new ModData_FoodData();
        if (source == null)
        {
            result.ApplyToFoodData();
            return result;
        }

        result.SyncFromFood(source);
        result.ApplyToFoodData();
        return result;
    }

    #endregion
}
