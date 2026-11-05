using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using MemoryPack;

/// <summary>冷角色的数值直接读写能力模块，创建和迁移不另建角色属性副本。</summary>
public static class ActorModuleState
{
    #region 冷模块状态访问
    private static ModuleData Find(ItemData data, string id)
        => data?.ModuleDataDic?.Values.FirstOrDefault(value => value?.ModuleId == id);
    private static JObject Read(Ex_ModData data)
        => string.IsNullOrEmpty(data?.BitData) ? new JObject() : JObject.Parse(data.BitData);
    private static Ex_ModData FindJson(ItemData data, string id) => Find(data, id) as Ex_ModData;
    private static Inventory_Data Bag(ItemData data)
        => (Find(data, ModText.Bag) as Inventory_ModuleData)?.Data?.Values.FirstOrDefault();

    public static float GetCameraPov(ItemData data) => Read(FindJson(data, ModText.Camera)).Value<float?>("PovValue") ?? 10f;
    public static void SetCameraPov(ItemData data, float value)
    {
        var module = FindJson(data, ModText.Camera);
        if (module == null) return;
        var state = Read(module); state["PovValue"] = value; module.BitData = state.ToString(Newtonsoft.Json.Formatting.None);
    }
    public static float GetTargetPerceptionMultiplier(ItemData data)
        => Read(FindJson(data, Mod_TargetPerceptionSignature.ModuleId)).Value<float?>("Multiplier") ?? 1f;
    public static void SetTargetPerceptionMultiplier(ItemData data, float value)
    {
        var module = FindJson(data, Mod_TargetPerceptionSignature.ModuleId);
        if (module == null) return;
        module.BitData = new JObject { ["Multiplier"] = value }.ToString(Newtonsoft.Json.Formatting.None);
    }
    public static float GetMaxCarryWeight(ItemData data) => Bag(data)?.MaxCarryWeight ?? Inventory_Data.DefaultPlayerBagMaxWeight;
    public static float GetMaxCarryVolume(ItemData data) => Bag(data)?.MaxCarryVolume ?? Inventory_Data.DefaultPlayerBagMaxVolume;
    public static void SetMaxCarryWeight(ItemData data, float value) => Bag(data)?.SetCarryCapacity(value, GetMaxCarryVolume(data));
    public static void SetMaxCarryVolume(ItemData data, float value) => Bag(data)?.SetCarryCapacity(GetMaxCarryWeight(data), value);

    public static void ApplyLegacyModuleState(ItemData data)
    {
        var attributes = LegacyActorModuleState.FindPendingModule(data, LegacyActorModuleState.AttributesKey) as Ex_ModData;
        if (attributes != null)
        {
            var state = attributes.GetData<LegacyActorModuleState>();
            // 原有模块载荷优先，旧冗余血量、移速和体力不参与迁移。
            if (FindJson(data, ModText.Camera) is { } camera && string.IsNullOrEmpty(camera.BitData)) SetCameraPov(data, state.CameraPov);
            if (FindJson(data, Mod_TargetPerceptionSignature.ModuleId) is { } perception && string.IsNullOrEmpty(perception.BitData))
                SetTargetPerceptionMultiplier(data, state.TargetPerceptionMultiplier);
            if (Bag(data) is { } bag && !bag.HasCarryCapacity)
                bag.ConfigurePlayerBagCapacity(state.MaxCarryWeight, state.MaxCarryVolume);
            if (FindJson(data, Mod_TargetPerceptionSignature.ModuleId) != null && Bag(data) != null)
                LegacyActorModuleState.RemovePendingModule(data, LegacyActorModuleState.AttributesKey);
        }
        var legacyStorage = LegacyActorModuleState.FindPendingModule(data, LegacyActorModuleState.InventoriesKey) as Inventory_ModuleData;
        var primary = Find(data, ModText.Bag) as Inventory_ModuleData;
        if (legacyStorage?.Data == null || primary?.Data == null) return;
        var realStorage = data.ModuleDataDic.Values.OfType<Inventory_ModuleData>()
            .Where(module => module != legacyStorage).ToArray();
        var existing = realStorage.SelectMany(module => module.Data.Values)
            .SelectMany(inventory => inventory.itemSlots).Where(slot => slot?.itemData != null && slot.itemData.Guid != 0)
            .Select(slot => slot.itemData.Guid).ToHashSet();
        var hotbar = Find(data, ModText.Hotbar) as Ex_ModData_MemoryPackable;
        Inventory_Data hotbarInventory = hotbar?.BitData?.Length > 0
            ? MemoryPackSerializer.Deserialize<InventoryHotBarSaveState>(hotbar.BitData)?.Inventory?.CreateColdData() : null;
        if (hotbarInventory != null)
            foreach (var slot in hotbarInventory.itemSlots)
                if (slot?.itemData != null && slot.itemData.Guid != 0) existing.Add(slot.itemData.Guid);
        foreach (var pair in legacyStorage.Data)
        {
            foreach (var slot in pair.Value.itemSlots)
                if (slot?.itemData != null && slot.itemData.Guid != 0 && !existing.Add(slot.itemData.Guid)) slot.itemData = null;
            var matching = realStorage.FirstOrDefault(module => module.Data.ContainsKey(pair.Key));
            if (hotbar != null && (pair.Key == hotbar.Name || pair.Key == hotbar.ModuleId || pair.Key == hotbarInventory?.Name) &&
                (hotbarInventory == null || hotbarInventory.itemSlots.All(slot => slot?.itemData == null)))
            {
                hotbarInventory = pair.Value;
                hotbar.BitData = MemoryPackSerializer.Serialize(new InventoryHotBarSaveState
                { Version = InventoryHotBarSaveState.CurrentVersion, Inventory = InventoryInstanceSnapshot.Capture(hotbarInventory) });
            }
            else if (matching != null && matching.Data[pair.Key].itemSlots.All(slot => slot?.itemData == null))
                matching.Data[pair.Key] = pair.Value;
            else
            {
                string key = "legacy:" + pair.Key;
                while (primary.Data.ContainsKey(key)) key += ":legacy";
                primary.Data[key] = pair.Value;
            }
        }
        LegacyActorModuleState.RemovePendingModule(data, LegacyActorModuleState.InventoriesKey);
    }
    #endregion
}
