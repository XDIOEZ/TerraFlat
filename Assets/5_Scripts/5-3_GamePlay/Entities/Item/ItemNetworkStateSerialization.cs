// AI-Context: Item/Module 网络快照与游戏层网络桥；游戏模块不能依赖 Mirror，建造/拾取请求通过这里交给网络协调器。
using System;
using System.Collections.Generic;
using System.Reflection;
using MemoryPack;
using UnityEngine;

/// <summary>
/// Item/Module 的通用网络快照。网络层只传 byte[]，避免与具体 Module 类型耦合。
/// </summary>
public static class ItemNetworkStateSerialization
{
    private const int MaxSnapshotBytes = 512 * 1024;
    private static readonly Dictionary<Type, FieldInfo[]> SyncFieldsByType = new();

    public static event Action<Item> RuntimeStateChanged;
    public static Func<bool> ShouldDeferLocalDestruction;
    public static Func<ItemPicker, Item, bool> TryBeginNetworkPickup;
    public static Func<Mod_Building, Vector3, bool> TryBeginNetworkBuilding;
    public static Func<Mod_Building, bool> TryBeginNetworkBuildingDismantle;

    public static void NotifyRuntimeStateChanged(Item item)
    {
        if (item != null)
            RuntimeStateChanged?.Invoke(item);
    }

    public static bool DeferLocalDestruction()
        => ShouldDeferLocalDestruction?.Invoke() == true;

    public static bool BeginNetworkPickup(ItemPicker picker, Item worldItem)
        => TryBeginNetworkPickup?.Invoke(picker, worldItem) == true;

    public static bool BeginNetworkBuilding(Mod_Building building, Vector3 position)
        => TryBeginNetworkBuilding?.Invoke(building, position) == true;

    public static bool BeginNetworkBuildingDismantle(Mod_Building building)
        => TryBeginNetworkBuildingDismantle?.Invoke(building) == true;

    public static byte[] Capture(Item item, bool ignoreTransform)
    {
        if (item == null || item.itemData == null)
            return Array.Empty<byte>();

        item.ModuleSave();
        if (!ignoreTransform)
            return MemoryPackSerializer.Serialize<ItemData>(item.itemData);

        // 玩家移动由独立运动通道同步。临时清空位姿后只序列化一次，避免移动期间
        // 每次状态检查都“序列化 -> 反序列化克隆 -> 再序列化”造成 GC 尖峰。
        ItemTransform transformState = item.itemData.transform;
        if (transformState == null)
            return MemoryPackSerializer.Serialize<ItemData>(item.itemData);

        Vector3 position = transformState.position;
        Quaternion rotation = transformState.rotation;
        Vector3 scale = transformState.scale;
        try
        {
            transformState.position = Vector3.zero;
            transformState.rotation = Quaternion.identity;
            transformState.scale = Vector3.one;
            return MemoryPackSerializer.Serialize<ItemData>(item.itemData);
        }
        finally
        {
            transformState.position = position;
            transformState.rotation = rotation;
            transformState.scale = scale;
        }
    }

    public static bool IsValidPayload(byte[] payload)
        => payload != null && payload.Length > 0 && payload.Length <= MaxSnapshotBytes;

    public static bool TryReadIdentity(byte[] payload, out int guid, out string itemId)
    {
        guid = 0;
        itemId = null;
        if (!TryDeserialize(payload, out ItemData state))
            return false;

        guid = state.Guid;
        itemId = state.IDName;
        return true;
    }

    public static bool TryDeserializeItemData(byte[] payload, out ItemData itemData)
        => TryDeserialize(payload, out itemData);

    public static bool TrySerializeItemData(ItemData itemData, out byte[] payload)
    {
        payload = null;
        if (itemData == null)
            return false;

        try
        {
            payload = MemoryPackSerializer.Serialize<ItemData>(itemData);
            return IsValidPayload(payload);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[物品快照] 序列化失败：{exception.Message}");
            payload = null;
            return false;
        }
    }

    public static bool Apply(Item target, byte[] payload, bool reloadRuntimeModules, bool requireMatchingGuid)
    {
        if (!TryMergeSnapshot(target, payload, requireMatchingGuid, out ItemData current,
                out Dictionary<string, ModuleData> previousModuleStates))
            return false;

        if (!reloadRuntimeModules || target.Mods == null || target.Mods.Count == 0)
            return true;

        List<Module> runtimeModules = new List<Module>(target.Mods.Values);
        for (int i = 0; i < runtimeModules.Count; i++)
        {
            Module module = runtimeModules[i];
            if (module == null || module._Data == null)
                continue;

            ModuleData state = FindModuleState(current.ModuleDataDic, module._Data.Name, module._Data.ID);
            if (state == null)
                continue;

            ModuleData previousState = FindModuleState(previousModuleStates, module._Data.Name, module._Data.ID);
            if (ModuleStatesEqual(previousState, state))
                continue;

            module.ModuleInit(target, state, current);
            module.ApplyNetworkData(state);
        }

        target.OnUIRefresh?.Invoke();
        return true;
    }

