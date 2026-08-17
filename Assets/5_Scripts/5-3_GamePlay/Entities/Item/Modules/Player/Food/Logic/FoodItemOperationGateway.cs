using System;
using UnityEngine;

/// <summary>
/// 食物对库存和手持物品的最小操作契约。
/// 食物数据与执行器不直接知道槽位、UI 和快捷栏，所有可变更操作统一经过这里。
/// </summary>
public interface IFoodItemOperationGateway
{
    void BindInventoryContext(Inventory_Data inventoryData, ItemSlot slot, int slotIndex);
    void ClearInventoryContext();
    bool TryConsumeOne(IFoodRuntimeContext food, out bool depleted, out string reason);
    bool TryReplaceCurrentItem(IFoodRuntimeContext food, string targetItemID, out string reason);
    ItemData CreateItemData(string targetItemID);
}

/// <summary>
/// 食物物品操作网关的默认实现。
/// 这里集中处理原槽位替换、数量扣除、运行时对象同步和 UI 刷新，避免业务服务依赖库存细节。
/// </summary>
public sealed class InventoryFoodItemOperationGateway : IFoodItemOperationGateway
{
    private Inventory_Data inventoryData;
    private ItemSlot slot;
    private int slotIndex = -1;
    private bool HasInventoryContext => inventoryData != null && slot?.itemData != null;

    /// <summary>绑定一次食物操作所需的库存槽位上下文。</summary>
    public void BindInventoryContext(Inventory_Data inventoryData, ItemSlot slot, int slotIndex)
    {
        this.inventoryData = inventoryData;
        this.slot = slot;
        this.slotIndex = slotIndex;
    }

    /// <summary>结束当前食物操作并释放槽位上下文。</summary>
    public void ClearInventoryContext()
    {
        inventoryData = null;
        slot = null;
        slotIndex = -1;
    }

    /// <summary>扣除一件食物；有库存上下文时同步槽位，否则处理临时运行时物品。</summary>
    public bool TryConsumeOne(IFoodRuntimeContext food, out bool depleted, out string reason)
    {
        depleted = false;
        reason = string.Empty;

        TryResolveOwnerHotbarContext(food);

        if (food == null || food.ItemData == null || food.ItemData.Stack == null)
        {
            reason = "当前食物或数量数据为空";
            return false;
        }

        if (HasInventoryContext && ReferenceEquals(slot.itemData, food.ItemData))
        {
            ItemData sourceData = slot.itemData;
            float amount = Mathf.Max(0f, sourceData.Stack.Amount);
            if (amount <= 0f)
            {
                reason = "当前物品数量不足";
                return false;
            }

            inventoryData.Event_OnBeforeDataChanged?.Invoke(slot);
            sourceData.Stack.Amount = Mathf.Max(0f, amount - 1f);
            depleted = sourceData.Stack.Amount <= 0f;
            if (depleted)
                slot.itemData = null;

            NotifySlotChanged(inventoryData, slot, slotIndex);
            RefreshOwnerHotbar(food.Item, slotIndex);
            if (depleted)
                ClearInventoryContext();
            return true;
        }

        float runtimeAmount = Mathf.Max(0f, food.ItemData.Stack.Amount);
        if (runtimeAmount <= 0f)
        {
            reason = "当前物品数量不足";
            return false;
        }

        food.ItemData.Stack.Amount = Mathf.Max(0f, runtimeAmount - 1f);
        depleted = food.ItemData.Stack.Amount <= 0f;
        food.Item?.OnUIRefresh?.Invoke();
        if (depleted)
            food.Item?.DestroySelf();
        return true;
    }

    /// <summary>创建目标物品数据，并保留当前食物的运行时身份信息。</summary>
    public bool TryReplaceCurrentItem(IFoodRuntimeContext food, string targetItemID, out string reason)
    {
        reason = string.Empty;
        if (food == null || food.ItemData == null)
        {
            reason = "当前食物数据为空";
            return false;
        }

        TryResolveOwnerHotbarContext(food);

        if (HasInventoryContext && ReferenceEquals(slot.itemData, food.ItemData))
        {
            bool replaced = TryReplaceSlot(
                inventoryData,
                slot,
                slotIndex,
                targetItemID,
                out reason);
            if (replaced)
            {
                RefreshOwnerHotbar(food.Item, slotIndex);
                ClearInventoryContext();
            }

            return replaced;
        }

        ItemData replacementData = CreateItemData(targetItemID);
        if (!TryPrepareReplacement(replacementData, food.ItemData, out reason))
            return false;

        food.Item.BindData(replacementData);
        food.Item.OnUIRefresh?.Invoke();
        return true;
    }

