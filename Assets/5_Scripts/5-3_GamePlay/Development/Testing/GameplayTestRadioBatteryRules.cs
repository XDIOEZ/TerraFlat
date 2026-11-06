using System.Collections.Generic;
using FlatWorld.Gameplay.Events;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>只构造隔离冷物品和真实库存槽，Lua 独立判断电量与事务结果。</summary>
public static class GameplayTestRadioBatteryRules
{
    #region 电池槽规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register()
    {
        GameplayTestSystemProbe.Register("radio", "battery_inventory", CaptureBatteryInventory);
        GameplayTestSystemProbe.Register("radio", "scope_recreation", CaptureRadioServiceRecreation);
        GameplayTestSystemProbe.Register("radio", "broadcast_definition", CaptureRadioBroadcastDefinition);
    }

    private static JObject CaptureRadioBroadcastDefinition(JObject args)
    {
        var handler = new RadioBroadcastGameEventAction();
        bool positive = handler.Validate(new JObject { ["stationId"] = 4, ["messageId"] = "distress" }, out _);
        bool negative = handler.Validate(new JObject { ["stationId"] = -4, ["messageId"] = "warning" }, out _);
        bool zero = handler.Validate(new JObject { ["stationId"] = 0, ["messageId"] = "distress" }, out _);
        bool unknown = handler.Validate(new JObject { ["stationId"] = 4, ["messageId"] = "missing" }, out _);
        return new JObject { ["positive"] = positive, ["negative"] = negative, ["zero"] = zero, ["unknown"] = unknown };
    }

    private static JObject CaptureRadioServiceRecreation(JObject args)
    {
        var oldService = new RadioSignalService("probe:world", default);
        oldService.Register(10, 1, () => Vector2.zero, RadioEndpointCapabilities.TransmitCircuit);
        oldService.SendCircuitBit(10, true);
        oldService.TryGetLastCircuitFrame(10, out RadioCircuitFrame oldFrame);
        oldService.Clear();
        var newService = new RadioSignalService("probe:world", default);
        RadioCircuitState received = default;
        newService.Register(10, 1, () => Vector2.zero, RadioEndpointCapabilities.TransmitCircuit);
        newService.Register(20, 1, () => Vector2.zero, RadioEndpointCapabilities.ReceiveCircuit, state => received = state);
        newService.SendCircuitBit(10, false);
        newService.TryGetLastCircuitFrame(10, out RadioCircuitFrame newFrame);
        bool oldAccepted = newService.ReceiveCircuitFrame(oldFrame);
        newService.Advance(1f);
        return new JObject { ["epochsDifferent"] = oldFrame.Epoch != newFrame.Epoch,
            ["oldAccepted"] = oldAccepted, ["hasSource"] = received.HasSource, ["value"] = received.Value };
    }

    private static ItemData CreateItem(string name, int guid, string moduleId)
    {
        var item = new Data_GeneralItem { IDName = name, Guid = guid, Stack = new ItemStack { Amount = 1, Stackable = false } };
        item.ModuleDataDic.Add("probe", new Ex_ModData_MemoryPackable { StableName = "probe", ModuleId = moduleId });
        return item;
    }

    private static JObject CaptureBatteryInventory(JObject args)
    {
        ItemData device = CreateItem("probe:radio", 72001, Mod_RadioTransceiver.ModuleId);
        ItemData battery = CreateItem("probe:other-battery-definition", 72002, Mod_RadioBattery.ModuleId);
        RadioBatteryStorage.TryGetRemainingJoules(battery, out double full);
        RadioTransceiverBinding binding = RadioTransceiverSystem.BindRuntime(device, null);
        var hand = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = battery } }, "probe-hand");
        bool inserted = hand.DropItemSlotTo(hand.itemSlots[0], binding.Inventory.Data, binding.Inventory.Data.itemSlots[0]);
        bool consumedHalf = RadioBatteryStorage.TryConsumeExactJoules(binding.Battery, 1800d);
        RadioTransceiverSystem.Flush(device);
        ItemData restoredDevice = ItemSnapshotSerialization.Capture(device).CreateColdData();
        RadioTransceiverBinding restored = RadioTransceiverSystem.BindRuntime(restoredDevice, null);
        double afterRestore = restored.RemainingJoules;
        bool removed = restored.Inventory.Data.DropItemSlotTo(restored.Inventory.Data.itemSlots[0], hand, hand.itemSlots[0]);
        RadioBatteryStorage.TryGetRemainingJoules(hand.itemSlots[0].itemData, out double afterRemoval);
        bool reinserted = hand.DropItemSlotTo(hand.itemSlots[0], restored.Inventory.Data, restored.Inventory.Data.itemSlots[0]);
        bool consumedRest = RadioBatteryStorage.TryConsumeExactJoules(restored.Battery, 1800d);
        bool rejectedOverdraw = !RadioBatteryStorage.TryConsumeExactJoules(restored.Battery, 1d);
        RadioTransceiverSystem.Flush(restoredDevice);
        ItemData emptyDevice = ItemSnapshotSerialization.Capture(restoredDevice).CreateColdData();
        RadioTransceiverBinding empty = RadioTransceiverSystem.BindRuntime(emptyDevice, null);
        bool emptyRetained = empty.Battery != null;
        double emptyEnergy = empty.RemainingJoules;
        var wrong = new Data_GeneralItem { IDName = "probe:not-a-battery", Guid = 72003,
            Stack = new ItemStack { Amount = 1, Stackable = false } };
        var wrongSource = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = wrong } }, "probe-wrong");
        bool wrongRejected = !wrongSource.DropItemSlotTo(wrongSource.itemSlots[0], empty.Inventory.Data, empty.Inventory.Data.itemSlots[0]);
        RadioTransceiverSystem.Unbind(device, null);
        RadioTransceiverSystem.Unbind(restoredDevice, null);
        RadioTransceiverSystem.Unbind(emptyDevice, null);
        return new JObject { ["full"] = full, ["inserted"] = inserted, ["halfConsumed"] = consumedHalf,
            ["halfAfterRestore"] = afterRestore, ["removed"] = removed, ["halfAfterRemoval"] = afterRemoval,
            ["reinserted"] = reinserted, ["restConsumed"] = consumedRest, ["overdrawRejected"] = rejectedOverdraw,
            ["emptyRetained"] = emptyRetained, ["emptyAfterRestore"] = emptyEnergy, ["wrongRejected"] = wrongRejected };
    }
    #endregion
}
