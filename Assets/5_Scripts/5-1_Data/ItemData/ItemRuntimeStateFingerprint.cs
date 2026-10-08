using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>直接读取内建实例值判断状态是否改变，未覆盖的类型交回完整快照管线。</summary>
public static class ItemRuntimeStateFingerprint
{
    #region 物品状态指纹

    private const int MaxNestedItems = 64;

    public static bool TryCalculate(ItemData data, bool includeTransform, bool publicState,
        HashSet<ItemData> visiting, out ulong signature)
    {
        signature = 0;
        if (data == null || visiting == null || visiting.Count >= MaxNestedItems ||
            (data.GetType() != typeof(Data_GeneralItem) && data.GetType() != typeof(BlockData)) ||
            data.PreservedModuleStates?.Length > 0 || !visiting.Add(data)) return false;
        try
        {
            var hash = new ItemRuntimeFingerprintBuilder();
            hash.Add(data.GetType() == typeof(Data_GeneralItem) ? 0 : 3);
            hash.Add(data.IDName);
            hash.Add(data.Guid);
            if (data.Stack != null && data.Stack.GetType() != typeof(ItemStack)) return false;
            hash.Add(data.Stack?.Amount ?? 1f);
            hash.Add(data.Stack?.CanBePickedUp ?? true);
            hash.Add(data.MaxDurability > 0f ? Mathf.Clamp01(data.Durability / data.MaxDurability) : 1f);
            float multiplier = data.CraftedDurabilityMultiplier;
            hash.Add(!float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f ? multiplier : 1f);
            hash.Add(data.ItemSpecialData);
            hash.Add(data.inHand);
            hash.Add(data.FactionId);
            bool hasTransform = includeTransform && data.transform != null;
            hash.Add(hasTransform);
            if (hasTransform)
            {
                if (data.transform.GetType() != typeof(ItemTransform)) return false;
                hash.Add(data.transform.position);
                hash.Add(data.transform.rotation);
                hash.Add(data.transform.scale);
            }
            if (data.MatterState != null && data.MatterState.GetType() != typeof(ItemMatterState)) return false;
            hash.Add(data.MatterState?.Initialized ?? false);
            hash.Add(data.MatterState?.TemperatureCelsius ?? 20f);
            hash.Add(data.MatterState?.Moisture ?? 0f);
            hash.Add(data.MatterState?.IsBurning ?? false);
            hash.Add(data.MatterState?.CombustionElapsedSeconds ?? 0f);

            // 标签差量依赖当前配置基线；直接读取两侧列表，不建立临时集合。
            AppendStrings(ref hash, data.Tags);
            AppendStrings(ref hash, data.SharedConfiguration?.Tags);
            AppendStrings(ref hash, data.PreservedAddedTags);
            AppendStrings(ref hash, data.PreservedRemovedTags);
            hash.Add(data.ModuleDataDic?.Count ?? 0);
            if (data.ModuleDataDic != null)
                foreach (KeyValuePair<string, ModuleData> pair in data.ModuleDataDic)
                {
                    if (pair.Value == null) continue;
                    if (pair.Key != pair.Value.StableName ||
                        !TryCalculateModule(pair.Value, publicState, visiting, out ulong moduleSignature)) return false;
                    hash.Add(moduleSignature);
                }
            if (data is Data_GeneralItem general) hash.Add(general.code);
            signature = hash.Value;
            return true;
        }
        finally { visiting.Remove(data); }
    }

    #endregion

    #region 模块状态指纹

    public static bool TryCalculateModule(ModuleData data, bool publicState,
        HashSet<ItemData> visiting, out ulong signature)
    {
        signature = 0;
        if (data == null || visiting == null || string.IsNullOrWhiteSpace(data.StableName) ||
            string.IsNullOrWhiteSpace(data.ModuleId)) return false;
        var hash = new ItemRuntimeFingerprintBuilder();
        hash.Add(data.StableName);
        hash.Add(data.ModuleId);
        Type type = data.GetType();
        if (type == typeof(Ex_ModData))
        {
            hash.Add(1);
            hash.Add(((Ex_ModData)data).BitData);
        }
        else if (type == typeof(Ex_ModData_MemoryPackable))
        {
            hash.Add(2);
            // 二进制载荷可原位改写，必须逐字节读取内容，不能依赖数组引用。
            AppendBytes(ref hash, ((Ex_ModData_MemoryPackable)data).BitData, nullAsEmpty: true);
        }
        else if (type == typeof(CollectableModuleData))
        {
            hash.Add(3);
            var state = (CollectableModuleData)data;
            hash.Add(state.CurrentStock);
            hash.Add(state.IsInitialized);
        }
        else if (type == typeof(Inventory_ModuleData))
        {
            hash.Add(4);
            var state = (Inventory_ModuleData)data;
            hash.Add(state.PanleRectPosition);
            hash.Add(state.BasePanelIsOpen);
            hash.Add(publicState);
            if (!publicState && !AppendInventories(ref hash, state.Data, visiting)) return false;
        }
        else if (type == typeof(ModData_FoodData))
        {
            hash.Add(5);
            var state = (ModData_FoodData)data;
            if (state.FoodData != null && state.FoodData.GetType() != typeof(Food)) return false;
            hash.Add(state.FoodData?.PanelPosition ?? Vector2.zero);
            Nutrition nutrition = state.FoodData?.nutrition;
            hash.Add(nutrition != null);
            if (nutrition != null)
            {
                if (nutrition.GetType() != typeof(Nutrition)) return false;
                hash.Add(nutrition.Carbohydrates);
                hash.Add(nutrition.Fat);
                hash.Add(nutrition.Protein);
                hash.Add(nutrition.Water);
                hash.Add(nutrition.Vitamins);
                hash.Add(nutrition.Max_Fat);
            }
            hash.Add(state.MechanicStates?.Count ?? -1);
            if (state.MechanicStates != null)
                foreach (FoodMechanicStateData mechanic in state.MechanicStates)
                {
                    hash.Add(mechanic != null);
                    if (mechanic == null) continue;
                    if (mechanic.GetType() != typeof(FoodMechanicStateData)) return false;
                    mechanic.AppendRuntimeFingerprint(ref hash);
                }
        }
        else
        {
            if (!ModuleInstanceStateCodecs.TryGet(type, out var codec) ||
                codec is not IModuleInstanceStateFingerprintProvider provider ||
                !provider.TryCalculateStateFingerprint(data, publicState, out ulong extensionSignature)) return false;
            hash.Add(6);
            hash.Add(codec.Id);
            hash.Add(ModuleInstanceStateCodecs.Revision);
            hash.Add(extensionSignature);
        }
        signature = hash.Value;
        return true;
    }

