// AI-Context: Item/Module 网络快照与游戏层网络桥；游戏模块不能依赖 Mirror，建造/拾取请求通过这里交给网络协调器。
using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

/// <summary>
/// Item/Module 的通用网络快照。网络层只传 byte[]，避免与具体 Module 类型耦合。
/// </summary>
public static class ItemNetworkStateSerialization
{
    #region Item 网络状态桥与快照
    private const int MaxSnapshotBytes = 512 * 1024;
    private static readonly byte[] SnapshotMagic = { (byte)'F', (byte)'W', (byte)'I', (byte)'1' };

    public static event Action<Item> RuntimeStateChanged;
    public static Func<bool> ShouldDeferLocalDestruction;
    public static Func<Mod_ItemPicker, Item, bool> TryBeginNetworkPickup;
    public static Func<Mod_Building, Vector3, bool> TryBeginNetworkBuilding;
    public static Func<Mod_Building, bool> TryBeginNetworkBuildingDismantle;
    public static Func<MachineEntity, bool> TryBeginNetworkMechanicalDismantle;

    public static void NotifyRuntimeStateChanged(Item item)
    {
        if (item != null)
            RuntimeStateChanged?.Invoke(item);
    }

    public static bool DeferLocalDestruction()
        => ShouldDeferLocalDestruction?.Invoke() == true;

    public static bool BeginNetworkPickup(Mod_ItemPicker picker, Item worldItem)
        => TryBeginNetworkPickup?.Invoke(picker, worldItem) == true;

    public static bool BeginNetworkBuilding(Mod_Building building, Vector3 position)
        => TryBeginNetworkBuilding?.Invoke(building, position) == true;

    public static bool BeginNetworkBuildingDismantle(Mod_Building building)
        => TryBeginNetworkBuildingDismantle?.Invoke(building) == true;

    /// <summary>纯数据机械的拆除请求交由联机协调器发送。</summary>
    public static bool BeginNetworkMechanicalDismantle(MachineEntity node)
        => TryBeginNetworkMechanicalDismantle?.Invoke(node) == true;

    public static byte[] Capture(Item item, bool ignoreTransform)
    {
        if (item == null || item.itemData == null)
            return Array.Empty<byte>();

        item.ModuleSave();
        // 捕获阶段直接生成脱离对象的公开状态，不暂改活跃数据或位姿。
        return SerializeSnapshot(ItemInstanceSnapshot.Capture(item.itemData, !ignoreTransform,
            item is Player, MachineInventoryCommands.PublicSpecialData));
    }

    public static bool IsValidPayload(byte[] payload)
    {
        if (payload == null || payload.Length <= SnapshotMagic.Length || payload.Length > MaxSnapshotBytes) return false;
        for (int i = 0; i < SnapshotMagic.Length; i++)
            if (payload[i] != SnapshotMagic[i]) return false;
        return true;
    }

    public static bool TryReadIdentity(byte[] payload, out int guid, out string itemId)
    {
        guid = 0;
        itemId = null;
        if (!TryDeserialize(payload, out ItemInstanceSnapshot state, validateRuntimeState: false))
            return false;

        guid = state.Guid;
        itemId = state.DefinitionId;
        return true;
    }

    public static bool TryDeserializeItemData(byte[] payload, out ItemData itemData)
    {
        itemData = null;
        if (!TryDeserialize(payload, out ItemInstanceSnapshot snapshot, validateRuntimeState: false)) return false;
        try
        {
            ItemData coldData = snapshot.CreateColdData();
            GameRes resources = GameRes.ExistingInstance;
            if (resources == null) return false;
            if (coldData is Data_Player)
                ItemInstanceDataFactory.ApplyCurrentDefinition(coldData,
                    resources.GetPrefab("Player").GetComponent<Player>().Get_NewItemData());
            else
            {
                ItemDefinitionRuntime.RebasePersistedData(resources, coldData);
            }
            if (coldData.SharedConfiguration == null) return false;
            ItemDefinitionRuntime.RebaseNestedPersistedItems(resources, coldData, onlyColdData: true);
            itemData = coldData;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[联机物品] 快照恢复失败：{exception.Message}");
            return false;
        }
    }

