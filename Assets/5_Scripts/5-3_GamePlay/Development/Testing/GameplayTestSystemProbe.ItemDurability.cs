using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 基类耐久规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterItemDurabilityRules()
    {
        Register("inventory", "durability_consume", ProbeDurabilityConsume);
        Register("inventory", "durability_transfer", ProbeDurabilityTransfer);
        Register("inventory", "durability_add_sort_save", ProbeDurabilityAddSortSave);
    }

    private static ItemData DurabilityProbeItem(float amount = 5f, float durability = 7f, float maximum = 20f)
        => new Data_GeneralItem { IDName = "durability-probe", Guid = 701,
            Stack = new ItemStack { Amount = amount, Stackable = true, Weight = 1f, Volume = 1f },
            MaxDurability = maximum, Durability = durability, Tags = new List<string> { "Wood" } };

    private static JObject ProbeDurabilityConsume(JObject args)
    {
        ItemData data = DurabilityProbeItem((float)Number(args, "amount", 5),
            (float)Number(args, "durability", 1), (float)Number(args, "maximum", 20));
        data.Stack.Stackable = Flag(args, "stackable", true);
        var slot = new ItemSlot(0) { itemData = data };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "durability-probe");
        int changed = 0, refreshed = 0;
        inventory.Event_OnDataChanged += _ => changed++;
        inventory.Event_RefreshUI += _ => refreshed++;
        ItemData expected = Flag(args, "stale", false) ? ItemInstanceDataFactory.CloneRuntime(data) : data;
        bool accepted = inventory.TryConsumeDurability(expected, (float)Number(args, "wear", 1), out bool broke);
        bool repeated = Flag(args, "repeat", false) && inventory.TryConsumeDurability(data, 1f, out _);
        return new JObject { ["accepted"] = accepted, ["broke"] = broke, ["empty"] = slot.itemData == null,
            ["amount"] = data.Stack.Amount, ["durability"] = data.Durability, ["changed"] = changed,
            ["refreshed"] = refreshed, ["repeated"] = repeated, ["stillMaterial"] = !data.Tags.Contains("Tool") };
    }

    private static JObject ProbeDurabilityTransfer(JObject args)
    {
        ItemData data = DurabilityProbeItem();
        var from = new ItemSlot(0) { itemData = data };
        var to = new ItemSlot(0) { SlotMaxVolume = (float)Number(args, "capacity", 20) };
        if (Flag(args, "target", false))
        {
            to.itemData = DurabilityProbeItem(2f, (float)Number(args, "targetDurability", 20));
            to.itemData.Guid = 702;
        }
        var source = new Inventory_Data(new List<ItemSlot> { from }, "source");
        var destination = new Inventory_Data(new List<ItemSlot> { to }, "target");
        int count = (int)Number(args, "count", 1);
        int preview = source.GetTransferableQuantityTo(from, destination, to, count);
        bool unchanged = data.Stack.Amount == 5f && data.Durability == 7f;
        bool success = Flag(args, "hand", false) ? source.ChangeItemAmount(from, to, count)
            : source.TransferItemQuantityTo(from, destination, to, count);
        return new JObject { ["accepted"] = success, ["preview"] = preview, ["previewUnchanged"] = unchanged,
            ["sourceAmount"] = from.itemData?.Stack.Amount ?? 0f, ["sourceDurability"] = from.itemData?.Durability ?? 0f,
            ["targetAmount"] = to.itemData?.Stack.Amount ?? 0f, ["targetDurability"] = to.itemData?.Durability ?? 0f,
            ["sameReference"] = ReferenceEquals(to.itemData, data),
            ["separateGuid"] = from.itemData == null || to.itemData == null || from.itemData.Guid != to.itemData.Guid };
    }

    private static JObject ProbeDurabilityAddSortSave(JObject args)
    {
        ItemData data = DurabilityProbeItem();
        var slots = new List<ItemSlot>();
        for (int i = 0; i < 3; i++) slots.Add(new ItemSlot(i) { SlotMaxVolume = 2 });
        var inventory = new Inventory_Data(slots, "add-probe");
        inventory.TryAddItem(data, false, out float preview);
        bool previewUnchanged = data.Durability == 7f && data.Stack.Amount == 5f && slots.TrueForAll(s => s.itemData == null);
        inventory.TryAddItem(data, true, out float added);
        var portions = new JArray();
        foreach (ItemSlot slot in slots)
            portions.Add(new JObject { ["amount"] = slot.itemData?.Stack.Amount ?? 0f, ["durability"] = slot.itemData?.Durability ?? 0f });

        // 使用真实实例二进制快照恢复到当前基础配置，不能靠克隆代替存档验证。
        byte[] bytes = MemoryPackSerializer.Serialize(ItemInstanceSnapshot.Capture(data));
        ItemData restored = DurabilityProbeItem(1f, 20f);
        MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(bytes).RestoreTo(restored);

        var full = DurabilityProbeItem(2f, 20f);
        var worn = DurabilityProbeItem(3f, 6f);
        var secondWorn = DurabilityProbeItem(4f, 9f);
        var organized = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = full },
            new(1) { itemData = worn }, new(2) { itemData = secondWorn } }, "organize-probe");
        bool sorted = organized.Organize();
        var result = new JArray();
        foreach (ItemSlot slot in organized.itemSlots)
            if (slot.itemData != null) result.Add(new JObject { ["amount"] = slot.itemData.Stack.Amount, ["durability"] = slot.itemData.Durability });
        return new JObject { ["preview"] = preview, ["previewUnchanged"] = previewUnchanged, ["added"] = added,
            ["portions"] = portions, ["savedAmount"] = restored.Stack.Amount, ["savedDurability"] = restored.Durability,
            ["sorted"] = sorted, ["organized"] = result };
    }
    #endregion
}
