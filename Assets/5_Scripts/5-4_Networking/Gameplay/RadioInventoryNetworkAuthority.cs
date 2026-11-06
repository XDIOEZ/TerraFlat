using System;
using System.Collections.Generic;

namespace FlatWorld.Networking.Gameplay
{
    internal sealed class RadioRequestSequence
    {
        #region 单连接请求顺序
        private uint last;
        public bool TryAccept(uint request)
        {
            if (request == 0 || unchecked((int)(request - last)) <= 0) return false;
            last = request;
            return true;
        }
        #endregion
    }

    /// <summary>保留服务端对讲机状态，同时允许客户端快照携带正常的物品槽位置。</summary>
    internal static class RadioInventoryNetworkAuthority
    {
        #region 嵌套库存权威状态
        public static void PreserveServerRadioStates(Item actor, ItemData incoming)
        {
            Dictionary<int, byte[]> trusted = null;
            foreach (ItemData device in MachineInventoryCommands.OwnedRadioTransceivers(actor))
            {
                trusted ??= new Dictionary<int, byte[]>();
                RadioTransceiverSystem.Flush(device);
                trusted[device.Guid] = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId).BitData;
            }
            MergeTrustedIntoSnapshot(actor, incoming, trusted);
        }

        private static void MergeTrustedIntoSnapshot(Item actor, ItemData incoming, Dictionary<int, byte[]> trusted)
        {
            if (trusted == null || trusted.Count == 0 || incoming?.ModuleDataDic == null) return;
            foreach (Module module in actor.Mods.Values)
            {
                if (module == null || !incoming.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state)) continue;
                if (state is Inventory_ModuleData inventories)
                {
                    if (inventories.Data != null)
                        foreach (Inventory_Data inventory in inventories.Data.Values) PreserveInventory(inventory, trusted);
                }
                else if (module is Mod_HotBar && state is Ex_ModData_MemoryPackable hotbar)
                {
                    InventoryHotBarSaveState snapshot = hotbar.GetData<InventoryHotBarSaveState>();
                    if (snapshot?.Inventory == null) continue;
                    Inventory_Data inventory = snapshot.Inventory.CreateColdData();
                    if (PreserveInventory(inventory, trusted))
                    { snapshot.Inventory = InventoryInstanceSnapshot.Capture(inventory); hotbar.WriteData(snapshot); }
                }
                else if (module is Mod_Equipment && state is Ex_ModData_MemoryPackable equipment)
                {
                    Mod_EquipmentSaveData snapshot = equipment.GetData<Mod_EquipmentSaveData>();
                    if (snapshot?.EquipmentInventoryData == null) continue;
                    Inventory_Data inventory = snapshot.EquipmentInventoryData.CreateColdData();
                    if (PreserveInventory(inventory, trusted))
                    { snapshot.EquipmentInventoryData = InventoryInstanceSnapshot.Capture(inventory); equipment.WriteData(snapshot); }
                }
            }
        }

        public static void ApplyAuthoritativeRadioStates(Item actor, ItemData authoritative)
        {
            if (actor == null || authoritative?.ModuleDataDic == null) return;
            using (var devices = MachineInventoryCommands.OwnedRadioTransceivers(actor).GetEnumerator())
                if (!devices.MoveNext()) return;
            var trusted = new Dictionary<int, byte[]>();
            foreach (Module module in actor.Mods.Values)
            {
                if (module == null || !authoritative.ModuleDataDic.TryGetValue(module.StableName, out ModuleData state)) continue;
                if (state is Inventory_ModuleData inventories && inventories.Data != null)
                {
                    foreach (Inventory_Data inventory in inventories.Data.Values) CollectInventory(inventory, trusted);
                }
                else if (module is Mod_HotBar && state is Ex_ModData_MemoryPackable hotbar)
                    CollectInventory(hotbar.GetData<InventoryHotBarSaveState>()?.Inventory?.CreateColdData(), trusted);
                else if (module is Mod_Equipment && state is Ex_ModData_MemoryPackable equipment)
                    CollectInventory(equipment.GetData<Mod_EquipmentSaveData>()?.EquipmentInventoryData?.CreateColdData(), trusted);
            }
            foreach (ItemData device in MachineInventoryCommands.OwnedRadioTransceivers(actor))
            {
                if (!trusted.TryGetValue(device.Guid, out byte[] payload)) continue;
                Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
                if (binary != null && !SamePayload(binary.BitData, payload))
                {
                    binary.BitData = payload;
                    RadioTransceiverSystem.RefreshCachedBinding(device, actor);
                }
            }
        }

        private static void CollectInventory(Inventory_Data inventory, Dictionary<int, byte[]> trusted)
        {
            if (inventory?.itemSlots == null) return;
            foreach (ItemSlot slot in inventory.itemSlots)
            {
                ItemData device = slot?.itemData;
                Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
                if (binary != null) trusted[device.Guid] = binary.BitData;
            }
        }

        private static bool PreserveInventory(Inventory_Data inventory, Dictionary<int, byte[]> trusted)
        {
            bool changed = false;
            if (inventory?.itemSlots == null) return false;
            foreach (ItemSlot slot in inventory.itemSlots)
            {
                ItemData device = slot?.itemData;
                if (device == null || !trusted.TryGetValue(device.Guid, out byte[] payload)) continue;
                Ex_ModData_MemoryPackable binary = FluidTankStorage.FindBinary(device, Mod_RadioTransceiver.ModuleId);
                if (binary == null || SamePayload(binary.BitData, payload)) continue;
                binary.BitData = payload;
                changed = true;
            }
            return changed;
        }
        private static bool SamePayload(byte[] left, byte[] right)
            => ReferenceEquals(left, right) || left != null && right != null && left.AsSpan().SequenceEqual(right);
        #endregion
    }
}