    public static bool TrySerializeItemData(ItemData itemData, out byte[] payload)
    {
        payload = null;
        if (itemData == null)
            return false;

        try
        {
            payload = SerializeSnapshot(ItemInstanceSnapshot.Capture(itemData));
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
                out RuntimeStateBaseline baseline))
            return false;
        if (!ApplyMergedRuntimeStates(target, baseline, reloadRuntimeModules)) return false;
        target.OnUIRefresh?.Invoke();
        return IsCurrentItem(target, current, baseline.ItemGeneration);
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
        if (!TryMergeSnapshot(target, payload, false, out _, out RuntimeStateBaseline baseline))
            return false;
        return ApplyMergedRuntimeStates(target, baseline, reloadRuntimeModules: false, runtimeApplier: runtimeApplier);
    }

    /// <summary>在热恢复前捕获身份与模块状态，后续回调不能接管回收后产生的新代实例。</summary>
    public sealed class RuntimeStateBaseline
    {
        internal readonly Item Item;
        internal readonly ItemData Data;
        internal readonly uint ItemGeneration;
        internal readonly List<(Module Module, uint Generation, ModuleInstanceSnapshot State)> Modules = new();

        internal RuntimeStateBaseline(Item item)
        {
            Item = item;
            Data = item.itemData;
            ItemGeneration = item.RuntimeGeneration;
            foreach (Module module in item.Mods.Values)
                if (module != null && module._Data != null)
                    Modules.Add((module, module.RuntimeGeneration, ModuleInstanceSnapshot.Capture(module._Data)));
        }
    }

    public static RuntimeStateBaseline CaptureRuntimeStateBaseline(Item item)
        => item != null && item.itemData != null ? new RuntimeStateBaseline(item) : null;

    /// <summary>原位恢复库存或网络数据后，只刷新进入恢复前已存在且状态变化的模块。</summary>
    public static bool ApplyMergedRuntimeStates(Item target, RuntimeStateBaseline baseline,
        bool reloadRuntimeModules, Action<Module, ModuleData> runtimeApplier = null)
    {
        if (baseline == null || !ReferenceEquals(target, baseline.Item) ||
            !IsCurrentItem(target, baseline.Data, baseline.ItemGeneration)) return false;
        foreach (var entry in baseline.Modules)
        {
            if (!IsCurrentItem(target, baseline.Data, baseline.ItemGeneration)) return false;
            Module module = entry.Module;
            if (!IsCurrentModule(target, module, entry.Generation)) continue;
            ModuleData state = FindModuleState(baseline.Data.ModuleDataDic, module.StableName);
            if (state == null || entry.State.Matches(state)) continue;
            if (runtimeApplier != null)
            {
                module.BindSnapshotData(target, state, baseline.Data);
                runtimeApplier(module, state);
            }
            else if (reloadRuntimeModules) module.ApplyNetworkData(state);
            else module.BindSnapshotData(target, state, baseline.Data);
        }
        return IsCurrentItem(target, baseline.Data, baseline.ItemGeneration);
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

            return AppendHash(hash, MemoryPackSerializer.Serialize(ModuleInstanceSnapshot.Capture(state)));
        }
    }

    private static byte[] SerializeSnapshot(ItemInstanceSnapshot snapshot)
    {
        snapshot.Validate();
        byte[] body = MemoryPackSerializer.Serialize(snapshot);
        if (body.Length + SnapshotMagic.Length > MaxSnapshotBytes)
            throw new InvalidOperationException("物品实例快照超过网络大小上限。");
        byte[] payload = new byte[SnapshotMagic.Length + body.Length];
        Buffer.BlockCopy(SnapshotMagic, 0, payload, 0, SnapshotMagic.Length);
        Buffer.BlockCopy(body, 0, payload, SnapshotMagic.Length, body.Length);
        return payload;
    }

    private static bool TryDeserialize(byte[] payload, out ItemInstanceSnapshot state, bool validateRuntimeState = true)
    {
        state = null;
        if (!IsValidPayload(payload))
            return false;

        try
        {
            byte[] body = new byte[payload.Length - SnapshotMagic.Length];
            Buffer.BlockCopy(payload, SnapshotMagic.Length, body, 0, body.Length);
            state = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(body);
            if (state == null) return false;
            state.Validate();
            if (validateRuntimeState) state.CreateColdData();
            return true;
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
        out RuntimeStateBaseline baseline)
    {
        current = target?.itemData;
        baseline = null;
        if (current == null || !TryDeserialize(payload, out ItemInstanceSnapshot incoming))
            return false;

        if (incoming.Kind != ItemInstanceSnapshot.GetKind(current) ||
            !string.Equals(incoming.DefinitionId, current.IDName, StringComparison.Ordinal) ||
            (requireMatchingGuid && incoming.Guid != current.Guid))
        {
            return false;
        }

        try
        {
            uint itemGeneration = target.RuntimeGeneration;
            baseline = CaptureRuntimeStateBaseline(target);
            string privateState = target is Player ? current.ItemSpecialData : null;
            // 活跃实例已在生成时挂接本地定义，状态包不能重建其当前模块布局。
            incoming.RestoreTo(current, preserveTransform: true, preserveGuid: true);
            if (!IsCurrentItem(target, current, itemGeneration)) return false;
            ItemDefinitionRuntime.RebaseNestedPersistedItems(GameRes.ExistingInstance, current, onlyColdData: true);
            if (!IsCurrentItem(target, current, itemGeneration)) return false;
            if (target is Player)
                current.ItemSpecialData = MachineInventoryCommands.MergePrivateSpecialData(privateState, current.ItemSpecialData);
            target.MarkModuleScheduleDirty();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[联机物品] 合并实例状态失败：{exception.Message}");
            return false;
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

    private static ModuleData FindModuleState(
        Dictionary<string, ModuleData> states,
        string stableName)
    {
        if (states == null || string.IsNullOrWhiteSpace(stableName))
            return null;

        return states.TryGetValue(stableName, out ModuleData exact) ? exact : null;
    }

    private static bool IsCurrentItem(Item target, ItemData data, uint generation) => target != null &&
        !target.DestructionHandled && !target.IsInPool && target.RuntimeGeneration == generation && ReferenceEquals(target.itemData, data);

    private static bool IsCurrentModule(Item target, Module module, uint generation) => module != null &&
        module.RuntimeGeneration == generation && !string.IsNullOrWhiteSpace(module.StableName) &&
        target.Mods.TryGetValue(module.StableName, out Module registered) && ReferenceEquals(module, registered);

    #endregion
}
