#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>采样实际网络快照合并和请求序号，不建立真实连接。</summary>
    public static class GameplayTestRadioNetworkRules
    {
        #region 快照与请求采样
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Register() => GameplayTestSystemProbe.Register("radio", "network_ownership", Capture);

        private static Inventory_Data Inventory(params ItemData[] items)
        {
            var slots = new List<ItemSlot>();
            foreach (ItemData item in items) slots.Add(new ItemSlot(slots.Count) { itemData = item });
            return new Inventory_Data(slots, "probe");
        }
        private static ItemData Device(int guid, int channel, double consumed)
        {
            var battery = new Data_GeneralItem { IDName = "probe:battery", Guid = guid + 100,
                Stack = new ItemStack { Amount = 1f, Stackable = false } };
            battery.ModuleDataDic.Add("battery", new Ex_ModData_MemoryPackable
                { StableName = "battery", ModuleId = Mod_RadioBattery.ModuleId });
            if (consumed > 0d) RadioBatteryStorage.TryConsumeExactJoules(battery, consumed);
            var radio = new Data_GeneralItem { IDName = "probe:radio", Guid = guid,
                Stack = new ItemStack { Amount = 1f, Stackable = false } };
            var binary = new Ex_ModData_MemoryPackable { StableName = "radio", ModuleId = Mod_RadioTransceiver.ModuleId };
            binary.WriteData(new RadioTransceiverState { Channel = channel, IsOn = true,
                Battery = InventoryInstanceSnapshot.Capture(Inventory(battery)) });
            radio.ModuleDataDic.Add(binary.StableName, binary);
            return radio;
        }
        private static JObject Capture(JObject args)
        {
            var root = new GameObject("RadioNetworkRules");
            root.SetActive(false);
            try
            {
                var actor = root.AddComponent<GameItem>();
                actor.BindData(new Data_GeneralItem { IDName = "probe:actor", Guid = 73001, Stack = new ItemStack() });
                var bag = root.AddComponent<Mod_Inventory>();
                var bagData = new Inventory_ModuleData { StableName = "bag", ModuleId = ModText.Bag };
                bag.ModuleInit(actor, bagData);
                bag.InventoryInstances.Add(new Inventory { item = actor, Data = Inventory(Device(73002, 3, 1800d)) });
                actor.itemMods.AddMod(bag);
                var hotbar = root.AddComponent<Mod_HotBar>();
                hotbar.ModuleInit(actor, new Ex_ModData_MemoryPackable { StableName = "hotbar", ModuleId = ModText.Hotbar });
                actor.itemMods.AddMod(hotbar);
                var equipment = root.AddComponent<Mod_Equipment>();
                equipment.ModuleInit(actor, new Ex_ModData_MemoryPackable { StableName = "equipment", ModuleId = ModText.Equipment_Module });
                equipment.EquipmentInventory = new Inventory_Equipment { item = actor };
                equipment.EquipmentInventory.EnsureSlotSchema();
                actor.itemMods.AddMod(equipment);

                var incoming = new Data_GeneralItem { IDName = "probe:actor", Guid = 73001, Stack = new ItemStack() };
                var incomingBag = new Inventory_ModuleData { StableName = "bag", ModuleId = ModText.Bag };
                incomingBag.Data["bag"] = Inventory(Device(73002, 9, 0d), Device(73003, 8, 0d));
                incoming.ModuleDataDic.Add("bag", incomingBag);
                var incomingHotbar = new Ex_ModData_MemoryPackable { StableName = "hotbar", ModuleId = ModText.Hotbar };
                incomingHotbar.WriteData(new InventoryHotBarSaveState { Version = InventoryHotBarSaveState.CurrentVersion,
                    Inventory = InventoryInstanceSnapshot.Capture(Inventory(Device(73002, 9, 0d))) });
                incoming.ModuleDataDic.Add("hotbar", incomingHotbar);
                var incomingEquipment = new Ex_ModData_MemoryPackable { StableName = "equipment", ModuleId = ModText.Equipment_Module };
                incomingEquipment.WriteData(new Mod_EquipmentSaveData
                    { EquipmentInventoryData = InventoryInstanceSnapshot.Capture(Inventory(Device(73002, 9, 0d))) });
                incoming.ModuleDataDic.Add("equipment", incomingEquipment);

                RadioInventoryNetworkAuthority.PreserveServerRadioStates(actor, incoming);
                ItemData bagRadio = incomingBag.Data["bag"].itemSlots[0].itemData;
                ItemData other = incomingBag.Data["bag"].itemSlots[1].itemData;
                ItemData hotbarRadio = incomingHotbar.GetData<InventoryHotBarSaveState>().Inventory.CreateColdData().itemSlots[0].itemData;
                ItemData equippedRadio = incomingEquipment.GetData<Mod_EquipmentSaveData>().EquipmentInventoryData.CreateColdData().itemSlots[0].itemData;
                ItemData clientRadio = Device(73002, 9, 0d);
                bag.InventoryInstances[0].Data.itemSlots[0].itemData = clientRadio;
                RadioTransceiverBinding clientBinding = RadioTransceiverSystem.Bind(clientRadio, actor);
                RadioInventoryNetworkAuthority.ApplyAuthoritativeRadioStates(actor, incoming);
                RadioTransceiverBinding bagBinding = RadioTransceiverSystem.Bind(bagRadio, null);
                RadioTransceiverBinding hotbarBinding = RadioTransceiverSystem.Bind(hotbarRadio, null);
                RadioTransceiverBinding equippedBinding = RadioTransceiverSystem.Bind(equippedRadio, null);
                RadioTransceiverBinding otherBinding = RadioTransceiverSystem.Bind(other, null);
                var sequence = new RadioRequestSequence();
                bool first = sequence.TryAccept(1), duplicate = sequence.TryAccept(1), stale = sequence.TryAccept(0), next = sequence.TryAccept(2);
                var equipped = Device(73004, 4, 0d);
                var store = new Ex_ModData_MemoryPackable { StableName = "equipment-store", ModuleId = ModText.Equipment_Store };
                store.WriteData(new List<EquipmentInstance> { new EquipmentInstance_RadioTransceiver(), new EquipmentInstance_RadiationProtection() });
                equipped.ModuleDataDic.Add(store.StableName, store);
                var equippedSnapshot = new Inventory_Equipment(); equippedSnapshot.EnsureSlotSchema();
                int offhand = equippedSnapshot.GetSlotIndex(EquipmentSlotCatalog.OffHand);
                equippedSnapshot.Data.itemSlots[offhand].itemData = equipped;
                equipment.ModSaveData.WriteData(new Mod_EquipmentSaveData
                    { EquipmentInventoryData = InventoryInstanceSnapshot.Capture(equippedSnapshot.Data) });
                equipment.ApplyNetworkAuthorityInventory();
                bool equippedRuntime = false;
                foreach (EquipmentInstance effect in equipment.EnumerateEquippedInstances())
                    equippedRuntime |= effect is EquipmentInstance_RadioTransceiver { CanConfigure: true };
                equippedSnapshot.Data.itemSlots[offhand].itemData = null;
                equipment.ModSaveData.WriteData(new Mod_EquipmentSaveData
                    { EquipmentInventoryData = InventoryInstanceSnapshot.Capture(equippedSnapshot.Data) });
                equipment.ApplyNetworkAuthorityInventory();
                bool removedRuntime = true;
                foreach (EquipmentInstance effect in equipment.EnumerateEquippedInstances())
                    if (effect is EquipmentInstance_RadioTransceiver) removedRuntime = false;
                return new JObject { ["bagChannel"] = bagBinding.State.Channel, ["bagEnergy"] = bagBinding.RemainingJoules,
                    ["hotbarChannel"] = hotbarBinding.State.Channel, ["hotbarEnergy"] = hotbarBinding.RemainingJoules,
                    ["equippedChannel"] = equippedBinding.State.Channel, ["equippedEnergy"] = equippedBinding.RemainingJoules,
                    ["otherChannel"] = otherBinding.State.Channel, ["clientChannel"] = clientBinding.State.Channel,
                    ["clientEnergy"] = clientBinding.RemainingJoules, ["first"] = first, ["duplicate"] = duplicate,
                    ["stale"] = stale, ["next"] = next, ["equippedRuntime"] = equippedRuntime,
                    ["removedRuntime"] = removedRuntime };
            }
            finally { Object.DestroyImmediate(root); }
        }
        #endregion
    }
}
#endif