    /// <summary>
    /// 将所有模块数据绑定到远程副本，但不直接调用 Module.Load。
    /// 需要刷新远程表现的模块由 runtimeApplier 按白名单处理。
    /// </summary>
    public static bool ApplyRemoteReplica(
        Item target,
        byte[] payload,
        Action<Module, ModuleData> runtimeApplier)
    {
        if (!TryMergeSnapshot(target, payload, false, out ItemData current, out _))
            return false;

        if (target.Mods == null || target.Mods.Count == 0)
            return true;

        List<Module> runtimeModules = new List<Module>(target.Mods.Values);
        for (int i = 0; i < runtimeModules.Count; i++)
        {
            Module module = runtimeModules[i];
            if (module == null || module._Data == null)
                continue;

            ModuleData state = FindModuleState(current.ModuleDataDic, module._Data.Name, module._Data.ID);
            if (state == null)
                continue;

            module.ModuleInit(target, state, current);
            runtimeApplier?.Invoke(module, state);
        }

        return true;
    }

    public static uint CalculateHash(byte[] payload)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (payload == null)
                return hash;

            for (int i = 0; i < payload.Length; i++)
                hash = (hash ^ payload[i]) * 16777619u;

            return hash;
        }
    }

    public static uint CalculateModuleHash(ModuleData state)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (state == null)
                return hash;

            hash = AppendHash(hash, state.GetType().FullName);
            hash = AppendHash(hash, state.ID);
            hash = AppendHash(hash, state.Name);
            hash = (hash ^ (state.isRunning ? (byte)1 : (byte)0)) * 16777619u;

            if (state is Ex_ModData_MemoryPackable binaryState)
                return AppendHash(hash, binaryState.BitData);
            if (state is Ex_ModData jsonState)
                return AppendHash(hash, jsonState.BitData);

            return hash;
        }
    }

    private static bool TryDeserialize(byte[] payload, out ItemData state)
    {
        state = null;
        if (!IsValidPayload(payload))
            return false;

        try
        {
            state = MemoryPackSerializer.Deserialize<ItemData>(payload);
            return state != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[联机物品] 快照反序列化失败：{exception.Message}");
            return false;
        }
    }

    private static bool TryMergeSnapshot(
        Item target,
        byte[] payload,
        bool requireMatchingGuid,
        out ItemData current,
        out Dictionary<string, ModuleData> previousModuleStates)
    {
        current = target?.itemData;
        previousModuleStates = null;
        if (current == null || !TryDeserialize(payload, out ItemData incoming))
            return false;

        if (incoming.GetType() != current.GetType() ||
            !string.Equals(incoming.IDName, current.IDName, StringComparison.Ordinal) ||
            (requireMatchingGuid && incoming.Guid != current.Guid))
        {
            return false;
        }

        previousModuleStates = current.ModuleDataDic;
        ItemTransform preservedTransform = current.transform;
        int preservedGuid = current.Guid;
        CopySerializableFields(incoming, current);
        current.Guid = preservedGuid;
        current.transform = preservedTransform;

        if (current.ModuleDataDic == null)
            current.ModuleDataDic = new Dictionary<string, ModuleData>();

        return true;
    }

    private static uint AppendHash(uint hash, string value)
    {
        unchecked
        {
            if (string.IsNullOrEmpty(value))
                return (hash ^ 0u) * 16777619u;

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                hash = (hash ^ (byte)character) * 16777619u;
                hash = (hash ^ (byte)(character >> 8)) * 16777619u;
            }

            return hash;
        }
    }

    private static uint AppendHash(uint hash, byte[] value)
    {
        unchecked
        {
            if (value == null)
                return (hash ^ 0u) * 16777619u;

            for (int i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * 16777619u;

            return hash;
        }
    }

    private static void CopySerializableFields(ItemData source, ItemData destination)
    {
        Type type = source.GetType();
        if (!SyncFieldsByType.TryGetValue(type, out FieldInfo[] fields))
        {
            fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            SyncFieldsByType[type] = fields;
        }

        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field.IsInitOnly || field.Name == nameof(ItemData.Guid) || field.Name == nameof(ItemData.transform))
                continue;

            field.SetValue(destination, field.GetValue(source));
        }
    }

    private static ModuleData FindModuleState(
        Dictionary<string, ModuleData> states,
        string moduleName,
        string moduleId)
    {
        if (states == null)
            return null;

        if (!string.IsNullOrEmpty(moduleName) && states.TryGetValue(moduleName, out ModuleData exact))
            return exact;

        foreach (ModuleData state in states.Values)
        {
            if (state != null && string.Equals(state.ID, moduleId, StringComparison.Ordinal))
                return state;
        }

        return null;
    }

    private static bool ModuleStatesEqual(ModuleData left, ModuleData right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.GetType() != right.GetType() ||
            left.ID != right.ID || left.Name != right.Name || left.isRunning != right.isRunning)
        {
            return false;
        }

        if (left is Ex_ModData leftJson && right is Ex_ModData rightJson)
            return string.Equals(leftJson.BitData, rightJson.BitData, StringComparison.Ordinal);

        if (left is Ex_ModData_MemoryPackable leftBinary && right is Ex_ModData_MemoryPackable rightBinary)
        {
            byte[] a = leftBinary.BitData;
            byte[] b = rightBinary.BitData;
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        return false;
    }
}