    /// <summary>通过游戏资源系统创建指定 ID 的物品数据。</summary>
    public ItemData CreateItemData(string targetItemID)
    {
        if (string.IsNullOrWhiteSpace(targetItemID))
            return null;

        try
        {
            return GameRes.Instance?.CreateItemData(targetItemID.Trim());
        }
        catch (Exception exception)
        {
            Debug.LogError($"[FoodItemOperation] 创建物品数据失败，目标ID={targetItemID}：{exception.Message}");
            return null;
        }
    }

    /// <summary>供库存模块数据观察者使用的无执行器替换入口。</summary>
    public static bool TryReplaceSlot(
        Inventory_Data inventoryData,
        ItemSlot sourceSlot,
        int sourceSlotIndex,
        string targetItemID,
        out string reason)
    {
        reason = string.Empty;
        if (inventoryData == null || sourceSlot == null || sourceSlot.itemData == null)
        {
            reason = "库存或源槽位为空";
            return false;
        }

        if (inventoryData.itemSlots == null || !inventoryData.itemSlots.Contains(sourceSlot))
        {
            reason = "源槽位不属于当前库存";
            return false;
        }

        ItemData sourceData = sourceSlot.itemData;
        ItemData replacementData;
        try
        {
            replacementData = GameRes.Instance?.CreateItemData(targetItemID);
        }
        catch (Exception exception)
        {
            reason = $"目标物品创建失败：{exception.Message}";
            return false;
        }

        if (!TryPrepareReplacement(replacementData, sourceData, out reason))
            return false;

        inventoryData.Event_OnBeforeDataChanged?.Invoke(sourceSlot);
        sourceSlot.itemData = replacementData;
        NotifySlotChanged(inventoryData, sourceSlot, sourceSlotIndex);
        return true;
    }

    private static bool TryPrepareReplacement(ItemData replacementData, ItemData sourceData, out string reason)
    {
        reason = string.Empty;
        if (replacementData == null)
        {
            reason = "目标物品定义不存在";
            return false;
        }

        if (sourceData == null || sourceData.Stack == null)
        {
            reason = "源物品数量数据为空";
            return false;
        }

        // 替换状态时保留数量、拾取状态、GUID、手持状态和空间变换。
        replacementData.Stack ??= new ItemStack();
        replacementData.Stack.Amount = Mathf.Max(1f, sourceData.Stack.Amount);
        replacementData.Stack.CanBePickedUp = sourceData.Stack.CanBePickedUp;
        replacementData.Guid = sourceData.Guid;
        replacementData.inHand = sourceData.inHand;
        replacementData.transform = CopyTransform(sourceData.transform);
        return true;
    }

    private static ItemTransform CopyTransform(ItemTransform source)
    {
        if (source == null)
            return new ItemTransform();

        return new ItemTransform
        {
            position = source.position,
            rotation = source.rotation,
            scale = source.scale
        };
    }

    private static void NotifySlotChanged(Inventory_Data inventoryData, ItemSlot slot, int slotIndex)
    {
        // 统一通知槽位、库存和快捷栏依赖的 UI 刷新链路。
        slot?.RefreshUI();
        inventoryData?.Event_RefreshUI?.Invoke(Mathf.Max(0, slotIndex));
        inventoryData?.Event_OnDataChanged?.Invoke(slot);
    }

    private static void RefreshOwnerHotbar(Item item, int slotIndex)
    {
        // 如果物品来自快捷栏，立即同步当前手持物和网络状态。
        Inventory_HotBar hotbar = item?.Owner?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        if (hotbar == null)
            return;

        hotbar.RefreshUI(slotIndex);
        hotbar.RuntimeInventory?.SyncHeldItemImmediately();
        hotbar.NotifyOwnerNetworkStateChanged();
    }

    private void TryResolveOwnerHotbarContext(IFoodRuntimeContext food)
    {
        // 右键临时对象没有显式槽位时，尝试从玩家当前选中快捷栏反查上下文。
        if (HasInventoryContext || food?.Item == null)
            return;

        Inventory_HotBar hotbar = food.Item.Owner?.itemMods?.GetMod_ByID<Inventory_HotBar>(ModText.Hotbar);
        ItemSlot selectedSlot = hotbar?.CurrentSelectItemSlot;
        Inventory_Data hotbarData = hotbar?.Data;
        if (hotbarData == null || selectedSlot == null ||
            !ReferenceEquals(selectedSlot.itemData, food.ItemData))
            return;

        BindInventoryContext(hotbarData, selectedSlot, hotbar.CurrentIndex);
    }
}
