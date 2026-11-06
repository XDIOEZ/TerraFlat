using System;
using FlatWorld.Networking;

/// <summary>手持和穿戴共用权威操作，网络层只转发玩家意图。</summary>
public static class RadioTransceiverCommands
{
    #region 可操作设备与请求
    public static event Func<Item, int, bool, int, bool> RemoteConfigurationRequested;
    public static event Func<Item, int, string, bool> RemoteMessageRequested;

    public static bool TryFindActiveDevice(Item actor, int guid, out ItemData device)
    {
        device = null;
        if (actor == null) return false;
        Item held = actor.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar)?.CurentSelectItem;
        if (held?.itemData?.Guid == guid && held.InHand &&
            held.itemMods.GetMod_ByID<Mod_RadioTransceiver>(Mod_RadioTransceiver.ModuleId)?.IsRuntimeLoaded == true)
        { device = held.itemData; return true; }
        var equipment = actor.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (equipment?.IsRuntimeLoaded != true || equipment.EquipmentInventory?.Data?.itemSlots == null) return false;
        foreach (ItemSlot slot in equipment.EquipmentInventory.Data.itemSlots)
            if (slot?.itemData?.Guid == guid && FluidTankStorage.FindBinary(slot.itemData, Mod_RadioTransceiver.ModuleId) != null)
            { device = slot.itemData; return true; }
        return false;
    }

    public static bool SetConfiguration(Item actor, int guid, bool isOn, int channel)
    {
        if (!TryFindActiveDevice(actor, guid, out ItemData device)) return false;
        if (!GameNetwork.HasStateAuthority) return RemoteConfigurationRequested?.Invoke(actor, guid, isOn, channel) == true;
        return RadioTransceiverSystem.SetConfiguration(device, actor, isOn, channel);
    }

    public static bool TrySend(Item actor, int guid, string text)
    {
        if (!TryFindActiveDevice(actor, guid, out ItemData device) || string.IsNullOrWhiteSpace(text) ||
            text.Length > RadioSignalService.MaximumTextLength) return false;
        if (!GameNetwork.HasStateAuthority) return RemoteMessageRequested?.Invoke(actor, guid, text) == true;
        string sender = actor.itemData.GetActorProfileMetadata().DisplayName;
        if (string.IsNullOrWhiteSpace(sender)) sender = actor.itemData.GameName;
        return RadioTransceiverSystem.TrySend(device, actor, sender, text);
    }
    #endregion
}
