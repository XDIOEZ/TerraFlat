using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;

[System.Serializable]
[MemoryPackable]
public partial class EquipmentInstance_Bag : EquipmentInstance
{
    public Inventory_Data BagData = new Inventory_Data(new List<ItemSlot>(), "EquipmentBag");

    [Header("调试")]
    public bool EnableDebugLog = true;


    [Header("对象缓存")]
    [MemoryPackIgnore]
    Inventory BagInventory = new Inventory();

    [MemoryPackIgnore]
    private Inventory attachedOwnerInventory;

    private string GetInventorySummary(Inventory_Data data)
    {
        if (data == null)
            return "Data=null";

        if (data.itemSlots == null)
            return $"Name={data.Name}, Slots=null";

        int filledCount = 0;
        StringBuilder sb = new StringBuilder();
        sb.Append($"Name={data.Name}, Slots={data.itemSlots.Count}");

        for (int i = 0; i < data.itemSlots.Count; i++)
        {
            var slot = data.itemSlots[i];
            if (slot == null || slot.itemData == null)
                continue;

            filledCount++;
            sb.Append($" | [{i}] {slot.itemData.IDName} x{slot.itemData.Stack.Amount}");
        }

        sb.Append($" | Filled={filledCount}");
        return sb.ToString();
    }

    private void LogDebug(string stage, Item item = null)
    {
        if (!EnableDebugLog)
            return;

        string owner = item != null ? item.name : "null";
        string bagDataSummary = GetInventorySummary(BagData);
        string runtimeSummary = GetInventorySummary(BagInventory?.Data);
        Debug.Log($"[EquipmentInstance_Bag][{stage}] Owner={owner} | BagData=> {bagDataSummary} | Runtime=> {runtimeSummary}");
    }

    #region 库存状态恢复

    private static Inventory_Data CreateInventoryLayout(Inventory_Data source)
    {
        var slots = new List<ItemSlot>(source.itemSlots?.Count ?? 0);
        for (int i = 0; i < (source.itemSlots?.Count ?? 0); i++)
        {
            ItemSlot slot = source.itemSlots[i];
            slots.Add(new ItemSlot(i)
            {
                CanAcceptTags = slot?.CanAcceptTags == null ? new List<string>() : new List<string>(slot.CanAcceptTags),
                SlotMaxVolume = slot?.SlotMaxVolume ?? Inventory_Data.DefaultSlotVolume,
                itemData = slot?.itemData
            });
        }
        return new Inventory_Data(slots, source.Name)
        {
            ToggleActionName = source.ToggleActionName,
            UIPrefabName = source.UIPrefabName
        };
    }

    /// <summary>只建立库存布局外壳，恢复后持久数据和装备槽位共用同一实例状态。</summary>
    private void RestoreInventoryState()
    {
        BagData ??= new Inventory_Data(new List<ItemSlot>(), "EquipmentBag");
        BagInventory ??= new Inventory();
        Inventory_Data current = BagInventory.Data ?? CreateInventoryLayout(BagData);
        if (!ReferenceEquals(current, BagData))
            InventoryInstanceSnapshot.Capture(BagData).RestoreTo(current, RebaseColdItem);
        else if (current.itemSlots != null)
            foreach (ItemSlot slot in current.itemSlots)
                if (slot?.itemData != null) slot.itemData = RebaseColdItem(slot.itemData);
        BagInventory.Data = current;
        BagData = current;
    }

    private static ItemData RebaseColdItem(ItemData data) => data.SharedConfiguration == null
        ? ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance, data) : data;

    #endregion

    [Button]
    public override void Equip(Item item = null)
    {
        if (item == null)
            throw new MissingReferenceException("[EquipmentInstance_Bag] Equip 失败：item 为空");

        LogDebug("Equip-Before", item);

        RestoreInventoryState();
        BagInventory.item = item;

        var controller = item.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        BagInventory.InitData();
        BagInventory.BindController(controller);

        AttachToPlayerInventory(ResolvePlayerBagInventory(item));

        LogDebug("Equip-After", item);
    }

    public override void Update()
    {

    }

    [Button]
    public override void UnEquip(Item item = null)
    {
        LogDebug("UnEquip-Before", item);

        DetachFromPlayerInventory(ResolvePlayerBagInventory(item));

        // 卸下只解除运行态，保留被主背包和手持模块引用的同一物品对象。
        if (BagInventory?.Data != null)
            BagData = BagInventory.Data;
        if (BagInventory == null)
            return;

        BagInventory.UnbindController();
        BagInventory.UnbindRuntimeDataEvents();
        if (BagInventory.basePanel != null)
        {
            BagInventory.basePanel.Destroy();
            BagInventory.basePanel = null;
        }

        LogDebug("UnEquip-After", item);

    }

    #region 背包槽位挂接

    /// <summary>只有创造背包可以挂接装备槽位，生存背包保持固定格数。</summary>
    public void AttachToPlayerInventory(Inventory ownerInventory)
    {
        // 生存背包保持固定格数，装备不能再挂入额外槽位。
        if (ownerInventory?.Data?.HasUnlimitedSlots != true || BagData == null)
            return;

        RestoreInventoryState();
        if (attachedOwnerInventory == ownerInventory)
            return;

        if (attachedOwnerInventory != null)
            DetachFromPlayerInventory(attachedOwnerInventory);

        BagData.itemSlots ??= new List<ItemSlot>();
        if (BagData.itemSlots.Count == 0)
            return;

        ownerInventory.AppendExternalSlots(BagData.itemSlots);
        attachedOwnerInventory = ownerInventory;
    }

    /// <summary>卸下草笼时按槽位引用回收扩展格，保留格内物品供下次重新装备。</summary>
    public void DetachFromPlayerInventory(Inventory ownerInventory)
    {
        Inventory target = attachedOwnerInventory ?? ownerInventory;
        if (target == null || BagData?.itemSlots == null || BagData.itemSlots.Count == 0)
            return;

        target.RemoveExternalSlots(BagData.itemSlots);
        if (ReferenceEquals(attachedOwnerInventory, target))
            attachedOwnerInventory = null;
    }

    private static Inventory ResolvePlayerBagInventory(Item playerItem)
    {
        if (playerItem == null)
            return null;

        Mod_Inventory bagModule = playerItem.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        if (bagModule?.inventory != null)
            return bagModule.inventory;

        Mod_Inventory[] modules = playerItem.GetComponentsInChildren<Mod_Inventory>(true);
        for (int i = 0; i < modules.Length; i++)
        {
            if (modules[i]?.inventory != null)
                return modules[i].inventory;
        }

        return null;
    }

    #endregion

}
