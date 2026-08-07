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

    private static Inventory_Data CloneInventoryData(Inventory_Data source)
    {
        if (source == null)
            return new Inventory_Data(new List<ItemSlot>(), "EquipmentBag");

        try
        {
            byte[] bytes = MemoryPackSerializer.Serialize(source);
            Inventory_Data cloned = MemoryPackSerializer.Deserialize<Inventory_Data>(bytes);
            return cloned ?? new Inventory_Data(new List<ItemSlot>(), source.Name);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[EquipmentInstance_Bag] Inventory_Data 克隆失败: {ex}");
            return source;
        }
    }

    [Button]
    public override void Equip(Item item = null)
    {
        if (item == null)
            throw new MissingReferenceException("[EquipmentInstance_Bag] Equip 失败：item 为空");

        LogDebug("Equip-Before", item);

        BagInventory ??= new Inventory();
        if (BagData == null)
            BagData = new Inventory_Data(new List<ItemSlot>(), "EquipmentBag");

        // 使用快照作为运行时副本，避免运行时引用状态影响序列化还原。
        BagInventory.Data = CloneInventoryData(BagData);
        BagInventory.item = item;

        var controller = item.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        BagInventory.InitData();
        BagInventory.BindController(controller);

        LogDebug("Equip-After", item);
    }

    public override void Update()
    {

    }

    [Button]
    public override void UnEquip(Item item = null)
    {
        LogDebug("UnEquip-Before", item);

        if (BagInventory != null && BagInventory.Data != null)
        {
            // 卸下时回写最新快照，确保下次装备可以还原内容。
            BagData = CloneInventoryData(BagInventory.Data);
        }

        BagInventory.UnbindController();
        if (BagInventory.basePanel != null)
        {
            BagInventory.basePanel.Destroy();
            BagInventory.basePanel = null;
        }

        LogDebug("UnEquip-After", item);

    }

}