    #endregion

    #region 内嵌库存与列表

    private static bool AppendInventories(ref ItemRuntimeFingerprintBuilder hash,
        Dictionary<string, Inventory_Data> inventories, HashSet<ItemData> visiting)
    {
        hash.Add(inventories?.Count ?? 0);
        if (inventories == null) return true;
        foreach (KeyValuePair<string, Inventory_Data> pair in inventories)
        {
            hash.Add(pair.Key);
            Inventory_Data inventory = pair.Value;
            hash.Add(inventory != null);
            if (inventory == null) continue;
            if (inventory.GetType() != typeof(Inventory_Data)) return false;
            hash.Add(inventory.Name);
            hash.Add(inventory.Index);
            hash.Add(inventory.IsInjected);
            hash.Add(inventory.PanelPosition);
            hash.Add(inventory.PanelIsOpen);
            hash.Add(inventory.IsDepositBlocked);
            hash.Add(inventory.itemSlots?.Count ?? 0);
            if (inventory.itemSlots == null) continue;
            foreach (ItemSlot slot in inventory.itemSlots)
            {
                if (slot != null && slot.GetType() != typeof(ItemSlot)) return false;
                ItemData item = slot?.itemData;
                hash.Add(item != null);
                if (item == null) continue;
                // 库存快照中的内嵌物品始终包含自己的位姿与私有状态。
                if (!TryCalculate(item, true, false, visiting, out ulong itemSignature)) return false;
                hash.Add(itemSignature);
            }
        }
        return true;
    }

    private static void AppendStrings(ref ItemRuntimeFingerprintBuilder hash, IReadOnlyList<string> values)
    {
        hash.Add(values?.Count ?? -1);
        if (values != null)
            for (int i = 0; i < values.Count; i++) hash.Add(values[i]);
    }

    internal static void AppendBytes(ref ItemRuntimeFingerprintBuilder hash, byte[] bytes, bool nullAsEmpty = false)
    {
        hash.Add(bytes?.Length ?? (nullAsEmpty ? 0 : -1));
        if (bytes != null)
            for (int i = 0; i < bytes.Length; i++) hash.Add(bytes[i]);
    }

    #endregion
}

internal struct ItemRuntimeFingerprintBuilder
{
    #region FNV64 值读取

    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private ulong value;
    private bool initialized;

    public ulong Value => initialized ? value : OffsetBasis;

    public void Add(byte current)
    {
        if (!initialized) { value = OffsetBasis; initialized = true; }
        value = unchecked((value ^ current) * Prime);
    }

    public void Add(bool current) => Add(current ? (byte)1 : (byte)0);
    public void Add(int current) => Add(unchecked((uint)current));
    public void Add(float current) => Add(unchecked((uint)BitConverter.SingleToInt32Bits(current)));

    public void Add(uint current)
    {
        unchecked { Add((byte)current); Add((byte)(current >> 8)); Add((byte)(current >> 16)); Add((byte)(current >> 24)); }
    }

    public void Add(ulong current)
    {
        Add(unchecked((uint)current));
        Add(unchecked((uint)(current >> 32)));
    }

    public void Add(string current)
    {
        Add(current?.Length ?? -1);
        if (current == null) return;
        for (int i = 0; i < current.Length; i++) { Add(unchecked((byte)current[i])); Add((byte)(current[i] >> 8)); }
    }

    public void Add(Vector2 current) { Add(current.x); Add(current.y); }
    public void Add(Vector3 current) { Add(current.x); Add(current.y); Add(current.z); }
    public void Add(Quaternion current) { Add(current.x); Add(current.y); Add(current.z); Add(current.w); }

    #endregion
}

public partial class FoodMechanicStateData
{
    #region 纯内存状态指纹

    internal void AppendRuntimeFingerprint(ref ItemRuntimeFingerprintBuilder hash)
    {
        hash.Add(StateKey);
        hash.Add(Data?.Count ?? -1);
        if (Data != null)
            foreach (KeyValuePair<string, string> pair in Data) { hash.Add(pair.Key); hash.Add(pair.Value); }
        ItemRuntimeStateFingerprint.AppendBytes(ref hash, Payload);
        // 私有浮点缓存可能比字符串负载更新，不为了比较状态而修改活跃对象。
        hash.Add(runtimeFloats?.Count ?? 0);
        if (runtimeFloats != null)
            foreach (KeyValuePair<string, float> pair in runtimeFloats) { hash.Add(pair.Key); hash.Add(pair.Value); }
    }

    #endregion
}
