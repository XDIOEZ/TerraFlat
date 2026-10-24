#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 装备眼部槽与快捷交换规则采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterQuickEquipRuntime() => RegisterQuickEquipRules();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterQuickEquipEditor() => RegisterQuickEquipRules();
#endif
    private static void RegisterQuickEquipRules()
    {
        Register("inventory", "equipment_eye_slots", CaptureEquipmentEyeSlots);
        Register("inventory", "equipment_quick_equip", CaptureEquipmentQuickEquip);
    }

    private static Data_GeneralItem QuickEquipItem(string id, string slotTag) => new()
    {
        IDName = id, Guid = id.GetHashCode(), Tags = new List<string> { slotTag },
        Stack = new ItemStack { Amount = 1f, Weight = 1f, Volume = 1f, Stackable = false },
        ModuleDataDic = new ModuleDataCollection
        {
            ["equipment"] = new Ex_ModData_MemoryPackable
            { StableName = "equipment", ModuleId = ModText.Equipment_Store }
        }
    };

    private static JObject CaptureEquipmentEyeSlots(JObject args)
    {
        var inventory = new Inventory_Equipment();
        inventory.EnsureSlotSchema();
        int head = inventory.GetSlotIndex(EquipmentSlotCatalog.Head);
        int eyes = inventory.GetSlotIndex(EquipmentSlotCatalog.Eyes);
        ItemData helmet = QuickEquipItem("probe:helmet", EquipmentSlotCatalog.HeadTag);
        ItemData glasses = QuickEquipItem("probe:glasses", EquipmentSlotCatalog.EyesTag);
        ItemData replacement = QuickEquipItem("probe:replacement", EquipmentSlotCatalog.EyesTag);
        inventory.Data.SetOne_ItemData(head, helmet);
        var result = new JObject
        {
            ["count"] = inventory.SlotRules.Count, ["head"] = head, ["eyes"] = eyes,
            ["offhand"] = inventory.GetSlotIndex(EquipmentSlotCatalog.OffHand),
            ["glassesWithHelmet"] = inventory.CanEquipAt(eyes, glasses),
            ["glassesAtHead"] = inventory.CanEquipAt(head, glasses),
            ["helmetAtEyes"] = inventory.CanEquipAt(eyes, helmet)
        };
        inventory.Data.SetOne_ItemData(eyes, glasses);
        result["helmetWithGlasses"] = inventory.CanEquipAt(head, helmet);
        helmet.Tags.Add(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Eyes);
        result["eyesUnderBlockingHelmet"] = inventory.CanEquipAt(eyes, replacement);
        result["blockingHelmetOverEyes"] = inventory.CanEquipAt(head, helmet);
        helmet.Tags.Remove(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Eyes);
        glasses.Tags.Add(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Head);
        result["headUnderBlockingGlasses"] = inventory.CanEquipAt(head, helmet);
        result["sameSlotReplacement"] = inventory.CanEquipAt(eyes, replacement);
        inventory.Data.SetOne_ItemData(head, null);
        result["blockingGlassesWithoutHead"] = inventory.CanEquipAt(eyes, glasses);
        ItemData first = QuickEquipItem("probe:any_first", EquipmentSlotCatalog.AnyTag);
        ItemData second = QuickEquipItem("probe:any_second", EquipmentSlotCatalog.AnyTag);
        first.Tags.Add(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Head);
        inventory.Data.SetOne_ItemData(head, first);
        inventory.Data.SetOne_ItemData(eyes, second);
        result["conflictingInternalSwap"] = inventory.TryMoveSlotTo(head, inventory, eyes);
        result["internalSwapUnchanged"] = ReferenceEquals(inventory.Data.itemSlots[head].itemData, first) &&
            ReferenceEquals(inventory.Data.itemSlots[eyes].itemData, second);
        inventory.Data.SetOne_ItemData(eyes, null);
        result["moveToEmptySlot"] = inventory.TryMoveSlotTo(head, inventory, eyes);
        result["emptyMoveSameInstance"] = ReferenceEquals(inventory.Data.itemSlots[eyes].itemData, first) &&
            inventory.Data.itemSlots[head].itemData == null;
        return WithEntityProbeItem(actor =>
        {
            actor.BindData(EntityProbeData("probe:vision_actor", 1f));
            Mod_Equipment equipment = actor.gameObject.AddComponent<Mod_Equipment>();
            equipment.item = actor;
            equipment.ModSaveData.StableName = "probe:vision_equipment";
            equipment.EquipmentInventory = inventory;
            actor.itemMods.AddMod(equipment);
            glasses.Tags.Remove(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Head);
            glasses.Tags.Add(EngineeringVision.CapabilityTag);
            glasses.Durability = 1f;
            inventory.Data.SetOne_ItemData(head, helmet);
            inventory.Data.SetOne_ItemData(eyes, glasses);
            result["engineeringEyesVisible"] = EngineeringVision.CanSee(actor);
            inventory.Data.SetOne_ItemData(eyes, null);
            result["engineeringWithoutEyes"] = EngineeringVision.CanSee(actor);
            return result;
        });
    }

    private static JObject CaptureEquipmentQuickEquip(JObject args)
    {
        string mode = Text(args, "mode", "empty");
        var root = new GameObject("[QuickEquipRules]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        InventoryDragTransaction drag = null;
        try
        {
            GameItem owner = root.AddComponent<GameItem>();
            owner.BindData(EntityProbeData("probe:equipment_owner", 1f));
            Mod_Equipment equipment = root.AddComponent<Mod_Equipment>();
            equipment.item = owner;
            var target = new Inventory_Equipment { item = owner };
            target.EnsureSlotSchema();
            equipment.EquipmentInventory = target;
            int eyes = target.GetSlotIndex(EquipmentSlotCatalog.Eyes);
            int head = target.GetSlotIndex(EquipmentSlotCatalog.Head);
            ItemData incoming = QuickEquipItem("probe:new_glasses", EquipmentSlotCatalog.EyesTag);
            ItemData previous = mode == "empty" ? null : QuickEquipItem("probe:old_glasses", EquipmentSlotCatalog.EyesTag);
            target.Data.SetOne_ItemData(eyes, previous);
            var source = new Inventory
            {
                item = owner,
                Data = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = incoming } }, "quick-equip-source")
            };
            if (mode == "blocked")
            {
                ItemData helmet = QuickEquipItem("probe:blocking_helmet", EquipmentSlotCatalog.HeadTag);
                helmet.Tags.Add(EquipmentSlotCatalog.BlocksSlotTagPrefix + EquipmentSlotCatalog.Eyes);
                target.Data.SetOne_ItemData(head, helmet);
            }
            if (mode == "wrong_tag") incoming.Tags.Clear();
            if (mode == "return_tag") source.Data.itemSlots[0].CanAcceptTags.Add(EquipmentSlotCatalog.HeadTag);
            if (mode == "foreign_owner") source.item = null;
            if (mode == "source_drag") drag = source.OnMouseDragBegin(0);
            if (mode == "target_drag") drag = target.OnMouseDragBegin(eyes);
            int sourceEvents = 0, targetEvents = 0;
            source.Data.Event_OnDataChanged += _ => sourceEvents++;
            target.Data.Event_OnDataChanged += _ => targetEvents++;
            // 仅准备独立数据，提交始终经过正式穿戴 API 和双向槽位事务。
            bool accepted = equipment.TryEquipFromSlot(source, mode == "invalid_index" ? -1 : 0);
            return new JObject
            {
                ["accepted"] = accepted,
                ["sourceIsIncoming"] = ReferenceEquals(source.Data.itemSlots[0].itemData, incoming),
                ["sourceIsPrevious"] = ReferenceEquals(source.Data.itemSlots[0].itemData, previous),
                ["targetIsIncoming"] = ReferenceEquals(target.Data.itemSlots[eyes].itemData, incoming),
                ["targetIsPrevious"] = ReferenceEquals(target.Data.itemSlots[eyes].itemData, previous),
                ["sourceEvents"] = sourceEvents, ["targetEvents"] = targetEvents,
                ["incomingAmount"] = incoming.Stack.Amount, ["previousAmount"] = previous?.Stack?.Amount ?? 0f,
                ["dragStarted"] = drag != null
            };
        }
        finally
        {
            drag?.Complete(false);
            Object.DestroyImmediate(root);
        }
    }

    #endregion
}
#endif
