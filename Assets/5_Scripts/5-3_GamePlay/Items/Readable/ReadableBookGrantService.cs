using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using Newtonsoft.Json.Linq;

/// <summary>由正式定义创建带初始书页的独立读物，经过玩家主背包的原有门禁发放。</summary>
public static class ReadableBookGrantService
{
    #region 本地权威玩家发书

    public static bool TryGive(Player player, string itemDefinitionId,
        IReadOnlyList<ReadableBookPageContent> pages, out string failureReason, string writingMaterialDefinitionId = null)
    {
        if (player == null || !player.IsLocalProfile || player.Data == null || player.DestructionHandled)
        {
            failureReason = "当前本地玩家尚未就绪。";
            return false;
        }
        if (!GameNetwork.HasStateAuthority)
        {
            failureReason = "该命令仅支持单机或主机的本地玩家。";
            return false;
        }
        if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
        {
            failureReason = "库存操作正在等待联机确认，请稍后重试。";
            return false;
        }

        Inventory bag = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag)?.GetDefaultTargetInventory();
        if (bag?.Data?.itemSlots == null || bag.item != player || !bag.Data.HasCarryCapacity ||
            !PlayerCarryCapacityUtility.TryGetSnapshot(player, out PlayerCarryCapacitySnapshot capacity))
        {
            failureReason = "玩家主背包尚未初始化。";
            return false;
        }

        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.IsStartupReady ||
            !resources.TryGetItemDefinition(itemDefinitionId, out RuntimeItemDefinition definition))
        {
            failureReason = "读物定义尚未加载，请等待游戏资源就绪。";
            return false;
        }
        if (!TryGetReadableConfiguration(definition, out string stableName, out int basePageCount, out string writingMaterialTag))
        {
            failureReason = "该物品没有唯一且已启用的可阅读书籍模块。";
            return false;
        }

        ItemData book = resources.CreateItemData(definition.Id);
        if (book?.Stack == null || book.Stack.Stackable ||
            !book.ModuleDataDic.TryGetValue(stableName, out ModuleData module) ||
            module is not Ex_ModData_MemoryPackable bookData || !bookData.Enabled)
        {
            failureReason = "读物实例数据无效，或该读物不支持独立书页。";
            return false;
        }
        ItemData writingMaterial = null;
        if (writingMaterialDefinitionId != null)
        {
            if (string.IsNullOrWhiteSpace(writingMaterialTag) ||
                !resources.TryGetItemDefinition(writingMaterialDefinitionId, out RuntimeItemDefinition materialDefinition))
            {
                failureReason = "随书书写材料未配置，或该读物不允许书写。";
                return false;
            }
            writingMaterial = resources.CreateItemData(materialDefinition.Id);
            if (writingMaterial?.Stack == null || writingMaterial.Tags == null ||
                !writingMaterial.Tags.Contains(writingMaterialTag))
            {
                failureReason = "随书材料不能用于该读物的书写槽。";
                return false;
            }
            writingMaterial.Stack.Amount = 1f;
            writingMaterial.Stack.CanBePickedUp = false;
        }

        book.Stack.Amount = 1f;
        if (!ReadableBookInitialContent.TryWrite(bookData, basePageCount, pages, out failureReason, writingMaterial) ||
            !TryInsert(bag, capacity, book, out failureReason))
            return false;

        PlayerCarryCapacityUtility.RefreshOverweightSlowdown(player);
        bag.RefreshUI();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(player);
        failureReason = string.Empty;
        return true;
    }

    #endregion

    #region 定义与库存事务

    private static bool TryGetReadableConfiguration(RuntimeItemDefinition definition,
        out string stableName, out int basePageCount, out string writingMaterialTag)
    {
        stableName = null;
        basePageCount = 0;
        writingMaterialTag = null;
        foreach (RuntimeItemModuleDefinition module in definition.ModuleDefinitions)
        {
            if (!module.Enabled || !string.Equals(module.ModuleId, Mod_ReadableBook.ModuleId, StringComparison.Ordinal))
                continue;
            if (stableName != null || !definition.TryGetModuleAssembly(module.StableName, out var assembly) ||
                assembly.ModuleType != typeof(Mod_ReadableBook))
                return false;
            if (ItemDefinitionCatalogLoader.FindModulePrototype(definition.ShellPrefab, assembly.ModulePrefab,
                    module.ModuleId) is not Mod_ReadableBook prototype)
                return false;

            JObject parameters = string.IsNullOrWhiteSpace(module.ParametersJson) ? null : JObject.Parse(module.ParametersJson);
            List<ReadableBookPageDefinition> configuredPages = parameters?["pages"]?.ToObject<List<ReadableBookPageDefinition>>()
                ?? prototype.pages;
            if (configuredPages == null || configuredPages.Count == 0)
                return false;
            foreach (ReadableBookPageDefinition page in configuredPages)
                if (page == null || string.IsNullOrWhiteSpace(page.key) || string.IsNullOrWhiteSpace(page.fallback))
                    return false;
            stableName = module.StableName;
            basePageCount = configuredPages.Count;
            if (parameters?["allowHandwriting"]?.Value<bool>() ?? prototype.allowHandwriting)
                writingMaterialTag = parameters?["writingMaterialTag"]?.Value<string>() ?? prototype.writingMaterialTag;
        }
        return stableName != null;
    }

    internal static bool TryInsert(Inventory bag, PlayerCarryCapacitySnapshot capacity, ItemData book,
        out string failureReason)
    {
        if (bag?.Data?.itemSlots == null || book?.Stack == null || book.Stack.Stackable ||
            book.Stack.Amount != 1f || !ReferenceEquals(capacity.BagData, bag.Data))
        {
            failureReason = "读物或背包数据尚未初始化。";
            return false;
        }
        if (ItemNetworkStateSerialization.HasPendingLocalInventoryTransaction())
        {
            failureReason = "库存操作正在等待联机确认，请稍后重试。";
            return false;
        }
        if (capacity.GetCapacityLimitedAmount(book, 1f) < 1f)
        {
            failureReason = "携带重量或体积不足，腾出空间后再试。";
            return false;
        }

        var sourceSlot = new ItemSlot(0) { itemData = book };
        var source = new Inventory_Data(new List<ItemSlot> { sourceSlot }, "readableBook.grant");
        for (int index = 0; index < bag.Data.itemSlots.Count; index++)
        {
            ItemSlot target = bag.Data.itemSlots[index];
            if (target == null || target.itemData != null || !(target.SlotMaxVolume >= 1f) ||
                bag.IsSlotBeingDragged(index) || !bag.CanAcceptQuickTransfer(sourceSlot, target) ||
                source.GetTransferableQuantityTo(sourceSlot, bag.Data, target, 1) != 1)
                continue;

            // 只向已通过真实接收策略的空槽提交一本，失败不扩容、不落地也不改其它物品。
            if (!source.TransferItemQuantityTo(sourceSlot, bag.Data, target, 1))
                break;
            book.Stack.CanBePickedUp = false;
            bag.Data.EnsureSpareSlot();
            failureReason = string.Empty;
            return true;
        }

        failureReason = "主背包没有可接收书籍的空槽，请腾出空间后重试。";
        return false;
    }

    #endregion
}
